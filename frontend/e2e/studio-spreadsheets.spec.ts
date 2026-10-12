import { readFile } from 'node:fs/promises';
import * as XLSX from 'xlsx';
import { test, expect, type Page, type APIRequestContext } from './fixtures';
import { clusterStudioProof } from '../unit/fixtures/clusterStudioProof';
import { prizeStudioProof } from '../unit/fixtures/prizeStudioProof';
import { computedLineBoundProof } from '../unit/fixtures/computedLineBoundProof';

async function open(page: Page, graph: Record<string, unknown>) {
  await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles({ name: 'private.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(graph)) });
  await expect(page.locator('.project-name')).toHaveText(String(graph.name)); await page.getByRole('tab', { name: 'Studio', exact: true }).click(); await page.getByRole('tab', { name: 'Paytables', exact: true }).click();
}
async function backup(page: Page) {
  const pending = page.waitForEvent('download'); await page.getByRole('button', { name: 'Back up project', exact: true }).click(); const download = await pending;
  return JSON.parse(await readFile((await download.path())!, 'utf8')).graph as Record<string, unknown>;
}
async function apply(page: Page) {
  const response = page.waitForResponse(response => new URL(response.url()).pathname === '/api/validate' && response.status() !== 429);
  await page.getByRole('button', { name: 'Validate & apply', exact: true }).click(); expect((await (await response).json()).isValid).toBe(true); await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeEnabled();
}
async function exact(request: APIRequestContext, graph: Record<string, unknown>, expected: number) {
  const response = await request.post('/api/evaluate/light', { data: { config: graph, maxBranches: 10000, sampleSize: 1000 } }); expect(response.ok()).toBe(true); const result = await response.json(); expect(result.strategy).toBe('Exact'); expect(result.rtp).toBeCloseTo(expected, 12);
}
function workbook(sheets: { name: string; rows: (string | number)[][]; formula?: { cell: string; expression: string; cached: number } }[]) {
  const book = XLSX.utils.book_new();
  for (const data of sheets) { const sheet = XLSX.utils.aoa_to_sheet(data.rows); if (data.formula) sheet[data.formula.cell] = { t: 'n', f: data.formula.expression, v: data.formula.cached }; XLSX.utils.book_append_sheet(book, sheet, data.name); }
  return XLSX.write(book, { bookType: 'xlsx', type: 'buffer', compression: true }) as Buffer;
}
async function clipboardText(page: Page, text: string) {
  await page.evaluate(text => { const input = document.createElement('textarea'); input.id = 'clipboard-test-buffer'; input.value = text; input.style.cssText = 'position:fixed;left:0;top:0;width:1px;height:1px;opacity:0'; document.body.appendChild(input); input.focus(); input.select(); }, text);
  await page.keyboard.press('Control+C'); await page.locator('#clipboard-test-buffer').evaluate(input => input.remove());
}
async function readCopiedText(page: Page) {
  await page.evaluate(() => { const input = document.createElement('textarea'); input.id = 'clipboard-test-buffer'; input.style.cssText = 'position:fixed;left:0;top:0;width:1px;height:1px;opacity:0'; document.body.appendChild(input); input.focus(); });
  await page.keyboard.press('Control+V'); const value = await page.locator('#clipboard-test-buffer').inputValue(); await page.locator('#clipboard-test-buffer').evaluate(input => input.remove()); return value;
}
test('Studio XLSX: select a worksheet, map arbitrary columns/names and convert exact units before executing a cluster edit', async ({ page, request }) => {
  const graph = clusterStudioProof(); await open(page, graph); await page.getByRole('button', { name: 'Import table', exact: true }).click();
  const bytes = workbook([{ name: 'Notes', rows: [['Notes'], ['Select Payouts']] }, { name: 'Payouts', rows: [['Authoring import'], ['Reward', 'Size', 'Name'], [6, 4, 'A']] }]);
  await page.getByLabel('Choose table file').setInputFiles({ name: 'cluster.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: bytes });
  await expect(page.getByLabel('Import worksheet').locator('option')).toHaveCount(2); await page.getByLabel('Import worksheet').selectOption({ label: 'Payouts' }); await page.getByLabel('Import header row').fill('2');
  await page.getByLabel('Map symbolId column').selectOption('2'); await page.getByLabel('Map count column').selectOption('1'); await page.getByLabel('Map value column').selectOption('0'); await page.getByLabel('Import symbol matching').selectOption('name'); await page.getByLabel('Import payout unit').selectOption('credits');
  await page.getByRole('button', { name: 'Preview exact values', exact: true }).click(); await expect(page.getByRole('button', { name: 'Stage import', exact: true })).toBeEnabled(); await expect(page.locator('.studio-import-preview')).toContainText('6');
  await page.getByRole('button', { name: 'Stage import', exact: true }).click(); expect(await backup(page)).toEqual(graph); await page.reload(); await apply(page); const changed = await backup(page);
  await exact(request, graph, 0); await exact(request, changed, 6); expect((changed.initialState as Record<string, unknown>).clusterMinSize).toBe(4);
});
test('Studio XLSX: real export stores exact cells as text and imports numeric prizes/independent weights without changing no-op source', async ({ page, request }) => {
  const graph = prizeStudioProof(); await open(page, graph); const pending = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export applied table', exact: true }).click(); const download = await pending, bytes = await readFile((await download.path())!);
  expect(bytes.subarray(0, 2).toString()).toBe('PK'); const book = XLSX.read(bytes, { type: 'buffer' }); expect(book.SheetNames).toEqual(['Inputs', 'Read me']); expect(book.Sheets.Inputs.A2.t).toBe('s'); expect(book.Sheets.Inputs.C2.t).toBe('s'); expect(book.Sheets.Inputs.F2.v).toBe('1');
  await page.getByRole('button', { name: 'Import table', exact: true }).click(); await page.getByLabel('Choose table file').setInputFiles({ name: 'no-op.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: bytes });
  await expect(page.getByLabel('Import worksheet').locator('option')).toHaveCount(2); await page.getByRole('button', { name: 'Preview exact values', exact: true }).click(); await expect(page.getByRole('button', { name: 'Stage import', exact: true })).toBeEnabled(); await page.getByRole('button', { name: 'Stage import', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Validate & apply', exact: true })).toBeDisabled(); expect(await backup(page)).toEqual(graph); await page.getByRole('button', { name: 'Discard changes', exact: true }).click();
  book.Sheets.Inputs.C2.v = '3'; book.Sheets.Inputs.G2.v = '1'; const changedFile = XLSX.write(book, { type: 'buffer', bookType: 'xlsx' }) as Buffer;
  await page.getByRole('button', { name: 'Import table', exact: true }).click(); await page.getByLabel('Choose table file').setInputFiles({ name: 'prizes.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: changedFile });
  await expect(page.getByLabel('Import worksheet').locator('option')).toHaveCount(2); await page.getByRole('button', { name: 'Preview exact values', exact: true }).click(); await expect(page.getByRole('button', { name: 'Stage import', exact: true })).toBeEnabled(); await page.getByRole('button', { name: 'Stage import', exact: true }).click(); await apply(page); await exact(request, await backup(page), 8);
});
test('Studio XLSX: formulas and unsafe/invalid cells expose their row/address and never apply a partial import', async ({ page }) => {
  const graph = clusterStudioProof(); await open(page, graph); await page.getByRole('button', { name: 'Import table', exact: true }).click();
  const bytes = workbook([{ name: 'Inputs', rows: [['symbolId', 'count', 'value'], ['1', '4', 6]], formula: { cell: 'C2', expression: '3+3', cached: 6 } }]);
  await page.getByLabel('Choose table file').setInputFiles({ name: 'formula.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: bytes }); await expect(page.locator('.studio-import-file')).toContainText('Loaded');
  await page.getByRole('button', { name: 'Preview exact values', exact: true }).click(); await expect(page.getByRole('dialog').getByRole('alert')).toContainText('C2: Formula cell rejected'); await expect(page.getByRole('button', { name: 'Stage import', exact: true })).toBeDisabled(); await page.getByRole('button', { name: 'Close import', exact: true }).click(); expect(await backup(page)).toEqual(graph);
});
test('Studio XLSX: original numeric XML precision is checked before a rounded parser value could become a valid credit', async ({ page }) => {
  const graph = clusterStudioProof(); await open(page, graph); await page.getByRole('button', { name: 'Import table', exact: true }).click();
  const book = XLSX.utils.book_new(), sheet = XLSX.utils.aoa_to_sheet([['symbolId', 'count', 'value'], ['1', '4', 1]]); sheet.C2 = { t: 'n', v: '1.000000000000000001' }; XLSX.utils.book_append_sheet(book, sheet, 'Inputs');
  const bytes = XLSX.write(book, { type: 'buffer', bookType: 'xlsx' }) as Buffer;
  expect(XLSX.read(bytes, { type: 'buffer' }).Sheets.Inputs.C2.v).toBe(1);
  await page.getByLabel('Choose table file').setInputFiles({ name: 'precision.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: bytes }); await expect(page.locator('.studio-import-file')).toContainText('Loaded');
  await expect(page.getByLabel('CSV import input')).toHaveValue(/1\.000000000000000001/); await page.getByRole('button', { name: 'Preview exact values', exact: true }).click(); await expect(page.getByRole('dialog').getByRole('alert')).toContainText('whole credits'); await expect(page.getByRole('button', { name: 'Stage import', exact: true })).toBeDisabled(); await page.getByRole('button', { name: 'Close import', exact: true }).click(); expect(await backup(page)).toEqual(graph);
});
test('Studio XLSX: round-price amounts convert to credits and reconcile the real computed payout bound', async ({ page, request }) => {
  const graph = computedLineBoundProof(); await open(page, graph); await page.getByRole('button', { name: 'Import table', exact: true }).click();
  await page.getByLabel('Choose table file').setInputFiles({ name: 'round-units.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: workbook([{ name: 'Inputs', rows: [['symbolId', 'count', 'value'], ['1', 2, 3]] }]) }); await expect(page.locator('.studio-import-file')).toContainText('Loaded');
  await page.getByLabel('Import payout unit').selectOption('round'); await page.getByRole('button', { name: 'Preview exact values', exact: true }).click(); await expect(page.getByRole('button', { name: 'Stage import', exact: true })).toBeEnabled(); await page.getByRole('button', { name: 'Stage import', exact: true }).click(); await apply(page);
  const changed = await backup(page); expect((changed.nodes as { id: string; winCap?: number }[]).find(node => node.id === 'sink')!.winCap).toBe(35); await exact(request, changed, 21);
});
test('Studio clipboard: TSV paste stages a payout batch and the real result changes', async ({ page, request }) => {
  const graph = computedLineBoundProof(); await open(page, graph); const cell = page.getByLabel('line payout 1 count 2', { exact: true }); await expect(cell).toBeEnabled(); await cell.focus();
  await clipboardText(page, '6\n'); await cell.focus(); await page.keyboard.press('Control+V');
  await expect(cell).toHaveValue('6'); await apply(page); await exact(request, await backup(page), 21);
  await page.getByRole('button', { name: 'Import table', exact: true }).click(); await page.getByLabel('Paste table format').selectOption('tsv'); await page.getByLabel('CSV import input').fill('symbolId\tcount\tvalue\n1\t2\t2');
  await page.getByRole('button', { name: 'Preview exact values', exact: true }).click(); await expect(page.getByRole('button', { name: 'Stage import', exact: true })).toBeEnabled(); await page.getByRole('button', { name: 'Stage import', exact: true }).click(); await apply(page); await exact(request, await backup(page), 7);
});
test('Studio clipboard: keyboard rectangle selection copies exact values and an oversized paste leaves every input intact', async ({ page }) => {
  const graph = clusterStudioProof(); await open(page, graph); const first = page.getByLabel('cluster payout 1 count 5', { exact: true }), second = page.getByLabel('cluster payout 1 count 6', { exact: true });
  await expect(first).toBeEnabled(); await first.focus(); await first.press('Shift+ArrowRight'); await expect(second).toBeFocused(); await expect(page.locator('.studio-matrix-keyboard')).toContainText('1 × 2');
  await page.keyboard.press('Control+C'); expect(await readCopiedText(page)).toBe('"10"\t"20"');
  await clipboardText(page, '1\t2\t3'); await first.focus(); await page.keyboard.press('Control+V');
  await expect(page.getByRole('alert')).toContainText('No input has changed'); await expect(first).toHaveValue('10'); await expect(second).toHaveValue('20'); expect(await backup(page)).toEqual(graph);
});
