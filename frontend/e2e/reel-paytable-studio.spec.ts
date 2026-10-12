import { readFile } from 'node:fs/promises';
import { test, expect, type Page, type APIRequestContext } from './fixtures';
import { Flow, Mechanics, gameGraph } from '../src/games/shared/graph/flow';
import { and, at, eq, num, str, when } from '../src/games/shared/graph/expr';
import { reelState, spinReels } from '../src/games/shared/graph/mechanics/reels';
import { cellState, spinCells } from '../src/games/shared/graph/mechanics/cells';
import { linePayTable, paylines, paylineState } from '../src/games/shared/graph/mechanics/paylines';
import { computedLineBoundProof } from '../unit/fixtures/computedLineBoundProof';
import { graphStudiosCorpus } from '../unit/fixtures/graphStudiosCorpus';
import { clusterStudioProof } from '../unit/fixtures/clusterStudioProof';
import { prizeStudioProof } from '../unit/fixtures/prizeStudioProof';
import { createX7ClubGraph } from '../src/games/x7-club/graph';
import { createNineLivesGraph as createTgslotsGraph } from '../src/games/nine-lives-tgslots/graph';
function reels() {
  const spec = { name: 'private-reels', stripKey: 'privateStrip', rows: 2, strips: [['A', 'B', 'C']] }, mechanics = new Mechanics();
  const root = new Flow().use('spin', 'Spin the private reel', mechanics.add(spinReels(spec))).write('pay', 'AB pays one', 'coins', when(and(eq(at('board', num(0)), str('A')), eq(at('board', num(1)), str('B'))), num(1), num(0))).sink('sink', 'Payout', 'coins', 1);
  return gameGraph({ id: 'studio-reel-ui-proof', name: 'Private reel UI proof', description: 'Three physical windows: AB, BC and CA. Only AB pays.', root, mechanics, initialState: { coins: 0, ...reelState(spec) }, tables: { boardConfig: { rows: 2, columns: 1 }, symbols: ['A', 'B', 'C'].map(id => ({ id, name: id, kind: 'Standard' })), opaque: { retained: true } } });
}
function cells() {
  const mechanics = new Mechanics(), root = new Flow().use('draw', 'Independent board cells', mechanics.add(spinCells({ name: 'private-cells', cells: 2, outcomes: [['1', 3], ['2', 1]] })))
    .use('lines', 'AA pays one', mechanics.add(paylines(mechanics, [[0, 0]], 1))).sink('sink', 'Payout', 'lineCoins', 1);
  return gameGraph({ id: 'studio-cell-ui-proof', name: 'Private cell UI proof', description: 'Two independent symbols. AA pays one.', root, mechanics, initialState: { ...cellState({ cells: 2 }), ...paylineState(0, linePayTable({ symbolCount: 3, wild: 0, pays: { 1: { 2: 1 } }, reels: 2 })) }, tables: { boardConfig: { rows: 1, columns: 2 }, symbols: [{ id: '0', name: 'Wild', kind: 'Wild' }, { id: '1', name: 'A', kind: 'Standard' }, { id: '2', name: 'B', kind: 'Standard' }] } });
}
async function open(page: Page, graph: Record<string, unknown>) {
  await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles({ name: 'private-graph.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(graph)) });
  await expect(page.locator('.project-name')).toHaveText(String(graph.name)); await page.getByRole('tab', { name: 'Studio', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Reel & Paytable Studio', exact: true })).toBeVisible();
}
async function backup(page: Page) {
  const pending = page.waitForEvent('download'); await page.getByRole('button', { name: 'Back up project', exact: true }).click(); const download = await pending, path = await download.path();
  return JSON.parse(await readFile(path!, 'utf8')) as { graph: Record<string, unknown>; views: unknown };
}
async function apply(page: Page) {
  const response = page.waitForResponse(response => new URL(response.url()).pathname === '/api/validate' && response.status() !== 429);
  await page.getByRole('button', { name: 'Validate & apply', exact: true }).click(); const result = await response; expect(result.ok()).toBe(true); expect((await result.json()).isValid).toBe(true);
  await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeEnabled();
}
async function exact(api: APIRequestContext, graph: Record<string, unknown>, value: number) {
  const response = await api.post('/api/evaluate/light', { data: { config: graph, maxBranches: 10000, sampleSize: 1000 } }); expect(response.ok()).toBe(true); const result = await response.json(); expect(result.strategy).toBe('Exact'); expect(result.rtp).toBeCloseTo(value, 12);
}
test('Studio: ordered reel changes stay staged, survive immediate reload, validate against the real compiler and undo completely', async ({ page, request }) => {
  const graph = reels(); await open(page, graph); await expect(page.getByRole('button', { name: 'Move selected stop down' })).toBeEnabled();
  await page.getByRole('button', { name: 'Move selected stop down' }).click(); expect((await backup(page)).graph).toEqual(graph);
  await page.reload(); await expect(page.getByLabel('Selected stop symbol')).toHaveValue('B');
  await apply(page); const changed = (await backup(page)).graph; await exact(request, graph, 1 / 3); await exact(request, changed, 0);
  expect((changed.initialState as Record<string, unknown>).privateStrip0).toEqual(['B', 'A', 'C', 'B']); expect(changed.opaque).toEqual(graph.opaque);
  await page.getByRole('button', { name: 'Undo', exact: true }).click(); expect((await backup(page)).graph).toEqual(graph);
});
test('Studio: one board cell changes real probability while independent peers and zero payloads stay intact', async ({ page, request }) => {
  const graph = cells(); await open(page, graph);
  const first = page.getByLabel('Weight 1 in cell-1', { exact: true }); await expect(first).toBeEnabled(); await first.fill('1'); await page.getByLabel('Weight 2 in cell-1', { exact: true }).fill('3');
  await page.reload(); await expect(first).toHaveValue('1'); await expect(page.getByLabel('Weight 2 in cell-1', { exact: true })).toHaveValue('3');
  await apply(page); const changed = (await backup(page)).graph; await exact(request, graph, 9 / 16); await exact(request, changed, 3 / 16);
  const oldNodes = ((graph.mechanics as Record<string, { nodes: Record<string, unknown>[] }>)['private-cells']).nodes, newNodes = ((changed.mechanics as Record<string, { nodes: Record<string, unknown>[] }>)['private-cells']).nodes;
  expect(newNodes[1]).toEqual(oldNodes[1]); expect(newNodes[0].drawWeights).toEqual([{ outcomeId: '1', weight: 1, value: 0 }, { outcomeId: '2', weight: 3, value: 0 }]);
});
test('Studio: invalid text/imports never change source and closing the inspector retains staged inputs', async ({ page }) => {
  const graph = cells(); await open(page, graph); const weight = page.getByLabel('Weight 1 in cell-1', { exact: true }); await expect(weight).toBeEnabled(); await weight.fill('');
  await expect(page.getByRole('button', { name: 'Validate & apply', exact: true })).toBeDisabled(); await expect(page.getByRole('alert')).toContainText('whole weights');
  await page.getByRole('button', { name: 'Source details', exact: true }).click(); await expect(page.getByLabel('Source details', { exact: true })).toBeVisible(); await page.keyboard.press('Escape'); await expect(page.getByLabel('Source details', { exact: true })).toHaveCount(0); await expect(weight).toHaveValue('');
  await page.getByRole('button', { name: 'Paste / import', exact: true }).click(); await page.getByLabel('CSV import input').fill('outcomeId,weight\n1,9007199254740993\n2,1'); await page.getByRole('button', { name: 'Preview exact values' }).click();
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('exact integer'); await expect(page.getByRole('button', { name: 'Stage import' })).toBeDisabled(); await page.getByRole('button', { name: 'Close import' }).click(); expect((await backup(page)).graph).toEqual(graph);
});
test('Studio: a payout increase updates its computed bound and the real executed result without clipping the new win', async ({ page, request }) => {
  const graph = computedLineBoundProof(); await open(page, graph); await page.getByRole('tab', { name: 'Paytables', exact: true }).click();
  const field = page.getByLabel('line payout 1 count 2', { exact: true }); await expect(field).toBeEnabled(); await field.fill('6');
  await apply(page); const changed = (await backup(page)).graph;
  expect((changed.nodes as { id: string; winCap?: number }[]).find(node => node.id === 'sink')!.winCap).toBe(35);
  await exact(request, graph, 7); await exact(request, changed, 21);
});
test('Studio: explicitly keeping the existing cap preserves its actual clipping rule', async ({ page, request }) => {
  const graph = computedLineBoundProof(); await open(page, graph); await page.getByRole('tab', { name: 'Paytables', exact: true }).click();
  const field = page.getByLabel('line payout 1 count 2', { exact: true }); await expect(field).toBeEnabled(); await page.getByLabel('Payout cap policy').selectOption('keep'); await field.fill('6');
  await apply(page); const changed = (await backup(page)).graph; expect((changed.nodes as { id: string; winCap?: number }[]).find(node => node.id === 'sink')!.winCap).toBe(15); await exact(request, changed, 15);
});
test('Studio: dynamic weights retain their expression and do not display static ratios from fallback rows', async ({ page }) => {
  const graph = cells(), scope = (graph.mechanics as Record<string, { nodes: Record<string, unknown>[]; expressions: Record<string, unknown> }>)['private-cells'];
  scope.nodes[0].weightExpressionId = 'dynamicWeights'; scope.expressions.dynamicWeights = { exprType: 'fieldAccess', target: 'state', path: ['runtimeWeights'] };
  await open(page, graph); await expect(page.locator('.weight-probability').filter({ hasText: 'State-dependent' })).toHaveCount(2); await expect(page.getByLabel('Weight 1 in cell-1', { exact: true })).toBeDisabled();
  expect((await page.locator('.weight-probability').allTextContents()).join(' ')).not.toContain('%'); expect((await backup(page)).graph).toEqual(graph);
});
test('Studio: all 65 game/variant/reference/custom corpus graphs open and export without changing executable source', async ({ page }) => {
  test.setTimeout(240000); const { graphs } = graphStudiosCorpus(); expect(graphs).toHaveLength(65);
  for (const { caseId, graph } of graphs) {
    await open(page, graph as Record<string, unknown>); expect((await backup(page)).graph, caseId).toEqual(graph);
  }
});
for (const weighted of [false, true]) test(`Studio: ${weighted ? 'weighted' : 'ordinary'} cluster size edits change executed payouts across minimum-five and isolated-cell boundaries`, async ({ page, request }) => {
  const graph = clusterStudioProof(weighted); await open(page, graph); await page.getByRole('tab', { name: 'Paytables', exact: true }).click();
  const size = weighted ? 3 : 4; await expect(page.getByLabel('New group size')).toBeEnabled(); await page.getByLabel('New group size').fill(String(size)); await page.getByRole('button', { name: 'Add size', exact: true }).click();
  await page.getByLabel(`cluster payout 1 count ${size}`, { exact: true }).fill('6'); await apply(page); const grouped = (await backup(page)).graph;
  await exact(request, graph, 0); await exact(request, grouped, 6); expect((grouped.initialState as Record<string, unknown>).clusterMinSize).toBe(size); expect(grouped.nodes).toEqual(graph.nodes); expect(grouped.opaque).toEqual(graph.opaque);
  await page.getByLabel('New group size').fill('1'); await page.getByRole('button', { name: 'Add size', exact: true }).click(); await page.getByLabel('cluster payout 1 count 1', { exact: true }).fill('3'); await apply(page);
  const singles = (await backup(page)).graph; await exact(request, singles, 9); expect((singles.initialState as Record<string, unknown>).clusterMinSize).toBe(1);
  await page.getByLabel('Remove paying size').selectOption('1'); await page.getByRole('button', { name: 'Remove size', exact: true }).click(); await apply(page); await exact(request, (await backup(page)).graph, 6);
  await page.getByLabel('Remove paying size').selectOption(String(size)); await page.getByRole('button', { name: 'Remove size', exact: true }).click(); await apply(page); const restored = (await backup(page)).graph; await exact(request, restored, 0); expect((restored.initialState as Record<string, unknown>).clusterMinSize).toBe(5);
  await page.getByRole('button', { name: 'Undo', exact: true }).click(); expect((await backup(page)).graph).toEqual(grouped);
});
test('Studio: prize values and independent weights survive immediate reload and change the real expected payout', async ({ page, request }) => {
  const graph = prizeStudioProof(); await open(page, graph); await page.getByRole('tab', { name: 'Paytables', exact: true }).click();
  await expect(page.getByLabel('Prize 1 value')).toBeEnabled(); await page.getByLabel('Prize 1 value').fill('3'); await page.reload(); await expect(page.getByLabel('Prize 1 value')).toHaveValue('3');
  await apply(page); const values = (await backup(page)).graph; await exact(request, graph, 4); await exact(request, values, 7);
  await page.getByLabel('Prize 1 weight').fill('1'); await apply(page); const weighted = (await backup(page)).graph; await exact(request, weighted, 8);
  await page.getByLabel('Prize 1 value').fill('5'); await expect(page.getByRole('button', { name: 'Validate & apply', exact: true })).toBeDisabled(); await expect(page.getByRole('alert')).toContainText('same identity'); expect((await backup(page)).graph).toEqual(weighted);
});
test('Studio: the selected X7 prize table and its pending values recover immediately instead of returning to line payouts', async ({ page }) => {
  const graph = createX7ClubGraph(); await open(page, graph); await page.getByRole('tab', { name: 'Paytables', exact: true }).click(); await page.locator('.studio-source-explorer').getByRole('button', { name: /Coin prizes/ }).click();
  await expect(page.getByLabel('Prize 1 value')).toBeEnabled(); await page.getByLabel('Prize weight distribution').selectOption({ index: 1 }); const selectedDraw = await page.getByLabel('Prize weight distribution').inputValue(); await page.getByLabel('Prize 1 weight').fill('1111'); await page.getByLabel('Prize 1 value').fill('10001'); await page.reload(); await expect(page.getByRole('heading', { name: 'Coin prizes', exact: true })).toBeVisible(); await expect(page.getByLabel('Prize 1 value')).toHaveValue('10001'); await expect(page.getByLabel('Prize weight distribution')).toHaveValue(selectedDraw); await expect(page.getByLabel('Prize 1 weight')).toHaveValue('1111');
  await apply(page); const changed = (await backup(page)).graph; expect(changed.initialState).toEqual(graph.initialState); expect(changed.nodes).toEqual(graph.nodes);
  const old = graph.mechanics as Record<string, { nodes: Record<string, unknown>[] }>, current = changed.mechanics as typeof old;
  for (const [scope, definition] of Object.entries(old)) for (const node of definition.nodes.filter(node => node.nodeType === 'draw' && /^prize\d+$/.test(String(node.stateWriteKey)))) {
    const edited = current[scope].nodes.find(value => value.id === node.id)!; expect((edited.drawWeights as Record<string, unknown>[])[0]).toEqual({ ...(node.drawWeights as Record<string, unknown>[])[0], outcomeId: '10001', ...(JSON.stringify([scope, node.id]) === selectedDraw ? { weight: 1111 } : {}) });
  }
});
test('Studio: adding a TGSlots size across all symbol rows and editing one cell commits the whole cluster dependency closure', async ({ page, request }) => {
  const graph = createTgslotsGraph(); await open(page, graph); await page.getByRole('tab', { name: 'Paytables', exact: true }).click();
  await expect(page.getByLabel('New group size')).toBeEnabled(); await page.getByLabel('New group size').fill('4'); await page.getByRole('button', { name: 'Add size', exact: true }).click(); await page.getByLabel('cluster payout 1 count 4', { exact: true }).fill('1');
  await apply(page); const changed = (await backup(page)).graph; expect((changed.initialState as Record<string, unknown>).clusterMinSize).toBe(4); expect(changed.nodes).toEqual(graph.nodes);
  const response = await request.post('/api/play/round', { data: { config: changed, seed: 42, roundIndex: 0 } }); expect(response.ok()).toBe(true); expect((await response.json()).seed).toBe(42);
  await page.getByRole('button', { name: 'Undo', exact: true }).click(); expect((await backup(page)).graph).toEqual(graph);
});
