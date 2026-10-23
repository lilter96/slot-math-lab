import {test, expect, postWithQuota, saveValidatedDialog, waitForWorkspace, type Page} from './fixtures';
import {readFile} from 'node:fs/promises';
import {
    collectionRepeatCalls,
    repeatCollection,
    type CollectionRepeatSpec
} from '../src/lib/authoring/collectionRepeat';
import type {ModelGraph, ModelNode} from '../src/lib/modelProjection';

const port = {state: {name: 'state', type: 'State'}};
const s = (key: string) => ({exprType: 'fieldAccess', target: 'state', path: [key]});
const n = (value: number) => ({exprType: 'constant', kind: 'Integer', value: String(value)});
const add = (left: unknown, right: unknown) => ({exprType: 'binary', op: 'Add', left, right});
const call = (id: string, mechanicName: string): ModelNode => ({
    nodeType: 'library',
    id,
    label: 'Award one item',
    mechanicName,
    parameters: {scale: '2'},
    inputs: port,
    outputs: port
});
const write = (id: string, outputKey: string, expressionId = id): ModelNode => ({
    nodeType: 'modifyState',
    id,
    outputKey,
    expressionId,
    inputs: port,
    outputs: port
});
const edges = (nodes: ModelNode[]) => nodes.slice(0, -1).map((node, index) => ({
    id: `edge-${index}`,
    sourceNodeId: node.id,
    sourcePort: 'state',
    targetNodeId: nodes[index + 1].id,
    targetPort: 'state'
}));
const body = () => {
    const nodes = [write('award', 'win'), write('change-source', 'items')];
    return {
        name: 'award',
        nodes,
        edges: edges(nodes),
        expressions: {
            award: add(s('win'), {
                exprType: 'binary',
                op: 'Mul',
                left: s('currentItem'),
                right: {exprType: 'fieldAccess', target: 'state', path: ['expressions', 'scale']}
            }), 'change-source': s('replacement')
        }
    };
};

function model(items = [2, 3, 5]): ModelGraph {
    const nodes = [call('call', 'award'), {
        nodeType: 'metricsSink',
        id: 'sink',
        winStateKey: 'win',
        winCap: 1000,
        inputs: port
    }];
    return {
        schemaVersion: '1.0.0',
        name: 'Collection control',
        initialState: {items, replacement: [99], win: 0},
        nodes,
        edges: edges(nodes),
        mechanics: {award: body()},
        opaque: {retained: true}
    };
}

const spec = (graph: ModelGraph, maxItems = 3): CollectionRepeatSpec => ({
    call: collectionRepeatCalls(graph)[0].key,
    name: 'ordered-items',
    sourceKey: 'items',
    itemKey: 'currentItem',
    indexKey: 'currentIndex',
    itemType: 'Number',
    maxItems
});
const numeric = (value: unknown): number => typeof value === 'object' && value && 'numerator' in value ? Number(value.numerator) : Number(value);

async function exported(page: Page) {
    const download = page.waitForEvent('download');
    await page.getByRole('button', {name: 'Export project', exact: true}).click();
    return JSON.parse(await readFile((await (await download).path())!, 'utf8')) as ModelGraph;
}

test('Generated collection graph snapshots mutations, visits exact bounds and stops before empty bodies', async ({request}) => {
    for (const items of [[2, 3, 5], [], [7]]) {
        const graph = model(items), candidate = repeatCollection(graph, spec(graph));
        const response = await postWithQuota(request, '/api/play/round', {data: {config: candidate, seed: 42}});
        expect(response.ok(), await response.text()).toBe(true);
        const result = await response.json();
        expect(result.winNumerator).toBe(String(items.reduce((sum, value) => sum + 2 * value, 0)));
        expect(result.state['__exitReason_call/each__']).toBe('condition');
        if (items.length) {
            expect(result.state.items.map(numeric)).toEqual([99]);
            expect(numeric(result.state.currentItem)).toBe(items.at(-1));
            expect(numeric(result.state.currentIndex)).toBe(items.length - 1);
        } else expect(result.state).not.toHaveProperty('currentItem');
    }
    const graph = model(), response = await postWithQuota(request, '/api/play/round', {
        data: {
            config: repeatCollection(graph, spec(graph, 2)),
            seed: 42
        }
    });
    expect(response.status()).toBe(400);
    expect(await response.text()).toContain('bound');
});

