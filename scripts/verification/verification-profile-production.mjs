import { waitForRunLaunch, saveMeasurement } from './requests.mjs';
import { chromium } from '../../frontend/node_modules/playwright/index.mjs';
import { expect } from '../../frontend/node_modules/@playwright/test/index.mjs';
import { readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
const root = fileURLToPath(new URL('../..', import.meta.url)), base = process.env.MEASUREMENTS_BASE_URL;
if (!base) throw new Error('Set MEASUREMENTS_BASE_URL to the authenticated deployment.');
const browser = await chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1560, height: 1100 } }), page = await context.newPage();
const errors = [], frames = [], owned = [], report = { checkedAt: new Date().toISOString(), endpoint: base };
page.on('pageerror', e => errors.push(e.message));
page.on('websocket', socket => socket.on('framereceived', ({ payload }) => {
  for (const raw of String(payload).split('\x1e')) try { const f = JSON.parse(raw); if (f.target === 'ProgressUpdate') frames.push(f.arguments[0]); } catch { /* handshake */ }
}));
async function get(path) {
  for (let i = 0; i < 3; i++) {
    const response = await context.request.get(base + path);
    if (response.status() === 429) { await new Promise(r => setTimeout(r, Math.min(60000, Math.max(1000, Number(response.headers()['retry-after'] ?? 1) * 1000)))); continue; }
    if (!response.ok()) throw new Error(`HTTP ${response.status()} reading ${path}`); return response.json();
  }
  throw new Error('Read quota did not recover.');
}
async function metric(name, field, reference, tolerance) {
  await page.getByRole('button', { name: '＋ Track metric', exact: true }).click(); await page.getByLabel('Metric name').fill(name);
  if (field) { await page.getByLabel('Metric observation level').selectOption('node'); await page.getByLabel('Metric graph node').selectOption('sink'); await page.getByLabel('Metric numeric expression').fill('state.' + field); }
  await page.getByLabel('Enable advanced measurement').check(); await page.getByText('Uncertainty, precision and reference checks', { exact: true }).click();
  await page.getByLabel('Independent measurement subjects').check(); await page.getByLabel('Reference mean', { exact: true }).fill(String(reference)); await page.getByLabel('Acceptance tolerance').fill(String(tolerance));
  await saveMeasurement(page);
}
async function profile(name, names, minimum) {
  await page.getByRole('button', { name: 'Configure verification profile', exact: true }).click(); await page.getByLabel('Verification profile name').fill(name);
  for (const [i, label] of names.entries()) {
    if (i) await page.getByRole('button', { name: 'Add required check', exact: true }).click();
    await page.getByLabel(`Required measurement ${i + 1}`).selectOption({ label }); await page.getByLabel(`Required check ${i + 1}`, { exact: true }).selectOption('mean-equivalence'); await page.getByLabel(`Required minimum ${i + 1}`).fill(String(minimum));
  }
  await page.getByRole('button', { name: 'Save verification profile', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
}
async function launch(engine = 'auto', workers = '2') {
  await page.getByLabel('Simulation spins').fill('100000'); await page.getByLabel('Simulation workers').selectOption(workers);
  await page.locator('#execution-configuration').evaluate(el => { el.open = true; }); await page.getByLabel('Sampling engine', { exact: true }).selectOption(engine);
  const accepted = waitForRunLaunch(page);
  await page.getByRole('button', { name: /^▶ Start (new )?run$/ }).click(); const response = await accepted;
  if (response.status() !== 202) throw new Error(`Launch rejected HTTP ${response.status()}`); const { id } = await response.json(); owned.push(id);
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 120000 }); return get('/api/runs/' + id);
}
async function evaluate(run) {
  await page.getByRole('button', { name: 'Evaluate pinned verification profile', exact: true }).click();
  await expect(page.getByLabel('Verification profile result')).toBeVisible({ timeout: 90000 });
  const evidence = await get(`/api/runs/${run.id}/evidence`), artifact = evidence.diagnostics.find(d => d.kind === 'verification-profile');
  if (!artifact || artifact.output.report.profileHash !== run.verificationProfileHash || artifact.output.source.sequence !== run.sequence || artifact.output.source.paidRounds !== 100000 || artifact.measurementHash !== run.measurementHash) throw new Error('Retained profile identity does not match pinned evidence.');
  return artifact;
}
try {
  report.anonymousBlocked = (await context.request.post(base + '/api/runs/unavailable/verification', { data: {} })).status() === 401;
  if (!report.anonymousBlocked) throw new Error('Verification endpoint is anonymously accessible.');
  await page.goto(base + '/simulate'); await page.getByLabel('Username').fill(process.env.MEASUREMENTS_USER ?? 'operator');
  await page.getByLabel('Password').fill((await readFile(process.env.MEASUREMENTS_PASSWORD_FILE ?? root + '/production-secrets/operator-password.txt', 'utf8')).trim());
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle' }), page.getByRole('button', { name: 'Sign in', exact: true }).click()]);
  await page.goto(base + '/build'); await page.getByLabel('Import project file').setInputFiles(root + '/frontend/e2e/fixtures/component-accounting-model.json'); await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await metric('Mean X', 'x', 1, .02); await metric('Mean Y', 'y', 1, .02); await profile('Independent fixture · required means', ['Mean X', 'Mean Y'], 100000);
  const run = await launch(), fixture = await evaluate(run); if (fixture.output.report.status !== 'criteriaMet' || Math.abs(fixture.output.report.allocatedAlpha - .05) > 1e-14) throw new Error('Independent profile fixture or family budget failed.');
  const reference = await launch('reference', '1'); expect(reference.progress.measurements).toEqual(run.progress.measurements); expect(reference.verificationProfileHash).toBe(run.verificationProfileHash);
  // Values 0/2 have mean 1 and nonzero probability 1/2. A deliberately
  // incorrect probability reference of 1 must fail, despite the numeric mean.
  await page.locator('.measurement-plan > summary').click();
  await page.locator('.measurement-plan li').filter({ hasText: 'Mean X' }).getByRole('button', { name: 'Edit', exact: true }).click();
  await page.getByText('Uncertainty, precision and reference checks', { exact: true }).click(); await page.getByLabel('Reference statistic', { exact: true }).selectOption('probability');
  await page.getByLabel('Proven minimum', { exact: true }).fill('0'); await page.getByLabel('Proven maximum', { exact: true }).fill('2'); await page.getByLabel('Acceptance tolerance').fill('.1');
  await saveMeasurement(page);
  const probabilityRun = await launch(), probability = await evaluate(probabilityRun), probabilityRow = probability.output.report.criteria[0];
  if (probability.output.report.status !== 'discrepancy' || probabilityRow.interval.method !== 'Clopper–Pearson, two-sided' || probabilityRow.evidence.observed > .6 || probabilityRun.progress.measurements[0].mean < .9) throw new Error('Numeric mean was incorrectly used as nonzero-event probability.');
  Object.assign(report, { probabilityRunId: probabilityRun.id, probabilityStatus: probability.output.report.status, numericMean: probabilityRun.progress.measurements[0].mean, nonzeroProbability: probabilityRow.evidence.observed, probabilityInterval: probabilityRow.interval });
  await page.goto(base + `/results?run=${run.id}`); await page.evaluate(() => localStorage.clear()); await page.reload();
  await expect(page.getByLabel('Verification profile result')).toHaveAttribute('data-profile-status', 'criteriaMet');
  await page.locator('#verification-profile').scrollIntoViewIfNeeded(); await page.screenshot({ path: root + '/docs/verification/verification-profile-production-desktop.png' });
  await page.setViewportSize({ width: 390, height: 844 }); if (!(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1))) throw new Error('Profile mobile viewport overflow.');
  await page.locator('#verification-profile > summary').scrollIntoViewIfNeeded(); await page.screenshot({ path: root + '/docs/verification/verification-profile-production-mobile.png' });
  Object.assign(report, { fixtureRunId: run.id, fixtureReferenceRunId: reference.id, fixtureStatus: fixture.output.report.status, fixtureProfileHash: run.verificationProfileHash, fixtureAllocatedAlpha: fixture.output.report.allocatedAlpha,
    fixtureArtifact: fixture.id, engineAndWorkerBitIdentical: true, emptyBrowserRestored: true, mobileVerified: true });
  await page.setViewportSize({ width: 1560, height: 1100 }); await page.goto(base + '/build?project=dog-house'); await expect(page.locator('.react-flow__node')).toHaveCount(12); await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await metric('Dog House return precision', null, .98, .005); await profile('98% target · 0.5 percentage-point precision', ['Dog House return precision'], 100000);
  const dog = await launch(), decision = await evaluate(dog), row = decision.output.report.criteria[0];
  // Independent fixed-count normal interval using the standard 97.5% normal
  // quantile. The fixture has no authored bounded/sequential alternative.
  const sample = dog.progress.measurements[0], width = 1.959963984540054 * sample.stdDev / Math.sqrt(sample.count);
  if (Math.abs(row.interval.lower - (sample.mean - width)) > 1e-7 || Math.abs(row.interval.upper - (sample.mean + width)) > 1e-7) throw new Error('Independent fixed-count interval oracle differs.');
  const expected = row.interval.lower >= .975 && row.interval.upper <= .985 ? 'withinPrecision' : row.interval.upper < .975 || row.interval.lower > .985 ? 'discrepancy' : 'insufficient';
  if (row.status !== expected) throw new Error('The precision decision does not follow its pinned interval and tolerance.');
  await page.goto(base + `/results?run=${dog.id}`); await page.getByRole('button', { name: 'Calculate reference', exact: true }).click(); await expect(page.locator('.results-reference-value')).toHaveText('98.000%', { timeout: 90000 });
  await page.getByRole('button', { name: 'Replay pinned run', exact: true }).click(); await page.getByLabel('Pinned run engine').selectOption('reference'); await page.getByLabel('Pinned run workers').selectOption('1');
  const accepted = waitForRunLaunch(page);
  await page.getByRole('button', { name: 'Start pinned run', exact: true }).click(); const replayResponse = await accepted;
  if (replayResponse.status() !== 202) throw new Error('Pinned replay was rejected.'); const replay = await replayResponse.json(); owned.push(replay.id);
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 120000 }); const other = await get('/api/runs/' + replay.id);
  expect(other.progress.measurements).toEqual(dog.progress.measurements); expect(other.verificationProfileHash).toBe(dog.verificationProfileHash);
  await page.goto(base + `/simulate?run=${dog.id}`); await expect(page.getByTestId('sample-count')).toHaveText('100,000');
  await expect(page.locator('#verification-profile')).toContainText(dog.verificationProfile.name);
  await expect(page.getByLabel('Verification profile result')).toHaveAttribute('data-profile-status', decision.output.report.status);
  const download = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export evidence' }).click();
  const exported = JSON.parse(await readFile(await (await download).path(), 'utf8'));
  if (exported.schemaVersion !== 'slotmath.simulation.v2' || exported.run.id !== dog.id || exported.run.verificationProfileHash !== dog.verificationProfileHash
    || !exported.pinnedGraph.inputVerified || exported.pinnedGraph.computedConfigHash !== dog.configHash) throw new Error('Authenticated simulation export lost pinned evidence.');
  expect(exported.progress).toEqual(dog.progress); expect(exported.run.progress).toEqual(exported.progress);
  expect(exported.run.runtimeProvenance).toEqual(dog.runtimeProvenance);
  report.authoritativeSimulationExportVerified = true;
  report.simulatePermalinkRestored = true;
  const rich = frames.filter(f => f.runId === dog.id && f.measurements?.some(m => m.analysis)); if (!rich.length) throw new Error('No rich WebSocket metric evidence.');
  Object.assign(report, { dogHouseRunId: dog.id, dogHouseReferenceRunId: replay.id, authoredRtp: .98, observedRtp: dog.progress.runningRtp, requiredTolerance: .005,
    dogHouseProfileStatus: decision.output.report.status, meanInterval: row.interval, dogHouseProfileHash: dog.verificationProfileHash, dogHouseArtifact: decision.id,
    independentIntervalVerified: true, dogHouseEngineAndWorkerBitIdentical: true, richPrimaryFrames: rich.length, richFrames: frames.length, runtimeProvenance: dog.runtimeProvenance,
    simulateUrl: base + `/simulate?run=${dog.id}`, resultsUrl: base + `/results?run=${dog.id}`, fixtureResultsUrl: base + `/results?run=${run.id}` });
  if (errors.length) throw new Error('Browser errors: ' + errors.join('; ')); report.browserErrors = [];
  await writeFile(process.env.MEASUREMENTS_REPORT_FILE ?? root + '/docs/verification/verification-profile-production.json', JSON.stringify(report, null, 2) + '\n');
  console.log(JSON.stringify({ runId: dog.id, status: report.dogHouseProfileStatus, authoredRtp: .98, observedRtp: report.observedRtp, simulateUrl: report.simulateUrl }));
} finally {
  await page.goto('about:blank').catch(() => {});
  for (const id of owned) try { const run = await get('/api/runs/' + id); if (!['completed', 'cancelled', 'failed'].includes(run.status)) await context.request.delete(base + '/api/runs/' + id); } catch { /* Preserve terminal archives. */ }
  await browser.close();
}
