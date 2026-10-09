import { test, expect as assertion, type APIRequestContext, type APIResponse } from '@playwright/test';
import { readFile } from 'node:fs/promises';
const expect = assertion.configure({ timeout: 70000 }); // Includes the server's 60s Retry-After window.
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
test('reference and exports use saved version after latest config changes; graph opening preserves the draft', async ({ page }) => {
  const config = coin(), configId = await save(page.request, config), original = await launch(page.request, configId);
  await finish(page.request, original.id);
  await page.request.put(`/api/configs/${configId}`, { data: { config: { ...config, name: 'Newer editor model' } } });
  await page.goto('/build'); await page.getByRole('button', { name: 'Load coin example' }).click();
  await page.goto(`/results?run=${original.id}`);
  await expect(page.getByRole('heading', { name: config.name, exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Calculate reference', exact: true }).click();
  await expect(page.locator('.results-reference-value')).toHaveText('75.000%');
  await expect(page.locator('.results-reference .results-source-tag')).toHaveText('ExactDistribution');
  await page.getByRole('tab', { name: 'Reproducibility', exact: true }).click();
  const jsonDownload = page.waitForEvent('download'); await page.getByRole('button', { name: 'Download evidence JSON', exact: true }).click();
  const bundle = JSON.parse(await readFile((await (await jsonDownload).path())!, 'utf8'));
  expect(bundle.evidence.pinnedConfig.name).toBe(config.name); expect(bundle.evidence.run.configVersion).toBe(1); expect(bundle.reference.rational).toBe('3/4');
  expect(bundle.evidence.run.configHash).toBe(original.configHash);
  for (const [label, contains] of [['Download metrics CSV', 'Sampler clipping count'], ['Printable report HTML', original.configHash]]) {
    const pending = page.waitForEvent('download'); await page.getByRole('button', { name: label, exact: true }).click();
    expect(await readFile((await (await pending).path())!, 'utf8')).toContain(contains);
  }
  await page.getByRole('button', { name: 'Open pinned graph ↗', exact: true }).click();
  await expect(page).toHaveURL('/build'); await expect(page.getByRole('button', { name: 'Restore previous editor draft' })).toBeVisible();
  await page.reload(); await page.getByRole('button', { name: 'Restore previous editor draft' }).click();
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('slotmath-draft-v1')!).configName)).toBe('REF-A Coin');
});
test('pinned replay changes worker count while preserving version and every seeded statistic', async ({ page }) => {
  const config = coin(), configId = await save(page.request, config), first = await launch(page.request, configId, 100000);
  await finish(page.request, first.id);
  await page.request.put(`/api/configs/${configId}`, { data: { config: { ...config, name: 'Later version' } } });
  await page.goto(`/results?run=${first.id}&view=reproducibility`);
  await page.getByRole('button', { name: 'Replay pinned run', exact: true }).click();
  await page.getByLabel('Pinned run workers').selectOption('3');
  const creation = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/api/runs'));
  await page.getByRole('button', { name: 'Start pinned run', exact: true }).click(); const second = await (await creation).json();
  expect(second.configVersion).toBe(1); expect(second.configHash).toBe(first.configHash); expect(second.degreeOfParallelism).toBe(3);
  await finish(page.request, second.id);
  await page.goto(`/results?run=${second.id}&view=compare&compare=${first.id}`);
  await expect(page.getByTestId('results-comparison-verdict')).toHaveText('Replay matches');
  await page.reload(); await expect(page.getByTestId('results-comparison-verdict')).toHaveText('Replay matches');
  // A rejected launch must leave the earlier complete simulation available.
  await page.getByRole('tab', { name: 'Reproducibility', exact: true }).click();
  await page.getByRole('button', { name: 'Replay pinned run', exact: true }).click();
  await page.route('**/api/runs', route => route.request().method() === 'POST'
    ? route.fulfill({ status: 404, contentType: 'application/json', body: '{"error":"Saved version unavailable"}' }) : route.continue());
  await page.getByRole('button', { name: 'Start pinned run', exact: true }).click();
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('Saved version unavailable');
  expect(await page.evaluate(() => JSON.parse(localStorage.getItem('slotmath-simulation-v2')!).run.id)).toBe(second.id);
  await page.getByRole('button', { name: 'Close run dialog', exact: true }).click();
});
test('comparison separates independent seeds, different inputs and overlapping stream lengths', async ({ page }) => {
  const configId = await save(page.request, coin()), first = await launch(page.request, configId), independent = await launch(page.request, configId, 10000, 43);
  const overlap = await launch(page.request, configId, 20000, 42), changedId = await save(page.request, coin('A different model')), changed = await launch(page.request, changedId);
  for (const run of [first, independent, overlap, changed]) await finish(page.request, run.id);
  for (const [run, title] of [[independent, 'Difference includes zero'], [overlap, 'Overlapping seeded streams'], [changed, 'Different or unverified inputs']]) {
    await page.goto(`/results?run=${first.id}&view=compare&compare=${run.id}`);
    await expect(page.getByTestId('results-comparison-verdict')).toHaveText(title);
  }
});
test('Dog House retains its 98% AST reference with contribution metrics, distribution and mobile access', async ({ page }) => {
  const config = JSON.parse(await readFile('../backend/SlotMath.Core.Tests/TestData/DogHouse/dog-house-ui.json', 'utf8')); delete config.id;
  const configId = await save(page.request, config), run = await launch(page.request, configId, 100000, 42, 2);
  await finish(page.request, run.id);
  const errors: string[] = []; page.on('pageerror', err => errors.push(err.message));
  await page.goto(`/results?run=${run.id}`);
  await expect(page.getByTestId('results-rtp')).toHaveText('99.672%');
  await expect(page.locator('.results-rtp-hero').getByText('Authored target', { exact: false })).toContainText('98.000%');
  await page.getByRole('button', { name: 'Calculate reference', exact: true }).click();
  await expect(page.locator('.results-reference-value')).toHaveText('98.000%');
  await expect(page.locator('.results-reference .results-source-tag')).toHaveText('ExactExpectation');
  await expect(page.getByText('Base paylines', { exact: true })).toBeVisible();
  await page.screenshot({ path: '../docs/verification/results-overview.png', fullPage: true });
  await page.getByLabel('RTP tolerance in percentage points').fill('0');
  await expect(page.getByTestId('results-assessment')).toHaveText('Set a valid tolerance');
  await page.getByRole('tab', { name: 'Distribution', exact: true }).click();
  await expect(page.getByRole('img', { name: /Payout histogram/ })).toBeVisible();
  await page.getByRole('button', { name: 'Cumulative', exact: true }).click();
  await expect(page.getByRole('img', { name: /Cumulative payout probability/ })).toBeVisible();
  await page.getByText(/Inspect \d+ nonempty bins/).click();
  await expect(page.getByRole('columnheader', { name: 'Cumulative', exact: true })).toBeVisible();
  await page.screenshot({ path: '../docs/verification/results-distribution.png', fullPage: true });
  await page.getByRole('tab', { name: 'Distribution', exact: true }).focus(); await page.keyboard.press('End');
  await expect(page.getByRole('tab', { name: 'Reproducibility', exact: true })).toHaveAttribute('aria-selected', 'true');
  await page.setViewportSize({ width: 390, height: 844 });
  for (const section of ['Overview', 'Distribution', 'Compare', 'Reproducibility']) {
    await page.getByRole('tab', { name: section, exact: true }).click();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.getByRole('tabpanel').scrollIntoViewIfNeeded(); await expect(page.getByRole('tabpanel')).toBeInViewport();
  }
  await page.getByRole('tab', { name: 'Overview', exact: true }).click();
  await page.screenshot({ path: '../docs/verification/results-mobile.png', fullPage: true });
  await page.getByRole('button', { name: 'Open proof in constructor ↗', exact: true }).click();
  await expect(page).toHaveURL('/build');
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('slotmath-draft-v1')!).tables.uiAnalysisKind)).toBe('expectationProof');
  expect(errors).toEqual([]);
});
test('failed and zero-variance runs never gain a green acceptance verdict', async ({ page }) => {
  const invalid = coin('Results invalid'); delete (invalid.nodes[1] as { winCap?: number }).winCap;
  const failed = await launch(page.request, await save(page.request, invalid)); await finish(page.request, failed.id, 'failed');
  await page.goto(`/results?run=${failed.id}`); await expect(page.getByTestId('results-rtp')).toHaveText('—');
  await expect(page.getByTestId('results-assessment')).toHaveText('Incomplete evidence');
  await expect(page.getByRole('alert').filter({ hasText: 'Run interrupted or failed' })).toBeVisible();
  const deterministic = coin('Results deterministic'); deterministic.nodes[0].drawWeights = [{ outcomeId: 'one', weight: 1, value: 1 }];
  const zero = await launch(page.request, await save(page.request, deterministic)); await finish(page.request, zero.id);
  await page.goto(`/results?run=${zero.id}`); await expect(page.getByTestId('results-assessment')).toHaveText('Variance unresolved');
});
test('live monitoring survives Results navigation with one socket; cancellation is diagnostic evidence', async ({ page }) => {
  let socketCount = 0; page.on('websocket', s => { if (s.url().includes('/hubs/runs')) socketCount++; });
  await page.goto('/build?project=dog-house'); await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByLabel('Simulation spins').fill('10000000');
  const creation = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/api/runs'));
  await page.getByRole('button', { name: /start run/i }).click(); const run = await (await creation).json();
  try {
    await expect(page.getByTestId('stream-status')).toContainText('WebSocket live');
    await expect(page.getByTestId('sample-count')).not.toHaveText('0');
    const first = (await (await page.request.get(`/api/runs/${run.id}`)).json()).progress.sampleCount;
    await page.getByRole('tab', { name: 'Results', exact: true }).click();
    await expect(page.getByTestId('results-assessment')).toHaveText('Run in progress');
    await expect.poll(async () => (await (await page.request.get(`/api/runs/${run.id}`)).json()).progress.sampleCount).toBeGreaterThan(first);
    expect(socketCount).toBe(1);
    await page.request.delete(`/api/runs/${run.id}`); await finish(page.request, run.id, 'cancelled');
    await expect(page.getByTestId('results-assessment')).toHaveText('Incomplete evidence');
    await expect(page.getByTestId('results-rtp')).not.toHaveText('—');
  } finally { await page.request.delete(`/api/runs/${run.id}`); }
});
test('unknown run and archive outages provide recoverable error states', async ({ page }) => {
  await page.goto('/results?run=missing-run'); await expect(page.getByRole('heading', { name: 'Run could not be retrieved' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Retry this run' })).toBeVisible();
  await page.route('**/api/runs?*', r => r.fulfill({ status: 503, contentType: 'application/json', body: '{"error":"Archive temporarily unavailable"}' }));
  await page.goto('/results'); await expect(page.getByRole('alert').filter({ hasText: 'Archive temporarily unavailable' })).toBeVisible();
  await page.unroute('**/api/runs?*'); await page.getByRole('button', { name: 'Retry archive', exact: true }).click();
  await expect(page.locator('.results-run-list > li').first()).toBeVisible();
});
test('a stale browser run cannot hide available server evidence; explicit missing links remain errors', async ({ page }) => {
  const config = coin(), configId = await save(page.request, config), run = await launch(page.request, configId);
  await finish(page.request, run.id);
  await page.addInitScript(snapshot => localStorage.setItem('slotmath-simulation-v2', JSON.stringify({ run: { ...snapshot, id: 'missing-browser-run', status: 'completed' }, points: [], history: [] })), run);
  await page.goto('/results');
  await expect(page.getByRole('heading', { name: config.name, exact: true })).toBeVisible();
  await expect(page.getByTestId('results-rtp')).not.toHaveText('—');
  await page.goto('/results?run=missing-browser-run');
  await expect(page.getByRole('heading', { name: 'Run could not be retrieved', exact: true })).toBeVisible();
});
