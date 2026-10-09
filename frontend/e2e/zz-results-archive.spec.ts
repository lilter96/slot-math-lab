import { test, expect as assertion, type APIRequestContext, type APIResponse } from '@playwright/test';
const expect = assertion.configure({ timeout: 70000 });
test.beforeEach(async ({ request }) => { test.setTimeout(120000); await permitted(() => request.get('/api/auth/status')); });
async function permitted(request: () => Promise<APIResponse>) {
  let response = await request();
  if (response.status() === 429) {
    // Rejected operations have not executed. Honor the production limiter;
    // never replay an accepted POST or weaken server limits for fixtures.
    await new Promise(resolve => setTimeout(resolve, Math.min(60000, Math.max(1000, Number(response.headers()['retry-after'] ?? '1') * 1000))));
    response = await request();
  }
  return response;
}
function coin(name = `Results coin ${crypto.randomUUID()}`) {
  return { schemaVersion: '1.0.0', name, initialState: { targetRtpPercent: 75 }, nodes: [
    { nodeType: 'draw', id: 'draw', label: 'Coin draw', drawWeights: [{ outcomeId: 'three', weight: 1, value: 3 }, { outcomeId: 'one', weight: 3, value: 1 }, { outcomeId: 'zero', weight: 4, value: 0 }], outputs: { out: { name: 'out', type: 'Wins' } } },
    { nodeType: 'metricsSink', id: 'sink', label: 'Metrics', winCap: 10000, inputs: { in: { name: 'in', type: 'Wins' } } },
  ], edges: [{ id: 'e', sourceNodeId: 'draw', sourcePort: 'out', targetNodeId: 'sink', targetPort: 'in' }] };
}
async function save(api: APIRequestContext, config: unknown) {
  const response = await permitted(() => api.post('/api/configs', { data: { config } })); expect(response.status()).toBe(201); return (await response.json()).id as string;
}
async function launch(api: APIRequestContext, configId: string, samples = 10000, seed = 42, workers = 1, configVersion?: number) {
  const response = await permitted(() => api.post('/api/runs', { data: { configId, sampleSize: samples, seed, degreeOfParallelism: workers, configVersion, progressBatchSize: 1000 } }));
  expect(response.status()).toBe(202); return response.json();
}
async function finish(api: APIRequestContext, id: string, expected = 'completed') {
  let result;
  await expect.poll(async () => { result = await (await permitted(() => api.get(`/api/runs/${id}`))).json(); return result.status; }, { timeout: 90000, intervals: [500, 1000, 2000] }).toBe(expected);
  return result;
}
test('server archive survives empty browser storage, filters and pages without substituting editor state', async ({ page }) => {
  const name = `Results archive ${crypto.randomUUID()}`, configId = await save(page.request, coin(name));
  const records = [];
  for (let i = 0; i < 23; i++) records.push(await launch(page.request, configId, 100, 4000 + i));
  for (const run of records) await finish(page.request, run.id);
  await page.goto('/results'); await page.getByLabel('Search saved runs').fill(name);
  await expect(page.getByRole('heading', { name: 'Run archive 23', exact: true })).toBeVisible();
  await expect(page.locator('.results-run-list > li')).toHaveCount(20);
  await page.getByRole('button', { name: 'Load older runs' }).click();
  await expect(page.locator('.results-run-list > li')).toHaveCount(23);
  await page.getByLabel('Filter saved runs').selectOption('failed');
  await expect(page.getByText('No matching runs', { exact: true })).toBeVisible();
  await page.goto(`/results?run=${records[0].id}`);
  await expect(page.getByRole('heading', { name, exact: true })).toBeVisible();
  await expect(page.getByTestId('results-rtp')).not.toHaveText('—');
  await page.reload(); await expect(page.getByRole('heading', { name, exact: true })).toBeVisible();
});
