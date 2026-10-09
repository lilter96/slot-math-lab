import { chromium } from '../../frontend/node_modules/playwright/index.mjs';
import { expect } from '../../frontend/node_modules/@playwright/test/index.mjs';
import { readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
const root = fileURLToPath(new URL('../..', import.meta.url));
const base = process.env.MEASUREMENTS_BASE_URL;
if (!base) throw new Error('Set MEASUREMENTS_BASE_URL to the deployment to verify.');
const sourceId = process.env.MEASUREMENTS_SOURCE_RUN ?? new URL(JSON.parse(await readFile(root + '/docs/verification/results-production.json', 'utf8')).resultsUrl).searchParams.get('run');
const browser = await chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1560, height: 1100 } });
const page = await context.newPage(), errors = [], owned = [], report = { checkedAt: new Date().toISOString(), endpoint: base, sourceRunId: sourceId };
page.on('pageerror', e => errors.push(e.message));
async function request(path, data) {
  const response = data === undefined ? await context.request.get(base + path) : await context.request.post(base + path, { data });
  if (!response.ok()) throw new Error(`${path}: HTTP ${response.status()} ${await response.text()}`);
  return response.json();
}
async function completed(id) {
  await expect.poll(async () => (await request('/api/runs/' + id)).status, { timeout: 45000, intervals: [500, 1000, 1500] }).toBe('completed');
  return request('/api/runs/' + id);
}
async function capture(name) {
  const style = await page.addStyleTag({ content: 'body{height:auto!important;overflow:auto!important}#root,.app{height:auto!important;min-height:100vh}.simulation-workspace{overflow:visible!important}' });
  await page.screenshot({ path: root + '/docs/verification/' + name, fullPage: true }); await style.evaluate(node => node.remove());
}
async function metric(name, nodeId, value, unit, filter) {
  await page.getByRole('button', { name: '＋ Track metric', exact: true }).click(); await page.getByLabel('Metric name').fill(name);
  if (nodeId) { await page.getByLabel('Metric observation level').selectOption('node'); await page.getByLabel('Metric graph node').selectOption(nodeId); await page.getByLabel('Metric numeric expression').fill(value); }
  await page.getByLabel('Metric unit').fill(unit);
  if (filter) { await page.getByLabel('Metric filter mode').selectOption('expression'); await page.getByLabel('Metric filter expression').fill(filter); }
  await page.getByRole('button', { name: 'Save measurement', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
}
try {
  report.anonymousMetricsBlocked = (await context.request.post(base + '/api/runs/measurements/schema', { data: { config: {} } })).status() === 401;
  if (!report.anonymousMetricsBlocked) throw new Error('Production measurement API permits anonymous access.');
  await page.goto(base + '/simulate'); await page.getByLabel('Username').fill(process.env.MEASUREMENTS_USER ?? 'operator');
  await page.getByLabel('Password').fill((await readFile(process.env.MEASUREMENTS_PASSWORD_FILE ?? root + '/production-secrets/operator-password.txt', 'utf8')).trim());
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle' }), page.getByRole('button', { name: 'Sign in', exact: true }).click()]);
  const source = await request('/api/runs/' + sourceId + '/evidence'); if (!source.inputVerified) throw new Error('Pinned source graph cannot be verified.');
  await page.goto(base + `/results?run=${sourceId}&view=reproducibility`); await page.getByRole('button', { name: 'Open pinned graph ↗', exact: true }).click();
  await expect(page.locator('.react-flow__node')).toHaveCount(11); await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await metric('Sticky FS payout', 'free-spin/snapshot-winHistory', 'state.spinCoins / 20', '× stake');
  await metric('Long-bonus FS payout', 'free-spin/snapshot-winHistory', 'state.spinCoins / 20', '× stake', 'state.fsCount >= 18');
  await metric('Awarded spins per bonus', 'free-spins', 'state.fsCount', 'spins');
  await metric('Bonus round payout', null, null, '× stake', 'state.fsCount > 0');
  await page.getByLabel('Simulation spins').fill('100000'); await page.getByLabel('Simulation workers').selectOption('2');
  const frames = [];
  page.on('websocket', socket => socket.on('framereceived', ({ payload }) => { for (const raw of String(payload).split('\x1e')) {
    try { const frame = JSON.parse(raw); if (frame.target === 'ProgressUpdate') frames.push(frame.arguments[0]); } catch { /* Handshake. */ }
  } }));
  const creation = page.waitForResponse(r => r.url().endsWith('/api/runs') && r.request().method() === 'POST');
  await page.getByRole('button', { name: /^▶ Start run$/ }).click(); const run = await (await creation).json(); owned.push(run.id);
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 45000 });
  const final = await completed(run.id), values = final.progress.measurements;
  if (values.length !== 4 || values.some(m => m.errors || !m.count || m.observations !== m.count + m.excluded + m.errors)) throw new Error('Invalid scoped measurements.');
  if (values[0].count !== values[2].sum || values[2].count !== values[3].count) throw new Error('FS visits, awarded spins and bonus rounds disagree.');
  if (values[1].count > values[0].count) throw new Error('Filtered cohort exceeds all FS visits.');
  if (!frames.some(f => f.sampleCount > 0 && f.sampleCount < 100000 && f.measurements?.length === 4)) throw new Error('No live measurement snapshot over the real production WebSocket.');
  report.liveMeasurementFrames = frames.filter(f => f.measurements?.length === 4).length;
  report.runId = run.id; report.configHash = run.configHash; report.measurementHash = run.measurementHash; report.measurements = values;
  report.rounds = final.progress.sampleCount; report.observedRtp = final.progress.runningRtp; report.elapsedMs = final.progress.elapsedMs;
  await expect(page.getByText('Exact expectation', { exact: true }).locator('..')).toContainText('98.000%', { timeout: 30000 });
  const firstCard = page.getByRole('article', { name: 'Tracked metric Sticky FS payout', exact: true });
  await firstCard.getByRole('button', { name: 'Observed range', exact: true }).click();
  await expect(firstCard.getByRole('button', { name: 'Observed range', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await firstCard.getByRole('button', { name: 'Average trend', exact: true }).click();
  await expect(firstCard.getByRole('button', { name: 'Average trend', exact: true })).toHaveAttribute('aria-pressed', 'true');
  report.independentChartScalesVerified = true;
  await capture('measurements-production-dashboard.png');
  const pending = page.waitForEvent('download'); await page.getByRole('button', { name: '↓ Export evidence', exact: true }).click();
  const bundle = JSON.parse(await readFile(await (await pending).path(), 'utf8'));
  if (!bundle.pinnedGraph.inputVerified || bundle.run.measurementHash !== run.measurementHash || bundle.exactReference !== .98 || JSON.stringify(bundle.progress.measurements) !== JSON.stringify(values)) throw new Error('Export loses pinned measurement or mathematical evidence.');
  report.evidenceExportVerified = true; report.exactReference = bundle.exactReference;
  await page.goto(base + `/results?run=${run.id}`); await expect(page.getByRole('heading', { name: 'Tracked measurements', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Replay pinned run', exact: true }).click(); await page.getByLabel('Pinned run workers').selectOption('1');
  const replayCreation = page.waitForResponse(r => r.url().endsWith('/api/runs') && r.request().method() === 'POST');
  await page.getByRole('button', { name: 'Start pinned run', exact: true }).click(); const replay = await (await replayCreation).json(); owned.push(replay.id);
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 45000 }); const replayFinal = await completed(replay.id);
  if (replay.measurementHash !== run.measurementHash || JSON.stringify(replayFinal.progress.measurements) !== JSON.stringify(values) || replayFinal.progress.runningRtp !== final.progress.runningRtp) throw new Error('Measurement replay changed with worker count.');
  report.replay = { runId: replay.id, originalWorkers: 2, workers: 1, bitIdenticalMeasurements: true };
  await page.goto(base + `/results?run=${replay.id}&view=compare&compare=${run.id}`); await expect(page.getByTestId('results-comparison-verdict')).toHaveText('Replay matches');
  const baseline = await request('/api/runs', { configId: run.configId, configVersion: run.configVersion, seed: run.seed, sampleSize: 100000, degreeOfParallelism: 2 }); owned.push(baseline.id);
  const baselineFinal = await completed(baseline.id);
  const fields = ['runningRtp', 'stdErr', 'hitFrequency', 'nonZeroCount', 'volatility', 'maxWin', 'capHits'];
  if (fields.some(key => baselineFinal.progress[key] !== final.progress[key]) || JSON.stringify(baselineFinal.progress.histogram) !== JSON.stringify(final.progress.histogram)) throw new Error('Collection alters game statistics or PRNG.');
  report.uninstrumentedGameStatisticsMatch = true; report.uninstrumentedElapsedMs = baselineFinal.progress.elapsedMs;
  report.timingRatio = final.progress.elapsedMs / Math.max(1, baselineFinal.progress.elapsedMs); // One timing observation, not a benchmark guarantee.
  await page.evaluate(() => localStorage.clear()); await page.goto(base + `/simulate?run=${run.id}`);
  await expect(page.getByRole('article', { name: 'Tracked metric Sticky FS payout', exact: true })).toBeVisible();
  await expect(page.getByTestId('sample-count')).toHaveText('100,000'); await expect(page.getByRole('button', { name: /^▶ Start new run$/ })).toBeEnabled();
  report.emptyStoragePermalinkVerified = true;
  await page.setViewportSize({ width: 390, height: 844 });
  if (!await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)) throw new Error('Mobile dashboard overflows horizontally.');
  await page.getByRole('button', { name: 'Display settings for Sticky FS payout', exact: true }).click();
  const card = page.getByRole('article', { name: 'Tracked metric Sticky FS payout', exact: true });
  await card.getByRole('checkbox', { name: 'Minimum', exact: true }).uncheck(); await card.getByRole('checkbox', { name: 'Maximum', exact: true }).uncheck();
  await expect(card.locator('[data-statistic=min]')).toHaveCount(0); await expect(card.locator('[data-statistic=mean]')).not.toHaveText('—');
  report.mobileAverageOnlyVerified = true; await capture('measurements-production-mobile.png');
  report.browserErrors = errors; if (errors.length) throw new Error(errors.join('; '));
  report.simulateUrl = base + `/simulate?run=${run.id}`; report.resultsUrl = base + `/results?run=${run.id}`;
  await writeFile(root + '/docs/verification/measurements-production.json', JSON.stringify(report, null, 2) + '\n'); console.log(JSON.stringify(report));
} finally {
  for (const id of owned) await context.request.delete(base + '/api/runs/' + id).catch(() => {});
  await browser.close();
}
