import { test, expect } from './fixtures';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
const model = { name: 'Editable invalid expression', tables: { schemaVersion: '1.0.0', initialState: { weights: [3, 1] }, untouched: { keep: true } }, nodes: [
  { id: 'draw', type: 'draw', position: { x: 0, y: 0 }, data: { nodeType: 'draw', label: 'Draw', drawWeights: [{ outcomeId: 'one', weight: 1, value: 1 }, { outcomeId: 'zero', weight: 1, value: 0 }], weightExpressionId: 'state-weights' } },
  { id: 'sink', type: 'sink', position: { x: 300, y: 0 }, data: { nodeType: 'sink', label: 'Sink', winCap: 10 } },
], edges: [{ id: 'edge', source: 'draw', sourceHandle: 'out', target: 'sink', targetHandle: 'in' }] };
const link = (value: unknown) => '/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(value))).toString('base64'));
async function expression(page: import('./fixtures').Page, text: string) {
  const editor = page.getByRole('complementary', { name: 'Context inspector' }).locator('.cm-content').first();
  await editor.fill(text);
}

test('Invalid legacy DSL retains the editor, backs up original text and blocks executable exports until corrected', async ({ page }) => {
  const errors: string[] = [], emptyRequests: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('request', request => { if (request.url().endsWith('/api/evaluate/light') && request.postData()?.includes('Invalid expression')) emptyRequests.push(request.postData()!); });
  await page.goto(link(model));
  await expect(page.getByRole('alert')).toContainText('Select a state field');
  await page.getByLabel('Focus graph node').selectOption('draw');
  await expect(page.locator('.cm-content').first()).toContainText('state-weights');
  const download = page.waitForEvent('download'); await page.getByRole('button', { name: 'Download editable draft' }).click();
  const backup = JSON.parse(await readFile((await (await download).path())!, 'utf8'));
  expect(backup.nodes[0].data.weightExpressionId).toBe('state-weights'); expect(backup.tables.untouched).toEqual({ keep: true });
  await page.getByRole('tab', { name: 'Export', exact: true }).click();
  for (const name of ['Download JSON', 'Copy to clipboard', 'Save to server']) await expect(page.getByRole('button', { name, exact: true })).toBeDisabled();
  await page.getByRole('tab', { name: 'Build', exact: true }).click(); await page.getByLabel('Focus graph node').selectOption('draw');
  await expression(page, 'state.weights');
  await expect(page.getByRole('alert')).toHaveCount(0);
  await page.getByRole('button', { name: 'Close inspector', exact: true }).click();
  const exported = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export project', exact: true }).click();
  const config = JSON.parse(await readFile((await (await exported).path())!, 'utf8'));
  expect(config.nodes[0].weightExpressionId).toBe('draw:weightExpressionId'); expect(config.expressions['draw:weightExpressionId']).toMatchObject({ exprType: 'fieldAccess', target: 'state', path: ['weights'] });
  expect(config.untouched).toEqual({ keep: true }); expect(errors).toEqual([]); expect(emptyRequests).toEqual([]);
});

test('Incomplete expression edits remain recoverable across invalid edits, reload and context dialogs', async ({ page }) => {
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  await page.goto(link({ ...model, nodes: [{ ...model.nodes[0], data: { ...model.nodes[0].data, weightExpressionId: 'state.weights' } }, model.nodes[1]] }));
  await page.getByLabel('Focus graph node').selectOption('draw');
  await expression(page, 'state.'); await expect(page.getByRole('alert')).toContainText('Model cannot be serialized');
  await expression(page, 'state-weights'); await expect(page.getByRole('alert')).toContainText('Select a state field');
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('slotmath-draft-v1') ?? '{}').nodes?.[0]?.data?.weightExpressionId)).toBe('state-weights');
  await page.reload(); await expect(page.getByRole('alert')).toContainText('Select a state field');
  await page.getByRole('button', { name: 'Open mechanic manager', exact: true }).click();
  await expect(page.getByTestId('mechanic-manager')).toBeVisible();
  await page.getByRole('button', { name: 'Close inspector', exact: true }).click();
  await page.getByRole('button', { name: 'Edit project data and expressions', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Project data & expressions' })).toBeVisible();
  await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
  for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
    await page.setViewportSize({ width, height });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: path.resolve('../docs/design/qa', `serialization-error-${test.info().project.name}-${width}.png`) });
  }
  expect(errors).toEqual([]);
});

