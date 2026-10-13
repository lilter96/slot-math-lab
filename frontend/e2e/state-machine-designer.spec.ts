import {mkdir, readFile, writeFile} from 'node:fs/promises';
import {expect, type Page, test} from './fixtures';
import {
    machineProofGraph,
    priorityMachine,
    selfMachine,
    sequenceMachine,
    weightedMachine
} from '../unit/fixtures/machineProofs';
import {graphStudiosCorpus} from '../unit/fixtures/graphStudiosCorpus';
import {controlSource, projectControl} from '../src/lib/authoring/controlProjection';
import {readMachineSource} from '../src/lib/authoring/machineSource';
import {changeControlSource} from '../src/lib/authoring/controlEdits';
import {changeRespinCount, respinContracts} from '../src/lib/authoring/respinContract';
import {sourceKey} from '../src/lib/authoring/sourceIndex';
import {num} from '../src/games/shared/graph/expr';
import type {ModelGraph} from '../src/lib/modelProjection';

async function open(page: Page, graph: ModelGraph, scope?: string) {
    test.setTimeout(Math.max(test.info().timeout, 180000));
    await page.goto('/build');
    await page.getByLabel('Import project file').waitFor({state: 'attached', timeout: 70000});
    await page.getByLabel('Import project file').setInputFiles({
        name: 'machine-graph.json',
        mimeType: 'application/json',
        buffer: Buffer.from(JSON.stringify(graph))
    });
    await expect(page.locator('.project-name')).toHaveText(String(graph.name));
    await page.goto('/model/machine' + (scope ? `?scope=${encodeURIComponent(scope)}` : ''));
    await expect(page.getByRole('heading', {
        name: 'State Machine Designer',
        exact: true
    })).toBeVisible({timeout: 70000});
    await expect(page.getByRole('button', {name: 'Create / reuse', exact: true})).toBeEnabled({timeout: 70000});
}

async function backup(page: Page): Promise<{ graph: ModelGraph; views: unknown }> {
    const pending = page.waitForEvent('download');
    await page.getByRole('button', {name: 'Back up project', exact: true}).click();
    return JSON.parse(await readFile((await (await pending).path())!, 'utf8'));
}

async function apply(page: Page) {
    await page.getByRole('button', {name: 'Validate & apply', exact: true}).click();
    await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeEnabled({timeout: 70000});
    await visibleStates(page);
}

async function visibleStates(page: Page) {
    if (await page.locator('.machine-canvas').count()) await expect.poll(() => page.locator('.react-flow__node').evaluateAll(nodes => nodes.length > 0 && nodes.every(node => {
        const bounds = node.getBoundingClientRect();
        return getComputedStyle(node).visibility === 'visible' && bounds.width > 0 && bounds.height > 0;
    })), {message: 'Applied and undone states keep measured dimensions and remain visible'}).toBe(true);
}

async function chooseState(page: Page, label: string) {
    await page.locator('.machine-explorer section').getByRole('button', {name: label, exact: false}).click();
    await page.waitForTimeout(250);
}

