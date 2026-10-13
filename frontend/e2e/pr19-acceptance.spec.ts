import {mkdir, readFile, writeFile} from 'node:fs/promises';
import {type APIRequestContext, expect, type Page, test} from './fixtures';
import {Flow, gameGraph, Mechanics} from '../src/games/shared/graph/flow';
import {and, at, eq, num, str, when} from '../src/games/shared/graph/expr';
import {reelState, showingState, spinReelsShowing} from '../src/games/shared/graph/mechanics/reels';
import {graphStudiosCorpus} from '../unit/fixtures/graphStudiosCorpus';
import {machineProofGraph, weightedMachine} from '../unit/fixtures/machineProofs';
import {computedLineBoundProof} from '../unit/fixtures/computedLineBoundProof';
import {reelBindings} from '../src/lib/authoring/reelBindings';
import {payBindings} from '../src/lib/authoring/payBindings';
import {canonicalJson} from '../src/lib/hash';
import type {ModelGraph} from '../src/lib/modelProjection';

const corpus = graphStudiosCorpus().graphs;
const visualPhase = process.env.PR19_VISUAL_PHASE ?? 'after';
const visualOrigin = process.env.PR19_VISUAL_ORIGIN ?? '';
const evidence = '../docs/design/graph-studios/evidence/pr19-acceptance';

async function open(page: Page, graph: ModelGraph, route = '/model/studio', origin = '') {
    await page.goto(origin + '/build');
    await page.getByLabel('Import project file').waitFor({state: 'attached', timeout: 70000});
    await page.getByLabel('Import project file').setInputFiles({
        name: 'acceptance.json',
        mimeType: 'application/json',
        buffer: Buffer.from(JSON.stringify(graph))
    });
    await expect(page.locator('.project-name')).toHaveText(String(graph.name));
    await page.goto(origin + route);
    await expect(page.locator('.graph-studio,.machine-designer')).toBeVisible();
}

async function backup(page: Page): Promise<ModelGraph> {
    await page.getByRole('button', {
        name: 'Back up project',
        exact: true,
        includeHidden: true
    }).waitFor({state: 'attached'});
    const button = page.getByRole('button', {name: 'Back up project', exact: true});
    const opened = !await button.isVisible();
    const sourceToggle = page.getByRole('button', {name: 'Choose source', exact: true});
    const toggle = await sourceToggle.count() ? sourceToggle : page.getByRole('button', {
        name: 'Features',
        exact: true
    });
    if (opened) await toggle.click();
    const pending = page.waitForEvent('download');
    await button.click();
    const graph = JSON.parse(await readFile((await (await pending).path())!, 'utf8')).graph;
    if (opened) await toggle.click();
    return graph;
}

async function apply(page: Page) {
    await page.getByRole('button', {name: 'Validate & apply', exact: true}).click();
    await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeEnabled({timeout: 70000});
}

async function exact(request: APIRequestContext, graph: ModelGraph, expected: number) {
    const response = await request.post('/api/evaluate/light', {
        data: {
            config: graph,
            maxBranches: 10000,
            sampleSize: 1000
        }
    });
    expect(response.ok(), await response.text()).toBe(true);
    expect(await response.json()).toMatchObject({strategy: 'Exact'});
    expect((await response.json()).rtp).toBeCloseTo(expected, 12);
}

function conditionalToy(): ModelGraph {
    const reels = {name: 'conditional-toy-source', stripKey: 'strip', rows: 1, strips: [['0', '1'], ['0', '1']]};
    const showing = {name: reels.name, symbol: '1', symbolName: 'One', atLeast: 1};
    const mechanics = new Mechanics();
    const root = new Flow().use('spin', 'Conditioned draw', mechanics.add(spinReelsShowing(reels, showing)))
        .write('award', 'Both ones pay one', 'coins', when(and(eq(at('board', num(0)), str('1')), eq(at('board', num(1)), str('1'))), num(1), num(0)))
        .sink('sink', 'One-credit settlement', 'coins', 1);
    return gameGraph({
        id: 'pr19-conditional-law', name: 'Conditional law: one of three', root, mechanics,
        initialState: {
            emptyArray: [],
            board: ['0', '0'],
            coins: 0, ...reelState(reels), ...showingState(reels, showing)
        },
        tables: {boardConfig: {columns: 2, rows: 1}, symbols: [{id: '0', name: 'Zero'}, {id: '1', name: 'One'}]},
        opaque: {retain: 'independent conditional proof'}
    }) as ModelGraph;
}

