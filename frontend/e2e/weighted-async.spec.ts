import { test, expect, type Page } from './fixtures';

const config = { schemaVersion: '1.0.0', name: 'Async weighted model', nodes: [
  { nodeType: 'draw', id: 'draw', drawWeights: [{ outcomeId: 'one', weight: 1, value: 1 }, { outcomeId: 'zero', weight: 1, value: 0 }], outputs: { out: { name: 'out', type: 'Wins' } } },
  { nodeType: 'metricsSink', id: 'sink', winCap: 10, inputs: { in: { name: 'in', type: 'Wins' } } },
], edges: [{ id: 'edge', sourceNodeId: 'draw', sourcePort: 'out', targetNodeId: 'sink', targetPort: 'in' }] };
const link = '/model/data?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(config))).toString('base64'));
async function delayFile(page: Page) {
  await page.addInitScript(() => {
    const host = window as unknown as { fileStarted: boolean; fileFinished: boolean; releaseFile(): void };
    const original = File.prototype.text, gate = new Promise<void>(resolve => { host.releaseFile = resolve; });
    File.prototype.text = async function () { const text = await original.call(this); if (this.name.startsWith('slow')) { host.fileStarted = true; await gate; host.fileFinished = true; } return text; };
  });
}
async function started(page: Page) { await expect.poll(() => page.evaluate(() => (window as unknown as { fileStarted: boolean }).fileStarted)).toBe(true); }
async function release(page: Page) {
  await page.evaluate(() => (window as unknown as { releaseFile(): void }).releaseFile());
  await expect.poll(() => page.evaluate(() => (window as unknown as { fileFinished: boolean }).fileFinished)).toBe(true);
}
const csv = (name: string, weight: number) => ({ name, mimeType: 'text/csv', buffer: Buffer.from(`outcomeId,weight\none,${weight}\nzero,1`) });

test('A newer CSV wins over a delayed read for the same draw', async ({ page }) => {
  await delayFile(page); await page.goto(link);
  const input = page.getByLabel('Weights CSV file');
  await input.setInputFiles(csv('slow.csv', 9)); await started(page);
  await input.setInputFiles(csv('new.csv', 3));
  await expect(page.getByLabel('Weight for one')).toHaveValue('3');
  await release(page);
  await expect(page.getByLabel('Weight for one')).toHaveValue('3');
});

test('Manual editing and reset invalidate a pending CSV without replacing the retained draft', async ({ page }) => {
  await delayFile(page); await page.goto(link);
  await page.getByLabel('Weights CSV file').setInputFiles(csv('slow.csv', 9)); await started(page);
  await page.getByLabel('Weight for one').fill('4');
  await release(page);
  await expect(page.getByLabel('Weight for one')).toHaveValue('4');
  await page.getByRole('tab', { name: 'Build', exact: true }).click();
  await page.getByRole('tab', { name: 'Model data', exact: true }).click();
  await expect(page.getByLabel('Weight for one')).toHaveValue('4');
  await page.getByRole('button', { name: 'Reset weights', exact: true }).click();
  await expect(page.getByLabel('Weight for one')).toHaveValue('1');
});

test('A CSV finishing after unmount cannot create a retained draft', async ({ page }) => {
  await delayFile(page); await page.goto(link);
  await page.getByLabel('Weights CSV file').setInputFiles(csv('slow.csv', 9)); await started(page);
  await page.getByRole('tab', { name: 'Build', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Weighted outcome editor' })).toHaveCount(0);
  await release(page);
  await page.getByRole('tab', { name: 'Model data', exact: true }).click();
  await expect(page.getByLabel('Weight for one')).toHaveValue('1');
});

test('Reset cancels a pending CSV read', async ({ page }) => {
  await delayFile(page); await page.goto(link);
  await page.getByLabel('Weight for one').fill('4');
  await page.getByLabel('Weights CSV file').setInputFiles(csv('slow.csv', 9)); await started(page);
  await page.getByRole('button', { name: 'Reset weights', exact: true }).click();
  await release(page);
  await expect(page.getByLabel('Weight for one')).toHaveValue('1');
});

test('Pending weighted validation aborts on unmount and cannot apply to an identical project copy', async ({ page }) => {
  await page.goto(link);
  await page.evaluate(() => {
    const host = window as unknown as { validationAborted: boolean };
    const original = window.fetch.bind(window);
    window.fetch = (input, init) => { if (String(input).endsWith('/api/validate')) init?.signal?.addEventListener('abort', () => { host.validationAborted = true; }, { once: true }); return original(input, init); };
  });
  let received = false, finished = false, releaseResponse!: () => void;
  const gate = new Promise<void>(resolve => { releaseResponse = resolve; });
  await page.route('**/api/validate', async route => { const response = await route.fetch(); expect(response.ok()).toBe(true); received = true; await gate; await route.fulfill({ response }); finished = true; });
  await page.getByLabel('Weight for one').fill('2');
  await page.getByRole('button', { name: 'Validate & apply weights', exact: true }).click();
  await expect.poll(() => received).toBe(true);
  await page.getByRole('tab', { name: 'Build', exact: true }).click();
  await expect.poll(() => page.evaluate(() => (window as unknown as { validationAborted: boolean }).validationAborted)).toBe(true);
  await page.getByLabel('Import project file').setInputFiles({ name: 'same.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(config)) });
  releaseResponse(); await expect.poll(() => finished).toBe(true);
  await page.getByRole('tab', { name: 'Model data', exact: true }).click();
  await expect(page.getByLabel('Weight for one')).toHaveValue('1');
  await expect(page.getByRole('button', { name: 'Undo weight change' })).toHaveCount(0);
});

test('Project-library import ignores a delayed read after a newer import opens', async ({ page }) => {
  await delayFile(page); await page.goto('/projects');
  const input = page.locator('.project-import input[type="file"]');
  await input.setInputFiles({ name: 'slow.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify({ ...config, name: 'Obsolete library import' })) });
  await started(page);
  await input.setInputFiles({ name: 'new.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify({ ...config, name: 'New library import' })) });
  await expect(page.getByRole('heading', { name: 'New library import', exact: true })).toBeVisible();
  await release(page);
  await expect(page.getByRole('heading', { name: 'New library import', exact: true })).toBeVisible();
  await page.getByRole('tab', { name: 'My projects', exact: true }).click();
  await expect(page.locator('.project-list').getByRole('heading', { name: 'Obsolete library import', exact: true })).toHaveCount(0);
});