test('Nested generated repeats keep distinct outer bindings across all inner visits', async ({request}) => {
    const graph = model([2, 3]);
    graph.initialState = {items: [2, 3], innerItems: [10, 20], win: 0};
    graph.mechanics = {
        outer: {name: 'outer', nodes: [call('inner', 'inner')], edges: []}, inner: {
            name: 'inner',
            nodes: [write('award', 'win')],
            edges: [],
            expressions: {
                award: add(s('win'), {
                    exprType: 'binary',
                    op: 'Mul',
                    left: s('outerItem'),
                    right: s('innerItem')
                })
            }
        }
    };
    graph.nodes![0].mechanicName = 'outer';
    const inner = repeatCollection(graph, {
        ...spec(graph),
        call: JSON.stringify(['outer', 'inner']),
        name: 'inner-repeat',
        sourceKey: 'innerItems',
        itemKey: 'innerItem',
        indexKey: 'innerIndex',
        maxItems: 2
    });
    const candidate = repeatCollection(inner, {
        ...spec(inner),
        name: 'outer-repeat',
        itemKey: 'outerItem',
        indexKey: 'outerIndex',
        maxItems: 2
    });
    const response = await postWithQuota(request, '/api/play/round', {data: {config: candidate, seed: 1}});
    expect(response.ok(), await response.text()).toBe(true);
    const result = await response.json();
    expect(result.winNumerator).toBe('150');
    expect(numeric(result.state.outerItem)).toBe(3);
    expect(numeric(result.state.innerItem)).toBe(20);
});


test('Array and record items retain their structured binding types', async ({request}) => {
    for (const record of [false, true]) {
        const graph = model();
        graph.initialState = {items: record ? [{v: 2}, {v: 3}] : [[2], [3]], win: 0};
        graph.mechanics!.award = {
            name: 'award',
            nodes: [write('award', 'win')],
            edges: [],
            expressions: {
                award: add(s('win'), record ? {
                    exprType: 'fieldAccess',
                    target: 'state',
                    path: ['currentItem', 'v']
                } : {exprType: 'call', function: 'index', args: [s('currentItem'), n(0)]})
            }
        };
        const candidate = repeatCollection(graph, {...spec(graph), itemType: record ? 'Record' : 'Array'});
        const response = await postWithQuota(request, '/api/play/round', {data: {config: candidate, seed: 42}});
        expect(response.ok(), await response.text()).toBe(true);
        expect((await response.json()).winNumerator).toBe('5');
    }
});