async function importStrips(page: Page, rows: string) {
    await page.getByRole('button', {name: 'Import table', exact: true}).click();
    await page.getByLabel('CSV import input').fill('reel,index,symbolId,stopId,weight\n' + rows);
    await page.getByRole('button', {name: 'Preview exact values', exact: true}).click();
    await page.getByRole('button', {name: 'Stage import', exact: true}).click();
}

test('PR19 conditional law: mixed → all-one → export/reimport → mixed retains the condition and independently exact 1/3 payout', async ({
                                                                                                                                            page,
                                                                                                                                            request
                                                                                                                                        }) => {
    test.setTimeout(180000);
    const graph = conditionalToy();
    // Independent enumeration has three equiprobable boards: 01, 10, 11. Only 11 pays.
    await exact(request, graph, 1 / 3);
    await open(page, graph);
    await importStrips(page, '1,0,1,0,1\n2,0,1,0,1');
    await apply(page);
    const degenerate = await backup(page);
    expect(reelBindings(degenerate)[0].source.conditions![0].atLeast).toBe(1);
    await exact(request, degenerate, 1);
    await open(page, degenerate);
    expect(await backup(page)).toEqual(degenerate);
    await importStrips(page, '1,0,0,0,1\n1,1,1,1,1\n2,0,0,0,1\n2,1,1,1,1');
    await apply(page);
    const restored = await backup(page);
    expect(reelBindings(restored)[0].source.conditions![0].atLeast).toBe(1);
    expect(restored.opaque).toEqual(graph.opaque);
    await exact(request, restored, 1 / 3);
    for (const config of [degenerate, restored]) {
        const response = await request.post('/api/play/round', {data: {config, seed: 42, roundIndex: 0}});
        expect(response.ok(), await response.text()).toBe(true);
        const round = await response.json();
        expect(round.state.board).toContain('1');
        if (config === degenerate) expect(round.win).toBe(1);
    }
    await open(page, restored);
    expect(await backup(page)).toEqual(restored);
    await page.getByRole('button', {name: 'Import table', exact: true}).click();
    await page.getByLabel('CSV import input').fill('reel,index,symbolId,stopId,weight\n1,0,0,0,1\n2,0,0,0,1');
    await page.getByRole('button', {name: 'Preview exact values', exact: true}).click();
    await expect(page.getByRole('dialog')).toContainText('No reel stops');
    await page.getByRole('button', {name: 'Close import', exact: true}).click();
    expect(await backup(page)).toEqual(restored);
});

for (const caseId of ['nine-lives/reference', 'starter/cluster', 'starter/cascade']) test(`PR19 intentional ${caseId} payout edit remains executable and portable`, async ({
                                                                                                                                                                               page,
                                                                                                                                                                               request
                                                                                                                                                                           }) => {
    test.setTimeout(180000);
    const graph = corpus.find(value => value.caseId === caseId)!.graph;
    const binding = payBindings(graph).find(value => value.kind === 'cluster')!;
    expect(binding).toBeTruthy();
    const row = binding.rows.find(value => value.values.some(amount => amount !== '0'))!;
    const position = row.values.findIndex(amount => amount !== '0'), changedValue = '0';
    await open(page, graph);
    await page.getByRole('tab', {name: 'Paytables', exact: true}).click();
    await page.getByLabel(`cluster payout ${row.symbolId} count ${row.counts[position]}`, {exact: true}).fill(changedValue);
    await apply(page);
    const changed = await backup(page);
    expect(canonicalJson(changed)).not.toBe(canonicalJson(graph));
    const retained = payBindings(changed).find(value => value.tableId === binding.tableId)!;
    expect(retained.rows.find(value => value.symbolId === row.symbolId)!.values[position]).toBe(changedValue);
    expect(changed.nodes).toEqual(graph.nodes);
    const response = await request.post('/api/play/round', {data: {config: changed, seed: 42, roundIndex: 0}});
    expect(response.ok(), await response.text()).toBe(true);
    await open(page, changed);
    expect(await backup(page)).toEqual(changed);
});

