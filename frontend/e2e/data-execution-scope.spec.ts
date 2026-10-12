import { test, expect, calculateWithQuota, type Page } from './fixtures';

const config = { schemaVersion: '1.0.0', name: 'Data execution scope', initialState: { win: 1 }, nodes: [
  { id: 'sink', nodeType: 'metricsSink', winCap: 10, winStateKey: 'win', inputs: { in: { name: 'in', type: 'State' } } },
], edges: [] };
const link = '/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(config))).toString('base64'));
async function open(page: Page) {
  await page.goto(link);
  await page.getByRole('button', { name: 'Edit project data and expressions', exact: true }).click();
  await page.getByRole('button', { name: 'Execute & inspect', exact: true }).click();
}
async function delayRound(page: Page) {
  let release!: () => void, received = false, finished = false;
  const gate = new Promise<void>(resolve => { release = resolve; });
  await page.evaluate(() => {
    const host = window as unknown as { executionAborted: boolean };
    host.executionAborted = false;
    const original = window.fetch.bind(window);
    window.fetch = (input, init) => {
      if (String(input).endsWith('/api/play/round')) init?.signal?.addEventListener('abort', () => { host.executionAborted = true; }, { once: true });
      return original(input, init);
    };
  });
  await page.route('**/api/play/round', async route => {
    const response = await route.fetch();
    expect(response.ok()).toBe(true);
    received = true;
    await gate;
    await route.fulfill({ response });
    finished = true;
  });
  await page.getByRole('button', { name: 'Execute graph', exact: true }).click();
  await expect.poll(() => received).toBe(true);
  return { release: async () => { release(); await expect.poll(() => finished).toBe(true); await page.unroute('**/api/play/round'); }, expectAborted: async () => { await expect.poll(() => page.evaluate(() => (window as unknown as { executionAborted: boolean }).executionAborted)).toBe(true); } };
}

test('Changing source and restoring it still aborts obsolete execution; a fresh result clears on edit', async ({ page }) => {
  await open(page);
  const delayed = await delayRound(page);
  await page.getByRole('button', { name: 'State & tables', exact: true }).click();
  await page.getByLabel('State field value', { exact: true }).fill('2');
  await page.getByLabel('State field value', { exact: true }).fill('1');
  await delayed.expectAborted(); await delayed.release();
  await page.getByRole('button', { name: 'Execute & inspect', exact: true }).click();
  await expect(page.getByTestId('executed-state')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Execute graph', exact: true })).toBeEnabled();
  await calculateWithQuota(page, '/api/play/round', 'Execute graph');
  await expect(page.getByTestId('executed-state').getByRole('row').filter({ hasText: 'win' })).toContainText('1');
  await page.getByRole('button', { name: 'State & tables', exact: true }).click();
  await page.getByLabel('State field value', { exact: true }).fill('3');
  await page.getByRole('button', { name: 'Execute & inspect', exact: true }).click();
  await expect(page.getByTestId('executed-state')).toHaveCount(0);
});

test('Identical authored models in different projects cannot reuse pending execution evidence', async ({ page }) => {
  await open(page);
  const delayed = await delayRound(page);
  await page.getByLabel('Import project file', { exact: true }).setInputFiles({ name: 'copy.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(config)) });
  await delayed.expectAborted(); await delayed.release();
  await expect(page.getByTestId('executed-state')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Execute graph', exact: true })).toBeEnabled();
  await calculateWithQuota(page, '/api/play/round', 'Execute graph');
  await expect(page.getByTestId('executed-state')).toBeVisible();
});

test('Closing data editor aborts its request and reopening has no stale execution', async ({ page }) => {
  await open(page);
  const delayed = await delayRound(page);
  await page.keyboard.press('Escape');
  await delayed.expectAborted(); await delayed.release();
  await page.getByRole('button', { name: 'Edit project data and expressions', exact: true }).click();
  await page.getByRole('button', { name: 'Execute & inspect', exact: true }).click();
  await expect(page.getByTestId('executed-state')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Execute graph', exact: true })).toBeEnabled();
});