test('Editable backup restores incomplete source into an independent project and preserves the current game', async ({ page }) => {
  await page.goto(link(model)); await expect(page.getByRole('alert')).toContainText('Select a state field');
  const pending = page.waitForEvent('download'); await page.getByRole('button', { name: 'Download editable draft', exact: true }).click();
  const backup = await readFile((await (await pending).path())!);
  await page.getByRole('button', { name: 'Load coin example', exact: true }).click();
  await page.getByRole('tab', { name: 'My projects', exact: true }).click();
  const input = page.locator('.project-import input[type="file"]');
  await input.setInputFiles({ name: 'broken.json', mimeType: 'application/json', buffer: Buffer.from('{"format":"slotmath-editable-draft-v1","nodes":[]}') });
  await expect(page.getByRole('alert')).toContainText('current project has not been replaced');
  await expect(page.locator('[aria-label="Personal projects"] .project-row').filter({ hasText: 'REF-A Coin' })).toContainText('Current project');
  await input.setInputFiles({ name: 'backup.json', mimeType: 'application/json', buffer: backup });
  await expect(page.getByRole('alert')).toContainText('Select a state field');
  await page.getByLabel('Focus graph node').selectOption('draw');
  await expect(page.locator('.cm-content').first()).toContainText('state-weights');
  await expression(page, 'state.weights'); await expect(page.getByRole('alert')).toHaveCount(0);
  await page.getByRole('tab', { name: 'My projects', exact: true }).click();
  await expect(page.locator('[aria-label="Personal projects"] .project-row')).toHaveCount(3);
  await page.locator('[aria-label="Personal projects"] .project-row').filter({ hasText: 'REF-A Coin' }).getByRole('button', { name: 'Open project', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'REF-A Coin', exact: true })).toBeVisible();
  await expect.poll(() => page.evaluate(() => {
    const saved = JSON.parse(localStorage.getItem('slotmath-draft-v1') ?? '{}');
    return saved.configName === 'REF-A Coin' && Object.keys(saved.projects ?? {}).length === 3;
  })).toBe(true);
  await page.reload(); await expect(page.getByRole('heading', { name: 'REF-A Coin', exact: true })).toBeVisible();
});

test('Invalid models keep measurement dialogs editable without posting a substitute schema or collection plan', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('slotmath-measurements-v1', JSON.stringify({ metrics: [{ id: 'payout', name: 'Payout', nodeId: '', valueMode: 'payout', expression: '', filterMode: 'all', filter: '', unit: '', reducers: ['mean'], chart: true, chartStatistic: 'mean', hidden: false }], hiddenWidgets: [] })));
  let schemas = 0; const errors: string[] = [];
  page.on('request', request => { if (request.url().endsWith('/api/runs/measurements/schema')) schemas++; });
  page.on('pageerror', error => errors.push(error.message));
  await page.goto(link(model)); await expect(page.getByRole('alert')).toContainText('Select a state field');
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  for (const [open, save] of [['＋ Track metric', 'Save measurement'], ['Create component accounting plan', 'Save accounting plan'], ['Configure verification profile', 'Save verification profile']]) {
    await page.getByRole('button', { name: open, exact: true }).click();
    const dialog = page.getByRole('dialog'); await expect(dialog.getByRole('alert')).toContainText('Fix model serialization');
    await expect(dialog.getByRole('button', { name: save, exact: true })).toBeDisabled();
    await expect(dialog.getByRole('status')).toHaveCount(0);
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  }
  expect(schemas).toBe(0); expect(errors).toEqual([]);
});
