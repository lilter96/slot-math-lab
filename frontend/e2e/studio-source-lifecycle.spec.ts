import {readFile} from 'node:fs/promises';
import {type APIRequestContext, expect, type Page, test} from './fixtures';
import {emptySourceProof, sharedDrawingProof} from '../unit/fixtures/sourceLifecycleProof';
import {prizeStudioProof} from '../unit/fixtures/prizeStudioProof';
import {computedLineBoundProof} from '../unit/fixtures/computedLineBoundProof';

async function open(page: Page, graph: Record<string, unknown>) {
    await page.goto('/build');
    await page.getByLabel('Import project file').setInputFiles({
        name: 'source-proof.json',
        mimeType: 'application/json',
        buffer: Buffer.from(JSON.stringify(graph))
    });
    await expect(page.locator('.project-name')).toHaveText(String(graph.name));
    await page.getByRole('tab', {name: 'Studio', exact: true}).click();
}

async function backup(page: Page) {
    const pending = page.waitForEvent('download');
    await page.getByRole('button', {name: 'Back up project', exact: true}).click();
    const download = await pending;
    return JSON.parse(await readFile((await download.path())!, 'utf8')).graph as Record<string, unknown>;
}

async function apply(page: Page) {
    test.setTimeout(Math.max(test.info().timeout, 180000));
    const response = page.waitForResponse(response => new URL(response.url()).pathname === '/api/validate' && response.status() !== 429, {timeout: 70000});
    await page.getByRole('button', {name: 'Validate & apply', exact: true}).click();
    expect((await (await response).json()).isValid).toBe(true);
    await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeEnabled();
}

async function exact(request: APIRequestContext, graph: Record<string, unknown>, expected: number) {
    const response = await request.post('/api/evaluate/light', {
        data: {
            config: graph,
            maxBranches: 10000,
            sampleSize: 1000
        }
    });
    expect(response.ok()).toBe(true);
    const result = await response.json();
    expect(result.strategy).toBe('Exact');
    expect(result.rtp).toBeCloseTo(expected, 12);
}

