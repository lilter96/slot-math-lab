import { scalarMeasurements } from '../src/lib/measurements/trends';
import { saveMeasurement, waitForRunLaunch, test, expect, type Page, cancelOwnedRun, getWithQuota } from './fixtures';
import { readFile } from 'node:fs/promises';
test.beforeEach(async ({ request }) => {
  test.setTimeout(180000);
  const response = await request.get('/api/auth/status');
  if (response.status() === 429) {
    const delay = Math.min(60000, Math.max(1000, Number(response.headers()['retry-after'] ?? '1') * 1000));
    await new Promise(resolve => setTimeout(resolve, delay));
  }
});
async function prepare(page: Page) {
  await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles('e2e/fixtures/measurement-model.json');
  await expect(page.locator('.react-flow__node')).toHaveCount(5); await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
}
async function sticky(page: Page, name = 'Sticky FS payout') {
  await page.getByRole('button', { name: '＋ Track metric', exact: true }).click();
  await page.getByLabel('Metric name').fill(name);
  await page.getByLabel('Metric observation level').selectOption('node'); await page.getByLabel('Metric graph node').selectOption('end');
  await page.getByLabel('Metric numeric expression').fill('state.spinWin'); await page.getByLabel('Metric unit').fill('coins');
  await page.getByLabel('Metric filter mode').selectOption('expression'); await page.getByLabel('Metric filter expression').fill('state.fsType == "sticky"');
  await saveMeasurement(page);
}
test('UI configures a scoped FS metric; real engine, durable export, display controls and pinned replay agree', { tag: '@critical' }, async ({ page }) => {
  await prepare(page); await sticky(page);
  await page.getByLabel('Simulation spins').fill('5000');
  const launch = waitForRunLaunch(page); await page.getByRole('button', { name: /^▶ Start run$/ }).click();
  const response = await launch; expect(response.status()).toBe(202); const run = await response.json();
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
  const metric = page.getByRole('article', { name: 'Tracked metric Sticky FS payout', exact: true });
  await expect(metric.locator('[data-statistic=min]')).toHaveText('2 coins'); await expect(metric.locator('[data-statistic=max]')).toHaveText('4 coins');
  await expect(metric.locator('[data-statistic=mean]')).toHaveText('3 coins'); await expect(metric).toContainText('10,000 matching'); await expect(metric).toContainText('15,000 eligible node visits');
  await page.getByRole('button', { name: 'Display settings for Sticky FS payout' }).click();
  await metric.getByRole('checkbox', { name: 'Minimum', exact: true }).uncheck(); await metric.getByRole('checkbox', { name: 'Maximum', exact: true }).uncheck();
  await expect(metric.locator('[data-statistic=min]')).toHaveCount(0); await expect(metric.locator('[data-statistic=mean]')).toHaveText('3 coins');
  await page.getByRole('button', { name: 'Customize dashboard' }).click(); await page.getByRole('checkbox', { name: 'Payout histogram', exact: true }).uncheck();
  await expect(page.getByRole('heading', { name: 'Payout distribution', exact: true })).toHaveCount(0);
  const download = page.waitForEvent('download'); await page.getByRole('button', { name: '↓ Export evidence', exact: true }).click();
  const bundle = JSON.parse(await readFile((await (await download).path())!, 'utf8'));
  expect(bundle.run.measurements[0].nodeId).toBe('end'); expect(bundle.progress.measurements[0]).toMatchObject({ count: 10000, excluded: 5000, observations: 15000, mean: 3, min: 2, max: 4 });
  expect(bundle.pinnedGraph.inputVerified).toBe(true); expect(bundle.pinnedGraph.config.expressions.win.op).toBe('Add');
  await page.reload(); await expect(metric.locator('[data-statistic=mean]')).toHaveText('3 coins'); await expect(metric.locator('[data-statistic=min]')).toHaveCount(0);
  await page.goto(`/simulate?run=${run.id}`); await page.evaluate(() => localStorage.clear()); await page.reload();
  await expect(metric.locator('[data-statistic=mean]')).toHaveText('3 coins'); await expect(page.getByRole('button', { name: /^▶ Start new run$/ })).toBeEnabled();
  await page.goto(`/results?run=${run.id}`); await expect(page.getByRole('heading', { name: 'Tracked measurements', exact: true })).toBeVisible({ timeout: 70000 });
  await page.getByRole('button', { name: 'Replay pinned run', exact: true }).click(); await page.getByLabel('Pinned run workers').selectOption('1');
  let replayAttempts = 0;
  await page.route('**/api/runs', route => {
    if (route.request().method() !== 'POST') return route.continue();
    return ++replayAttempts === 1
      ? route.fulfill({ status: 429, headers: { 'Retry-After': '1' }, body: '' }) : route.continue();
  });
  const replay = waitForRunLaunch(page); await page.getByRole('button', { name: 'Start pinned run', exact: true }).click();
  const replayResponse = await replay; expect(replayResponse.status()).toBe(202);
  const replayRun = await replayResponse.json(); expect(replayRun.measurementHash).toBe(run.measurementHash);
  expect(replayAttempts).toBeGreaterThanOrEqual(2);
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
  const final = await (await getWithQuota(page.request, `/api/runs/${replayRun.id}`)).json(); expect(final.progress.measurements[0]).toMatchObject(bundle.progress.measurements[0]);
  await page.screenshot({ path: '../docs/verification/measurements-scoped.png', fullPage: true });
});
test('Editor rejects unknown fields through compiler, and valid empty scopes remain undefined', async ({ page }) => {
  await prepare(page); await page.getByRole('button', { name: '＋ Track metric', exact: true }).click();
  await page.getByLabel('Metric name').fill('Invalid scope'); await page.getByLabel('Metric filter mode').selectOption('expression');
  await page.getByLabel('Metric filter expression').fill('state.unknownType == "sticky"');
  const rejected = page.waitForResponse(response => response.url().endsWith('/api/runs/measurements/schema') && response.status() === 400, { timeout: 70000 });
  await page.getByRole('button', { name: 'Save measurement', exact: true }).click(); await rejected;
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('Unknown field');
  await page.getByLabel('Metric name').fill('No matching rounds'); await page.getByLabel('Metric filter expression').fill('false');
  await saveMeasurement(page);
  await page.getByLabel('Simulation spins').fill('16'); await page.getByRole('button', { name: /^▶ Start run$/ }).click();
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
  const metric = page.getByRole('article', { name: 'Tracked metric No matching rounds', exact: true }); await expect(metric.locator('[data-statistic=mean]')).toHaveText('—');
  await expect(metric).toContainText('0 matching'); await expect(metric).toContainText('16 excluded'); await expect(metric).toContainText('0 invalid');
});
test('Metric editor and customizable workspace fit mobile, preserve draft across reload, and include nested FS points', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 }); await page.goto('/build?project=dog-house');
  await page.getByRole('button', { name: 'Open free spins graph', exact: true }).click(); await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByRole('button', { name: '＋ Track metric', exact: true }).click(); await page.getByLabel('Metric name').fill('Sticky free spin payout');
  await page.getByLabel('Metric observation level').selectOption('node'); await page.getByLabel('Find measurement node').fill('free-spin/snapshot');
  await page.getByLabel('Metric graph node').selectOption('free-spin/snapshot-winHistory'); await page.getByLabel('Metric numeric expression').fill('state.spinCoins / 20');
  await saveMeasurement(page);
  await expect(page.getByRole('article', { name: 'Tracked metric Sticky free spin payout' })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.reload(); await expect(page.getByRole('article', { name: 'Tracked metric Sticky free spin payout' })).toBeVisible();
  await page.screenshot({ path: '../docs/verification/measurements-mobile.png', fullPage: true });
});
test('Scoped measurements use real sockets, survive HTTP recovery and reload, and retain the cancelled prefix', async ({ page, request }) => {
  test.setTimeout(240000);
  let matchingFrames = 0;
  page.on('websocket', socket => socket.on('framereceived', ({ payload }) => {
    for (const raw of String(payload).split('\x1e')) {
      try { const frame = JSON.parse(raw); if (frame.target === 'ProgressUpdate' && frame.arguments[0].measurements?.[0]?.count > 0) matchingFrames++; } catch { /* SignalR handshake. */ }
    }
  }));
  await page.goto('/build?project=dog-house'); await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByRole('button', { name: '＋ Track metric', exact: true }).click(); await page.getByLabel('Metric name').fill('Sticky FS live');
  await page.getByLabel('Metric observation level').selectOption('node'); await page.getByLabel('Metric graph node').selectOption('free-spin/snapshot-winHistory');
  await page.getByLabel('Metric numeric expression').fill('state.spinCoins / 20'); await saveMeasurement(page); await page.route('**/hubs/runs/negotiate**', route => route.abort());
  await page.getByLabel('Simulation spins').fill('10000000'); const launched = waitForRunLaunch(page);
  await page.getByRole('button', { name: /^▶ Start run$/ }).click(); const run = await (await launched).json();
  try {
    const metric = page.getByRole('article', { name: 'Tracked metric Sticky FS live', exact: true });
    await expect(page.getByTestId('stream-status')).toContainText('recovering'); await expect(metric.locator('[data-statistic=mean]')).not.toHaveText('—', { timeout: 70000 });
    await page.unroute('**/hubs/runs/negotiate**'); await expect(page.getByTestId('stream-status')).toContainText('WebSocket live', { timeout: 70000 });
    await expect.poll(() => matchingFrames).toBeGreaterThan(0);
    await page.reload(); await expect(metric.locator('[data-statistic=mean]')).not.toHaveText('—', { timeout: 10000 });
    await page.getByRole('button', { name: /Cancel run/ }).click(); await expect(page.locator('.run-status')).toHaveText('cancelled', { timeout: 15000 });
    const final = await (await getWithQuota(request, `/api/runs/${run.id}`)).json(), m = final.progress.measurements[0];
    expect(m.count).toBeGreaterThan(0); expect(m.errors).toBe(0); expect(m.observations).toBe(m.count); expect(final.measurementHash).toBe(run.measurementHash);
    const stored = JSON.parse(await page.evaluate(() => localStorage.getItem('slotmath-simulation-v2')!)); expect(stored.progress.measurements).toEqual(scalarMeasurements(final.progress.measurements));
    expect(JSON.parse(final.resultJson).measurements).toEqual(final.progress.measurements);
  } finally { await cancelOwnedRun(request, run.id); }
});
test('Rejected launch honors Retry-After without duplicating an accepted run', async ({ page }) => {
  await page.goto('/build'); await page.getByRole('button', { name: 'Load coin example' }).click(); await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  let attempts = 0; const times: number[] = [];
  await page.route('**/api/runs', async route => {
    if (route.request().method() !== 'POST') return route.continue();
    attempts++; times.push(Date.now());
    if (attempts === 1) return route.fulfill({ status: 429, headers: { 'Retry-After': '1' }, json: { error: 'busy' } });
    return route.continue();
  });
  await page.getByLabel('Simulation spins').fill('10000'); await page.getByRole('button', { name: /^▶ Start run$/ }).click();
  await expect(page.getByText(/retrying the rejected request in 1s/).first()).toBeVisible({ timeout: 70000 });
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
  expect(attempts).toBe(2); expect(times[1] - times[0]).toBeGreaterThanOrEqual(950);
});
test('Waiting launch can be cancelled without sending the deferred run POST', async ({ page }) => {
  await page.goto('/build'); await page.getByRole('button', { name: 'Load coin example' }).click(); await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  let attempts = 0;
  await page.route('**/api/runs', route => {
    if (route.request().method() !== 'POST') return route.continue();
    attempts++; return route.fulfill({ status: 429, headers: { 'Retry-After': '60' }, json: { error: 'busy' } });
  });
  await page.getByRole('button', { name: /^▶ Start run$/ }).click(); await expect(page.getByText(/retrying the rejected request in 60s/).first()).toBeVisible();
  await page.getByRole('button', { name: 'Cancel launch', exact: true }).click(); await expect(page.locator('.run-status')).toHaveText('ready');
  expect(attempts).toBe(1); await expect(page.getByRole('button', { name: /^▶ Start run$/ })).toBeEnabled();
});