test('Authored action edit survives reload, applies once, runs raw graph, exports/reimports and undoes the whole source', async ({
                                                                                                                                     page,
                                                                                                                                     request
                                                                                                                                 }) => {
    const graph = machineProofGraph(sequenceMachine());
    await open(page, graph, 'sequence-machine');
    await chooseState(page, 'Action A');
    const editor = page.getByLabel('Expression for A', {exact: true});
    await editor.fill('coins + 5');
    await editor.locator('..').locator('..').getByRole('button', {name: 'Use expression', exact: true}).click();
    await expect(page.getByRole('button', {name: 'Validate & apply', exact: true})).toBeEnabled();
    await page.reload();
    await expect(page.getByRole('button', {name: 'Validate & apply', exact: true})).toBeEnabled();
    await apply(page);
    const saved = await backup(page);
    expect(readMachineSource(saved.graph, 'sequence-machine')!.machine.states[0].actions[0]).toMatchObject({kind: 'assign'});
    const response = await request.post('/api/play/round', {data: {config: saved.graph, seed: 42, roundIndex: 0}});
    expect(response.ok()).toBe(true);
    expect((await response.json()).win).toBe(115);
    await page.getByRole('button', {name: 'Undo', exact: true}).click();
    await visibleStates(page);
    expect((await backup(page)).graph).toEqual(graph);
    await open(page, saved.graph, 'sequence-machine');
    await expect(page.locator('.machine-canvas-node')).toHaveCount(3);
    expect((await backup(page)).graph).toEqual(saved.graph);
});
test('Create a connected feature, author its action and execute through one original settlement sink', async ({
                                                                                                                  page,
                                                                                                                  request
                                                                                                              }) => {
    const graph = machineProofGraph(sequenceMachine());
    await open(page, graph);
    await page.getByRole('button', {name: 'Create / reuse', exact: true}).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('New machine instance ID').fill('my-feature');
    const edge = (graph.edges as { id: string }[])[0];
    await dialog.getByLabel('New machine connection').selectOption(JSON.stringify({
        kind: 'edge',
        scope: null,
        edgeId: edge.id
    }));
    await dialog.getByRole('button', {name: 'Stage feature', exact: true}).click();
    const panel = page.getByRole('complementary', {name: 'Selected state editor'});
    await panel.getByRole('button', {name: 'Add assignment', exact: true}).first().click();
    const expression = panel.getByLabel('Expression for Set variable', {exact: true});
    await expression.fill('7');
    await expression.locator('..').locator('..').getByRole('button', {name: 'Use expression', exact: true}).click();
    await apply(page);
    const saved = await backup(page);
    expect(saved.graph.nodes!.filter(node => node.nodeType === 'metricsSink')).toHaveLength(1);
    const machine = readMachineSource(saved.graph, 'my-feature')!;
    expect(machine).not.toBeNull();
    const response = await request.post('/api/play/round', {data: {config: saved.graph, seed: 42, roundIndex: 0}});
    expect(response.ok()).toBe(true);
    const result = await response.json();
    expect(result.win).toBe(111);
    const privateCounter = Object.entries(result.state).find(([key]) => key.startsWith('__machine_') && key.endsWith('_private_636f756e746572'));
    expect(privateCounter).toBeDefined();
    expect(Number((privateCounter![1] as { displayValue?: number })?.displayValue ?? privateCounter![1])).toBe(7);
    test.setTimeout(Math.max(test.info().timeout, 180000));
    await page.goto('/build');
    await page.getByLabel('Import project file').waitFor({state: 'attached', timeout: 70000});
    await page.getByLabel('Import project file').setInputFiles({
        name: 'graph-only.json',
        mimeType: 'application/json',
        buffer: Buffer.from(JSON.stringify(saved.graph))
    });
    await page.goto('/model/machine?scope=my-feature');
    await expect(page.locator('.machine-canvas-node')).toHaveCount(2);
});
test('An independent machine use gets a private namespace and remains editable in the table', async ({
                                                                                                         page,
                                                                                                         request
                                                                                                     }) => {
    const graph = machineProofGraph(selfMachine());
    await open(page, graph, 'self-machine');
    await page.getByRole('button', {name: /Current round \/.*make independent/}).click();
    await apply(page);
    const saved = await backup(page), target = String(saved.graph.nodes![0].mechanicName);
    expect(target).not.toBe('self-machine');
    expect(readMachineSource(saved.graph, target)).not.toBeNull();
    expect(saved.graph.mechanics!['self-machine']).toEqual(graph.mechanics!['self-machine']);
    const response = await request.post('/api/play/round', {data: {config: saved.graph, seed: 42, roundIndex: 0}});
    expect(response.ok()).toBe(true);
    expect((await response.json()).win).toBe(12);
    await page.goto(`/model/machine?scope=${target}`);
    await page.getByRole('tab', {name: 'Transition table', exact: true}).click();
    await expect(page.locator('.machine-transition-table')).toBeVisible();
    await page.locator('.machine-transition-table').getByRole('button', {name: 'Visit', exact: true}).first().click();
    await expect(page.getByLabel('State name', {exact: true})).toHaveValue('Visit');
});
test('An imported feature use can get an independent definition without replacing its shared game fields or paid-entry contract', async ({
                                                                                                                                             page,
                                                                                                                                             request
                                                                                                                                         }) => {
    const graph = graphStudiosCorpus().graphs.find(value => value.caseId === 'ancient-dragon/ordinary')!.graph;
    await open(page, graph, 'free-spins');
    await page.getByRole('button', {name: /Current round \/.*make independent/}).click();
    await apply(page);
    const saved = await backup(page);
    expect(saved.graph.mechanics!['free-spins']).toEqual(graph.mechanics!['free-spins']);
    for (const [key, value] of Object.entries(graph.initialState as Record<string, unknown>)) expect((saved.graph.initialState as Record<string, unknown>)[key]).toEqual(value);
    const copied = saved.graph.nodes!.find(node => node.nodeType === 'library' && String(node.mechanicName).startsWith('private-feature-'));
    expect(copied).toBeDefined();
    const original = await request.post('/api/play/round', {data: {config: graph, seed: 42, roundIndex: 0}}),
        after = await request.post('/api/play/round', {data: {config: saved.graph, seed: 42, roundIndex: 0}});
    expect(original.ok() && after.ok()).toBe(true);
    const before = await original.json(), result = await after.json();
    expect(result.win).toBe(before.win);
    expect(result.state.board).toEqual(before.state.board);
});
test('Weighted actions and transition priority change actual exact and seeded results through the same machine editors', async ({
                                                                                                                                    page,
                                                                                                                                    request
                                                                                                                                }) => {
    await open(page, machineProofGraph(weightedMachine()), 'weighted-machine');
    await chooseState(page, 'Choose once');
    const rows = page.locator('.machine-draw-rows');
    await rows.getByLabel('Weight', {exact: true}).first().fill('3');
    await apply(page);
    const weighted = await backup(page), exact = await request.post('/api/evaluate/light', {
        data: {
            config: weighted.graph,
            maxBranches: 10000,
            sampleSize: 1000
        }
    });
    expect(exact.ok()).toBe(true);
    expect(await exact.json()).toMatchObject({strategy: 'Exact', rtp: 2});
    await open(page, machineProofGraph(priorityMachine()), 'priority-machine');
    await chooseState(page, 'Choose');
    const transition = page.locator('.machine-action-row').filter({has: page.locator('summary').filter({hasText: 'Second true guard must not run'})});
    await transition.locator(':scope > summary').click();
    await transition.getByRole('button', {name: 'Raise priority', exact: true}).click();
    await apply(page);
    const changed = await backup(page);
    expect(readMachineSource(changed.graph, 'priority-machine')!.machine.states[0].transitions[0].id).toBe('second');
    const round = await request.post('/api/play/round', {data: {config: changed.graph, seed: 42, roundIndex: 0}});
    expect(round.ok()).toBe(true);
    const result = await round.json();
    expect(result.win).toBe(1000);
    expect(Number(result.state.coins?.displayValue ?? result.state.coins)).toBe(1009);
});
test('Compiler rejection and cancellation retain the private source and pending inputs', async ({page}) => {
    const graph = machineProofGraph(sequenceMachine());
    await open(page, graph, 'sequence-machine');
    await chooseState(page, 'Action A');
    const expression = page.getByLabel('Expression for A', {exact: true});
    await expression.fill('coins + 5');
    await expression.locator('..').locator('..').getByRole('button', {name: 'Use expression', exact: true}).click();
    await page.route('**/api/validate', route => route.fulfill({
        json: {
            isValid: false,
            errors: [{message: 'Intentional compiler rejection'}]
        }
    }));
    await page.getByRole('button', {name: 'Validate & apply', exact: true}).click();
    await expect(page.getByRole('alert')).toContainText('Intentional compiler rejection');
    expect((await backup(page)).graph).toEqual(graph);
    await page.unroute('**/api/validate');
    let release!: () => void;
    const waiting = new Promise<void>(resolve => {
        release = resolve;
    });
    await page.route('**/api/validate', async route => {
        await waiting;
        await route.fulfill({json: {isValid: true}}).catch(() => {
        });
    });
    await page.getByRole('button', {name: 'Validate & apply', exact: true}).click();
    await page.getByRole('button', {name: 'Cancel validation', exact: true}).click();
    release();
    await expect(page.getByRole('button', {name: 'Validate & apply', exact: true})).toBeEnabled();
    expect((await backup(page)).graph).toEqual(graph);
    await page.reload();
    await expect(page.getByRole('button', {name: 'Validate & apply', exact: true})).toBeEnabled();
});
test('A pending designer draft cannot overwrite direct source edits made in Logic Graph', async ({page}) => {
    await open(page, machineProofGraph(sequenceMachine()), 'sequence-machine');
    await chooseState(page, 'Action A');
    const expression = page.getByLabel('Expression for A', {exact: true});
    await expression.fill('coins + 5');
    await expression.locator('..').locator('..').getByRole('button', {name: 'Use expression', exact: true}).click();
    await page.getByRole('button', {name: 'Open graph', exact: true}).click();
    await page.locator('.react-flow__node').filter({hasText: 'Sequence proof'}).first().click();
    await page.getByLabel('Label', {exact: true}).fill('Direct graph edit');
    await page.goto('/model/machine?scope=sequence-machine');
    await expect(page.getByRole('button', {name: 'Validate & apply', exact: true})).toBeDisabled();
    await expect(page.getByRole('alert')).toContainText('current graph changed');
    const graph = (await backup(page)).graph;
    expect(graph.mechanics!['sequence-machine'].nodes[0].label).toBe('Direct graph edit');
    await page.getByRole('button', {name: 'Discard pending changes', exact: true}).click();
    expect((await backup(page)).graph).toEqual(graph);
});
for (const caseId of ['ancient-dragon/ordinary', 'woodland-whisper/bought', 'x7-club/bought', 'nine-lives-tgslots/bought', 'le-militare/siege/spin']) test(`Existing ${caseId}: real source edit → graph → seeded execution → raw export/import`, async ({
                                                                                                                                                                                                                                                             page,
                                                                                                                                                                                                                                                             request
                                                                                                                                                                                                                                                         }) => {
    const graph = graphStudiosCorpus().graphs.find(value => value.caseId === caseId)!.graph;
    if (caseId.startsWith('x7')) {
        const binding = respinContracts(graph)[0];
        await open(page, graph, binding.scope);
        await page.getByLabel('Initial and reset respins').fill('2');
        await apply(page);
        const saved = await backup(page), expected = changeRespinCount(graph, binding, 2);
        expect(saved.graph).toEqual(expected);
        expect(saved.graph.initialState).toEqual(graph.initialState);
        const response = await request.post('/api/play/round', {data: {config: saved.graph, seed: 42, roundIndex: 0}});
        expect(response.ok(), await response.text()).toBe(true);
        await open(page, saved.graph, binding.scope);
        expect((await backup(page)).graph).toEqual(saved.graph);
        return;
    }
    const scopes = [null, ...Object.keys(graph.mechanics ?? {})],
        scope = scopes.find(scope => controlSource(graph, scope).nodes?.some(node => node.nodeType === 'modifyState' && node.outputKey === 'freeSpins' && node.id !== 'use' && node.id !== 'retrigger'))!,
        owner = controlSource(graph, scope),
        node = owner.nodes!.find(node => node.nodeType === 'modifyState' && node.outputKey === 'freeSpins' && node.id !== 'use' && node.id !== 'retrigger')!,
        region = projectControl(graph, scope).regions.find(region => region.actions.some(action => action.id === node.id))!;
    await open(page, graph, scope ?? undefined);
    await chooseState(page, region.label);
    const expression = page.getByLabel('Assignment to freeSpins', {exact: true});
    await expression.fill('1');
    await expression.locator('..').locator('..').getByRole('button', {name: 'Use expression', exact: true}).click();
    await apply(page);
    const saved = await backup(page),
        expected = changeControlSource(graph, scope, projectControl(graph, scope).revision, [{
            kind: 'expression',
            nodeKey: sourceKey(scope, node.id),
            field: 'expressionId',
            value: num(1)
        }], 'keep');
    expect(saved.graph).toEqual(expected);
    expect(saved.graph.initialState).toEqual(graph.initialState);
    expect(saved.graph.nodes!.find(node => node.nodeType === 'metricsSink')).toEqual(graph.nodes!.find(node => node.nodeType === 'metricsSink'));
    const response = await request.post('/api/play/round', {data: {config: saved.graph, seed: 42, roundIndex: 0}});
    expect(response.ok(), await response.text()).toBe(true);
    expect((await response.json()).win).toBeGreaterThanOrEqual(0);
    await open(page, saved.graph, scope ?? undefined);
    expect((await backup(page)).graph).toEqual(saved.graph);
});
test('Full-page visual review captures all states, table, lower sections and responsive inspector', async ({page}) => {
    test.setTimeout(120000);
    const folder = '../docs/design/graph-studios/evidence/m4-designer-visual';
    await mkdir(folder, {recursive: true});
    const manifest: unknown[] = [];
    for (const viewport of [{width: 1440, height: 900}, {width: 1280, height: 800}, {
        width: 1024,
        height: 768
    }, {width: 390, height: 844}]) {
        await page.setViewportSize(viewport);
        await open(page, machineProofGraph(selfMachine()), 'self-machine');
        if (viewport.width < 768) await page.getByRole('button', {name: 'Features', exact: true}).click();
        await chooseState(page, 'Visit');
        await page.locator('.machine-action-row').evaluateAll(elements => elements.forEach(element => element.setAttribute('open', '')));
        await page.getByText('Source, callers and execution limits', {exact: true}).click();
        await page.locator('.machine-designer').evaluate(element => {
            element.scrollTop = 0;
        });
        const path = `${folder}/${viewport.width}-diagram.png`;
        await page.screenshot({path, fullPage: true});
        const surface = page.locator('.machine-designer');
        await surface.evaluate(element => {
            element.scrollTop = element.scrollHeight;
        });
        await page.screenshot({path: `${folder}/${viewport.width}-lower.png`, fullPage: true});
        const panel = page.locator('.machine-inspector');
        await panel.evaluate(element => {
            element.scrollTop = element.scrollHeight;
        });
        await panel.screenshot({path: `${folder}/${viewport.width}-inspector.png`});
        for (const [regionIndex, region] of [...await page.locator('.machine-designer,.machine-inspector,.machine-explorer').all()].entries()) {
            const size = await region.evaluate(element => ({
                height: element.clientHeight,
                total: element.scrollHeight
            }));
            if (size.total > size.height + 8) for (let y = 0; y < size.total; y += Math.max(300, Math.floor(size.height * 0.75))) {
                await region.evaluate((element, y) => {
                    element.scrollTop = y;
                }, y);
                await page.screenshot({path: `${folder}/${viewport.width}-region${regionIndex}-${y}.png`});
            }
        }
        await page.getByRole('tab', {name: 'Transition table', exact: true}).click();
        if (viewport.width < 768) await page.getByRole('button', {name: 'Features', exact: true}).click();
        await page.locator('.machine-designer').evaluate(element => {
            element.scrollTop = 0;
        });
        await page.screenshot({path: `${folder}/${viewport.width}-table.png`, fullPage: true});
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
        manifest.push({
            viewport,
            paths: [path, `${viewport.width}-lower.png`, `${viewport.width}-inspector.png`, `${viewport.width}-table.png`]
        });
    }
    await writeFile(`${folder}/capture.json`, JSON.stringify(manifest, null, 2));
});
test('Imported family visual review covers root, nested editor, table and lower evidence on desktop and phone', async ({page}) => {
    test.setTimeout(180000);
    const folder = '../docs/design/graph-studios/evidence/m5-imported-designer-visual';
    await mkdir(folder, {recursive: true});
    const manifest: unknown[] = [];
    for (const caseId of ['ancient-dragon/ordinary', 'woodland-whisper/bought', 'x7-club/bought', 'nine-lives-tgslots/bought', 'le-militare/siege/spin']) for (const width of [1440, 390]) {
        const graph = graphStudiosCorpus().graphs.find(value => value.caseId === caseId)!.graph,
            name = caseId.replaceAll('/', '-');
        await page.setViewportSize({width, height: width === 390 ? 844 : 900});
        await open(page, graph);
        await page.screenshot({path: `${folder}/${name}-${width}-root.png`});
        const scope = caseId.startsWith('x7') ? respinContracts(graph)[0].scope : Object.keys(graph.mechanics ?? {}).find(scope => graph.mechanics![scope].nodes.some(node => node.nodeType === 'modifyState' && node.outputKey === 'freeSpins' && node.id !== 'use' && node.id !== 'retrigger'))!;
        await page.goto(`/model/machine?scope=${encodeURIComponent(scope)}`);
        await expect(page.getByRole('heading', {
            name: 'State Machine Designer',
            exact: true
        })).toBeVisible({timeout: 70000});
        if (width === 390) await page.getByRole('button', {name: 'Features', exact: true}).click();
        const region = projectControl(graph, scope).regions.find(region => region.kind === 'loop') ?? projectControl(graph, scope).regions[0];
        await chooseState(page, region.label);
        if (width === 390 && await page.locator('.machine-explorer.mobile-open').count()) await page.getByRole('button', {
            name: 'Features',
            exact: true
        }).click();
        await page.locator('.machine-designer').evaluate(element => {
            element.scrollTop = 0;
        });
        const geometry = await page.locator('.machine-canvas').evaluate(element => ({
            viewport: element.querySelector('.react-flow__viewport')?.getAttribute('style'),
            nodes: [...element.querySelectorAll('.react-flow__node')].map(node => ({
                id: node.getAttribute('data-id'),
                transform: node.getAttribute('style'),
                rect: node.getBoundingClientRect().toJSON()
            }))
        }));
        await writeFile(`${folder}/${name}-${width}-geometry.json`, JSON.stringify(geometry, null, 2));
        await page.screenshot({path: `${folder}/${name}-${width}-nested.png`});
        await page.locator('.machine-inspector').evaluate(element => {
            element.scrollTop = element.scrollHeight;
        });
        await page.locator('.machine-inspector').screenshot({path: `${folder}/${name}-${width}-editor.png`});
        await page.locator('.machine-designer').evaluate(element => {
            element.scrollTop = element.scrollHeight;
        });
        await page.screenshot({path: `${folder}/${name}-${width}-lower.png`});
        await page.getByRole('tab', {name: 'Transition table', exact: true}).click();
        await page.locator('.machine-designer').evaluate(element => {
            element.scrollTop = 0;
        });
        await page.screenshot({path: `${folder}/${name}-${width}-table.png`});
        await expect.poll(async () => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
        manifest.push({caseId, width, scope, views: ['root', 'nested', 'editor', 'lower', 'table']});
    }
    await open(page, machineProofGraph(sequenceMachine()));
    await page.getByRole('button', {name: 'Create / reuse', exact: true}).click();
    await page.screenshot({path: `${folder}/390-create.png`});
    await page.getByRole('button', {name: 'Close machine creation', exact: true}).click();
    await page.setViewportSize({width: 1440, height: 900});
    await page.getByRole('button', {name: 'Create / reuse', exact: true}).click();
    await page.screenshot({path: `${folder}/1440-create.png`});
    await writeFile(`${folder}/capture.json`, JSON.stringify(manifest, null, 2));
});