test('Studio source creation: a blank model receives an ordinary numeric source, persists before reload and executes without Studio metadata', async ({
                                                                                                                                                          page,
                                                                                                                                                          request
                                                                                                                                                      }) => {
    const graph = emptySourceProof();
    await open(page, graph);
    await page.getByRole('button', {name: 'Create source', exact: true}).click();
    await page.getByLabel('New source outcomes').fill('2,3,0\n10,1,0');
    await page.getByRole('button', {name: 'Stage new source', exact: true}).click();
    await page.reload();
    await apply(page);
    const changed = await backup(page);
    expect(changed.opaque).toEqual(graph.opaque);
    await exact(request, changed, 4);
    await page.getByRole('button', {name: 'Undo', exact: true}).click();
    expect(await backup(page)).toEqual(graph);
});
for (const kind of ['cells', 'reels'] as const) test(`Studio source creation: a new ${kind} board uses the authored dictionary and executes ordinary graph primitives`, async ({
                                                                                                                                                                                   page,
                                                                                                                                                                                   request
                                                                                                                                                                               }) => {
    const graph = emptySourceProof();
    await open(page, graph);
    await page.getByRole('button', {name: 'Create source', exact: true}).click();
    await page.getByLabel('New source kind').selectOption(kind);
    await page.getByRole('button', {name: 'Stage new source', exact: true}).click();
    await apply(page);
    const changed = await backup(page);
    expect(changed.boardConfig).toEqual({columns: 3, rows: 3});
    expect(changed.symbols).toEqual([{id: 'A', name: 'Symbol A', kind: 'Standard'}, {
        id: 'B',
        name: 'Symbol B',
        kind: 'Standard'
    }]);
    const response = await request.post('/api/play/round', {data: {config: changed, seed: 42, roundIndex: 0}});
    expect(response.ok()).toBe(true);
    const result = await response.json();
    expect(result.state.board).toHaveLength(9);
    expect(result.state.board.every((id: string) => ['A', 'B'].includes(id))).toBe(true);
    expect(result.win).toBe(0);
    expect(changed.opaque).toEqual(graph.opaque);
});
for (const kind of ['cells', 'reels'] as const) test(`Studio private ${kind}: copying the second use and editing only its source changes real probability while the first source stays intact`, async ({
                                                                                                                                                                                                           page,
                                                                                                                                                                                                           request
                                                                                                                                                                                                       }) => {
    const graph = sharedDrawingProof(kind);
    await open(page, graph);
    await page.getByRole('button', {name: 'Make one use independent', exact: true}).click();
    await page.getByLabel('Independent source use').selectOption({label: 'Root / Second use of the source'});
    await page.getByRole('button', {name: 'Stage independent copy', exact: true}).click();
    await apply(page);
    const copied = await backup(page);
    await exact(request, copied, 1);
    const picker = page.locator('.studio-source-explorer').getByRole('button', {name: /Private source|Private-source|Private_source/});
    await picker.first().click();
    if (kind === 'reels') {
        await page.getByLabel('Selected stop symbol').selectOption('B');
        await page.getByRole('button', {name: 'Move selected stop down', exact: true}).click();
        await page.getByLabel('Selected stop symbol').selectOption('B');
    } else {
        await page.locator('.studio-outcome-domain summary').click();
        await page.getByRole('button', {name: 'Remove outcome A', exact: true}).click();
    }
    await apply(page);
    const changed = await backup(page);
    await exact(request, changed, .5);
    expect((changed.mechanics as Record<string, unknown>)['shared-draw']).toEqual((graph.mechanics as Record<string, unknown>)['shared-draw']);
    expect((changed.nodes as { id: string }[]).find(node => node.id === 'first')).toEqual((graph.nodes as {
        id: string
    }[]).find(node => node.id === 'first'));
    expect(changed.opaque).toEqual(graph.opaque);
});
test('Studio prize lifecycle: adding/removing a prize updates real expected payout and requires explicit removal policy', async ({
                                                                                                                                     page,
                                                                                                                                     request
                                                                                                                                 }) => {
    const graph = prizeStudioProof();
    await open(page, graph);
    await page.getByRole('tab', {name: 'Paytables', exact: true}).click();
    await page.locator('.studio-outcome-domain summary').click();
    await page.getByLabel('New prize value').fill('10');
    await page.getByLabel('New prize weight').fill('2');
    await page.getByRole('button', {name: 'Add prize', exact: true}).click();
    await apply(page);
    const added = await backup(page);
    await exact(request, added, 28 / 3); // (3×2 + 1×10 + 2×20) / 6
    await page.getByRole('button', {name: 'Remove prize 1', exact: true}).click();
    await expect(page.getByRole('button', {name: 'Validate & apply', exact: true})).toBeDisabled();
    await expect(page.getByRole('alert')).toContainText('Confirm discarding');
    await page.getByLabel('Discard weights of removed prize outcomes', {exact: false}).check();
    await apply(page);
    await exact(request, await backup(page), 50 / 3); // (1×10 + 2×20) / 3
});
test('Studio line lifecycle: duplicate/remove lines preserve per-line pricing or explicitly keep a fixed total, and execute the real payout', async ({
                                                                                                                                                         page,
                                                                                                                                                         request
                                                                                                                                                     }) => {
    const graph = computedLineBoundProof();
    await open(page, graph);
    await page.getByRole('tab', {name: 'Paylines', exact: true}).click();
    await page.getByRole('button', {name: 'Duplicate selected line', exact: true}).click();
    await expect(page.getByLabel('Line stake policy')).toHaveValue('per-line');
    await apply(page);
    const priced = await backup(page);
    expect((priced.initialState as Record<string, unknown>).totalBet).toBe(2);
    expect((priced.initialState as Record<string, unknown>).roundCost).toBe(4);
    await exact(request, priced, 7);
    await page.getByRole('button', {name: 'Undo', exact: true}).click();
    expect(await backup(page)).toEqual(graph);
    await page.getByRole('button', {name: 'Duplicate selected line', exact: true}).click();
    await page.getByLabel('Line stake policy').selectOption('fixed-total');
    await expect(page.getByRole('button', {name: 'Validate & apply', exact: true})).toBeDisabled();
    await page.getByLabel('Confirm fixed total line stake').check();
    await apply(page);
    const fixed = await backup(page);
    expect((fixed.initialState as Record<string, unknown>).totalBet).toBe(1);
    expect((fixed.initialState as Record<string, unknown>).roundCost).toBe(2);
    await exact(request, fixed, 14);
    await page.getByRole('button', {name: 'Remove selected line', exact: true}).click();
    await apply(page);
    await exact(request, await backup(page), 7);
});