test('PR19 compatible custom graph: independently known payout 7 → 21 survives Studio/Graph/Designer/export and undo', async ({
                                                                                                                                  page,
                                                                                                                                  request
                                                                                                                              }) => {
    test.setTimeout(180000);
    const graph = {...computedLineBoundProof(), opaque: {custom: true}} as ModelGraph;
    await open(page, graph);
    await exact(request, graph, 7);
    await page.getByRole('tab', {name: 'Paytables', exact: true}).click();
    await page.getByLabel('line payout 1 count 2', {exact: true}).fill('3');
    await apply(page);
    const changed = await backup(page);
    expect(changed.opaque).toEqual({custom: true});
    await exact(request, changed, 21);
    await page.getByRole('tab', {name: 'Build', exact: true}).click();
    await page.goto('/model/machine');
    expect(await backup(page)).toEqual(changed);
    await page.goto('/model/studio?view=paytables');
    await page.getByRole('button', {name: 'Undo', exact: true}).click();
    expect(await backup(page)).toEqual(graph);
    await open(page, changed);
    await exact(request, await backup(page), 21);
});

test('PR19 fresh paired visual acceptance: full tabs, scrollers, source, dialogs, dirty/error/applied, keyboard and route order', async ({page}) => {
    test.setTimeout(600000);
    page.setDefaultTimeout(15000);
    const folder = `${evidence}/${visualPhase}`;
    await mkdir(folder, {recursive: true});
    const manifest: unknown[] = [], errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    const capture = async (model: string, view: string, viewport: { width: number; height: number }) => {
        const name = `${model}-${viewport.width}x${viewport.height}-${view}.png`;
        await page.evaluate(() => document.fonts.ready);
        await page.screenshot({path: `${folder}/${name}`, fullPage: true});
        manifest.push({
            model,
            view,
            viewport,
            phase: visualPhase,
            zoom: 1,
            deviceScaleFactor: await page.evaluate(() => devicePixelRatio),
            path: name
        });
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), name).toBe(true);
    };
    const scrollRegions = async (model: string, view: string, viewport: { width: number; height: number }) => {
        await page.locator('main *').evaluateAll(elements => {
            let index = 0;
            for (const element of elements) {
                element.removeAttribute('data-pr19-scroll');
                const style = getComputedStyle(element);
                if (element.clientHeight > 0 && element.clientWidth > 0 &&
                    ((/auto|scroll/.test(style.overflowY) && element.scrollHeight > element.clientHeight + 8) ||
                        (/auto|scroll/.test(style.overflowX) && element.scrollWidth > element.clientWidth + 8)))
                    element.setAttribute('data-pr19-scroll', String(index++));
            }
        });
        const regions = page.locator('[data-pr19-scroll]');
        for (const [index, region] of (await regions.all()).entries()) {
            if (!await region.isVisible()) continue;
            const dimensions = await region.evaluate(element => ({
                height: element.clientHeight,
                total: element.scrollHeight,
                width: element.clientWidth,
                across: element.scrollWidth
            }));
            if (dimensions.total > dimensions.height + 8) for (let y = 0; y < dimensions.total; y += Math.max(250, Math.floor(dimensions.height * .75))) {
                await region.evaluate((element, value) => {
                    element.scrollTop = value;
                }, y);
                await capture(model, `${view}-scroll${index}-y${y}`, viewport);
            }
            if (dimensions.across > dimensions.width + 8) {
                await region.evaluate(element => {
                    element.scrollLeft = element.scrollWidth;
                });
                await capture(model, `${view}-scroll${index}-right`, viewport);
            }
            await region.evaluate(element => {
                element.scrollTop = 0;
                element.scrollLeft = 0;
            });
        }
    };
    for (const viewport of [{width: 1920, height: 1080}, {width: 1440, height: 900}, {
        width: 1280,
        height: 800
    }, {width: 390, height: 844}]) {
        await page.setViewportSize(viewport);
        for (const model of ['ancient-dragon/ordinary', 'nine-lives-tgslots/bought']) {
            const graph = corpus.find(value => value.caseId === model)!.graph, key = model.replaceAll('/', '-');
            await open(page, graph, '/model/studio', visualOrigin);
            for (const view of ['Sources', 'Paytables', 'Paylines', 'Symbols']) {
                await page.getByRole('tab', {name: view, exact: true}).click();
                await capture(key, view, viewport);
                await scrollRegions(key, view, viewport);
            }
            await page.getByRole('tab', {name: 'Sources', exact: true}).click();
            await page.getByRole('button', {name: 'Source details', exact: true}).click();
            await page.getByText('Exact binding and graph source', {exact: true}).click();
            await capture(key, 'source-expanded', viewport);
            await scrollRegions(key, 'source-expanded', viewport);
            await page.keyboard.press('Escape');
            await expect(page.getByRole('complementary', {name: 'Source details'})).not.toBeVisible();
            await page.getByRole('tab', {name: 'Sources', exact: true}).focus();
            await page.keyboard.press('ArrowRight');
            await expect(page.getByRole('tab', {name: 'Paytables', exact: true})).toBeFocused();
            await page.getByRole('tab', {name: 'Sources', exact: true}).click();
            await page.getByRole('button', {name: 'Import table', exact: true}).first().click();
            await page.getByLabel('CSV import input').fill('invalid,value\n1,nope');
            await page.getByRole('button', {name: 'Preview exact values', exact: true}).click();
            await capture(key, 'import-error', viewport);
            await page.getByRole('button', {name: 'Close import', exact: true}).click();
            await page.goto(visualOrigin + '/simulate');
            await page.goto(visualOrigin + '/model/studio?view=symbols');
            expect(await backup(page)).toEqual(graph);
            await capture(key, 'varied-route-symbols', viewport);
        }
        const graph = machineProofGraph(weightedMachine()), key = 'weighted-machine';
        await open(page, graph, '/model/machine?scope=weighted-machine', visualOrigin);
        await expect(page.getByRole('button', {name: 'Create / reuse', exact: true})).toBeEnabled({timeout: 70000});
        if (viewport.width < 768) await page.getByRole('button', {name: 'Features', exact: true}).click();
        await page.locator('.machine-explorer section').getByRole('button', {
            name: 'Choose once',
            exact: false
        }).click();
        if (viewport.width < 768) await page.getByRole('button', {name: 'Features', exact: true}).click();
        await page.locator('.machine-action-row').evaluateAll(elements => elements.forEach(element => element.setAttribute('open', '')));
        await page.getByText('Source, callers and execution limits', {exact: true}).click();
        await capture(key, 'diagram-source-actions', viewport);
        await scrollRegions(key, 'diagram-source-actions', viewport);
        await page.getByRole('tab', {name: 'Transition table', exact: true}).click();
        await capture(key, 'transition-table', viewport);
        await scrollRegions(key, 'transition-table', viewport);
        await page.getByRole('tab', {name: 'Diagram', exact: true}).click();
        await page.getByRole('button', {name: 'Create / reuse', exact: true}).click();
        await capture(key, 'create-dialog', viewport);
        await page.getByLabel('Feature source').selectOption('reuse');
        await page.getByLabel('Existing definition').selectOption('weighted-machine');
        await capture(key, 'reuse-dialog-dropdown', viewport);
        await page.getByRole('dialog').getByRole('button', {name: 'Cancel', exact: true}).click();
        await page.getByLabel('Weight', {exact: true}).first().fill('1.00000000000000001');
        await capture(key, 'fractional-input', viewport);
        expect(await backup(page)).toEqual(graph);
        await page.getByLabel('Weight', {exact: true}).first().fill('2');
        await capture(key, 'dirty', viewport);
        await apply(page);
        const changed = await backup(page);
        await capture(key, 'applied', viewport);
        await page.getByRole('button', {name: 'Test round', exact: true}).click();
        await expect(page.getByText('Seed 42 · round 0', {exact: true})).toBeVisible();
        await capture(key, 'applied-round', viewport);
        await page.goto(visualOrigin + '/model/studio');
        await page.goto(visualOrigin + '/model/machine?scope=weighted-machine');
        expect(await backup(page)).toEqual(changed);
        await capture(key, 'varied-route-recovery', viewport);
    }
    expect(errors).toEqual([]);
    await writeFile(`${folder}/capture.json`, JSON.stringify({
        phase: visualPhase,
        origin: visualOrigin || test.info().project.use.baseURL,
        errors,
        manifest
    }, null, 2));
});