test('Repeat authoring previews, validates, exports editable JSON and undoes one complete edit', async ({
                                                                                                            page,
                                                                                                            request
                                                                                                        }, testInfo) => {
    const graph = model();
    await page.goto('/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(graph))).toString('base64')));
    await waitForWorkspace(page);
    const original = await exported(page);
    await page.getByRole('button', {name: 'Repeat over collection', exact: true}).click();
    const dialog = page.getByRole('dialog', {name: 'Repeat over collection', exact: true});
    await dialog.getByLabel('Collection state key', {exact: true}).fill('items');
    await dialog.getByLabel('Maximum items', {exact: true}).fill('3');
    await dialog.getByRole('button', {name: 'Preview collection repeat', exact: true}).click();
    await expect(dialog.getByRole('heading', {name: 'One body, up to 3 items'})).toBeVisible();
    const viewport = page.viewportSize();
    for (const size of [{width: 1440, height: 1000}, {width: 390, height: 844}]) {
        await page.setViewportSize(size);
        await expect(dialog).toBeInViewport();
        expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
        await dialog.getByLabel('Subgraph call').scrollIntoViewIfNeeded();
        await testInfo.attach(`collection-repeat-form-${size.width}`, {
            body: await page.screenshot({fullPage: true}),
            contentType: 'image/png'
        });
        await dialog.getByText('Inspect the generated graph', {exact: true}).click();
        await dialog.locator('pre').scrollIntoViewIfNeeded();
        expect(await dialog.locator('pre').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
        await testInfo.attach(`collection-repeat-expanded-${size.width}`, {
            body: await page.screenshot({fullPage: true}),
            contentType: 'image/png'
        });
        await dialog.getByText('Inspect the generated graph', {exact: true}).click();
        await dialog.getByRole('button', {name: 'Apply collection repeat', exact: true}).scrollIntoViewIfNeeded();
        await testInfo.attach(`collection-repeat-footer-${size.width}`, {
            body: await page.screenshot({fullPage: true}),
            contentType: 'image/png'
        });
    }
    if (viewport) await page.setViewportSize(viewport);
    await saveValidatedDialog(page, 'Apply collection repeat');
    const candidate = await exported(page);
    expect(candidate.mechanics!.award).toEqual(original.mechanics!.award);
    expect(candidate.edges).toEqual(original.edges);
    const response = await postWithQuota(request, '/api/play/round', {data: {config: candidate, seed: 42}});
    expect(response.ok(), await response.text()).toBe(true);
    expect((await response.json()).winNumerator).toBe('20');
    await page.getByRole('button', {name: 'Undo collection repeat', exact: true}).click();
    expect(await exported(page)).toEqual(original);
});

test('Repeated stochastic bodies retain ordered draws and the following categorical choice', async ({request}) => {
    const graph = model(), nodes = graph.mechanics!.award.nodes;
    nodes.splice(1, 0,
        {
            nodeType: 'draw',
            id: 'choice',
            stateWriteKey: 'choice',
            drawWeights: [{outcomeId: 'A', weight: 1, value: 0}, {outcomeId: 'B', weight: 3, value: 0}, {
                outcomeId: 'C',
                weight: 5,
                value: 0
            }],
            inputs: port,
            outputs: port
        },
        write('remember', 'seen'));
    graph.mechanics!.award.edges = edges(nodes);
    (graph.mechanics!.award.expressions as Record<string, unknown>).remember = {
        exprType: 'call',
        function: 'append',
        args: [s('seen'), s('choice')]
    };
    (graph.initialState as Record<string, unknown>).seen = [];
    const following: ModelNode = {
        nodeType: 'draw',
        id: 'following',
        stateWriteKey: 'following',
        drawWeights: Array.from({length: 17}, (_, index) => ({outcomeId: String(index), weight: index + 1, value: 0})),
        inputs: port,
        outputs: port
    };
    graph.nodes!.splice(1, 0, following);
    graph.edges = edges(graph.nodes!);
    const candidate = repeatCollection(graph, spec(graph));
    const reference = structuredClone(graph);
    reference.expressions = {};
    reference.nodes = [2, 3, 5].flatMap((value, index) => {
        (reference.expressions as Record<string, unknown>)[`bind-${index}`] = n(value);
        return [write(`bind-${index}`, 'currentItem'), call(`body-${index}`, 'award')];
    });
    reference.nodes.push(following, graph.nodes!.at(-1)!);
    reference.edges = edges(reference.nodes);
    for (const seed of [0, 1, 2, 3, 7, 17, 42, 123, 65537, -1, -7, -1234567]) {
        const results = [];
        for (const config of [reference, candidate]) {
            const response = await postWithQuota(request, '/api/play/round', {data: {config, seed}});
            expect(response.ok(), await response.text()).toBe(true);
            results.push(await response.json());
        }
        expect(results[1].winNumerator).toBe('20');
        for (const field of ['seen', 'following', 'choice', 'currentItem', 'items']) expect(results[1].state[field]).toEqual(results[0].state[field]);
    }
});

test('Closing a pending repeat validation cannot apply a late response and restores keyboard focus', async ({page}) => {
    const graph = model();
    await page.goto('/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(graph))).toString('base64')));
    await waitForWorkspace(page);
    const original = await exported(page);
    const trigger = page.getByRole('button', {name: 'Repeat over collection', exact: true});
    await trigger.click();
    const dialog = page.getByRole('dialog', {name: 'Repeat over collection', exact: true});
    await dialog.getByLabel('Collection state key', {exact: true}).fill('items');
    await dialog.getByRole('button', {name: 'Preview collection repeat', exact: true}).click();
    let release!: () => void, started!: () => void, handled!: () => void;
    const gate = new Promise<void>(resolve => {
        release = resolve;
    }), accepted = new Promise<void>(resolve => {
        started = resolve;
    }), settled = new Promise<void>(resolve => {
        handled = resolve;
    });
    await page.route('**/api/validate', async route => {
        started();
        await gate;
        try {
            await route.fulfill({status: 200, contentType: 'application/json', body: '{"isValid":true,"errors":[]}'});
        } catch { /* Closing deliberately aborts this validation. */
        } finally {
            handled();
        }
    });
    await dialog.getByRole('button', {name: 'Apply collection repeat', exact: true}).click();
    await accepted;
    await page.keyboard.press('Escape');
    await expect(dialog).not.toBeVisible();
    await expect(trigger).toBeFocused();
    release();
    await settled;
    expect(await exported(page)).toEqual(original);
    await expect(page.getByRole('button', {name: 'Undo collection repeat', exact: true})).not.toBeVisible();
});
