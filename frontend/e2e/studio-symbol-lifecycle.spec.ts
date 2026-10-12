import {readFile} from 'node:fs/promises';
import {type APIRequestContext, expect, type Page, test} from './fixtures';
import {clusterStudioProof} from '../unit/fixtures/clusterStudioProof';
import {computedLineBoundProof} from '../unit/fixtures/computedLineBoundProof';

async function open(page: Page, graph: Record<string, unknown>) {
    await page.goto('/build');
    await page.getByLabel('Import project file').setInputFiles({
        name: 'symbol-proof.json',
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
    const response = page.waitForResponse(response => new URL(response.url()).pathname === '/api/validate' && response.status() !== 429);
    await page.getByRole('button', {name: 'Validate & apply', exact: true}).click();
    const result = await (await response).json();
    expect(result, JSON.stringify(result)).toMatchObject({isValid: true});
    await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeEnabled();
}

async function exact(request: APIRequestContext, graph: Record<string, unknown>, payout: number) {
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
    expect(result.rtp).toBeCloseTo(payout, 12);
}

test('Studio dictionary: stable extension preserves real payout, pending reload and referenced deletion shows its blockers', async ({
                                                                                                                                        page,
                                                                                                                                        request
                                                                                                                                    }) => {
    const graph = clusterStudioProof();
    await open(page, graph);
    await page.getByRole('tab', {name: 'Symbols', exact: true}).click();
    await page.getByRole('button', {name: 'Add symbol', exact: true}).click();
    await expect(page.getByLabel('New symbol ID')).toHaveValue('3');
    await page.getByLabel('New symbol name').fill('Private C');
    await page.getByRole('button', {name: 'Stage symbol', exact: true}).click();
    await page.reload();
    await apply(page);
    const changed = await backup(page);
    expect((changed.symbols as { id: string }[]).map(symbol => symbol.id)).toEqual(['0', '1', '2', '3']);
    expect(changed.paytables).toEqual(graph.paytables);
    await exact(request, changed, 0);
    const row = page.locator('.studio-symbol-dictionary article').filter({has: page.getByLabel('Symbol 1 name', {exact: true})});
    await row.locator('summary').click();
    await expect(row).toContainText('Paytable private-clusters');
    await expect(page.getByRole('button', {name: 'Remove dictionary symbol 1', exact: true})).toBeDisabled();
    await page.getByRole('button', {name: 'Undo', exact: true}).click();
    expect(await backup(page)).toEqual(graph);
});
for (const weighted of [false, true]) test(`Studio ${weighted ? 'weighted' : 'ordinary'} cluster eligibility: new B row becomes a real one-cell payout, removal retains drawing/dictionary source`, async ({
                                                                                                                                                                                                               page,
                                                                                                                                                                                                               request
                                                                                                                                                                                                           }) => {
    const graph = clusterStudioProof(weighted);
    await open(page, graph);
    await page.getByRole('tab', {name: 'Paytables', exact: true}).click();
    await page.locator('.studio-paying-domain summary').click();
    await page.getByLabel('Domain new paying size').fill('1');
    await page.getByRole('button', {name: 'Add paying size', exact: true}).click();
    await page.getByLabel('New paying symbol').selectOption('2');
    await page.getByRole('button', {name: 'Add paying symbol', exact: true}).click();
    await page.getByLabel('Domain payout 2 count 1').fill('4');
    await page.reload();
    await apply(page);
    const added = await backup(page);
    await exact(request, added, 4);
    expect(added.symbols).toEqual(graph.symbols);
    expect((added.initialState as Record<string, unknown>).board).toEqual((graph.initialState as Record<string, unknown>).board);
    await page.locator('.studio-paying-domain summary').click();
    await page.getByRole('button', {name: 'Remove paying symbol 1', exact: true}).click();
    await apply(page);
    const removed = await backup(page);
    await exact(request, removed, 4);
    expect((removed.paytables as {
        entries: { symbolId: string }[]
    }[])[0].entries.map(row => row.symbolId)).toEqual(['2']);
    expect(removed.opaque).toEqual(graph.opaque);
    await page.getByLabel('Paying size to remove').selectOption('5');
    await page.getByRole('button', {name: 'Remove paying size 5', exact: true}).click();
    await apply(page);
    await exact(request, await backup(page), 4);
});
test('Studio line eligibility: authored wild row reconciles computed bound and can be removed to restore its derived rule', async ({
                                                                                                                                       page,
                                                                                                                                       request
                                                                                                                                   }) => {
    const graph = computedLineBoundProof();
    await open(page, graph);
    await page.getByRole('tab', {name: 'Paytables', exact: true}).click();
    await page.locator('.studio-paying-domain summary').click();
    await page.getByLabel('New paying symbol').selectOption('0');
    await page.getByRole('button', {name: 'Add paying symbol', exact: true}).click();
    await page.getByLabel('Domain payout 0 count 2').fill('6');
    await apply(page);
    const changed = await backup(page);
    expect((changed.nodes as { id: string; winCap?: number }[]).find(node => node.id === 'sink')!.winCap).toBe(35);
    await exact(request, changed, 7);
    await page.getByRole('button', {name: 'Remove paying symbol 0', exact: true}).click();
    await apply(page);
    const removed = await backup(page);
    expect((removed.nodes as { id: string; winCap?: number }[]).find(node => node.id === 'sink')!.winCap).toBe(15);
    await exact(request, removed, 7);
});
