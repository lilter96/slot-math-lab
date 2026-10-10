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
    if (response.status() === 429) { await new Promise(resolve => setTimeout(resolve, Math.min(60000, Math.max(1000, Number(response.headers()['retry-after'] ?? '1') * 1000)))); continue; }
    if (!response.ok()) throw new Error(`${path}: HTTP ${response.status()}`); return response.json();
  }
  throw new Error('Read quota did not recover.');
}
async function launch(engine = 'auto', workers = '2') {
  await page.getByLabel('Simulation spins').fill('100000'); await page.getByLabel('Simulation workers').selectOption(workers);
  await page.locator('#execution-configuration').evaluate(el => { el.open = true; }); await page.getByLabel('Sampling engine', { exact: true }).selectOption(engine);
  const accepted = page.waitForResponse(r => r.url().endsWith('/api/runs') && r.request().method() === 'POST' && r.status() !== 429, { timeout: 90000 });
  await page.getByRole('button', { name: /^▶ Start (new )?run$/ }).click(); const response = await accepted;
  if (response.status() !== 202) throw new Error(`Launch rejected HTTP ${response.status()}`); const { id } = await response.json(); owned.push(id);
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 120000 }); return get('/api/runs/' + id);
}
async function reconcile(run, cohort = '') {
  const panel = page.locator('#component-accounting'); await panel.evaluate(el => { el.open = true; });
  await panel.getByLabel('Accounting result cohort').selectOption(cohort ? { label: cohort } : 'overall'); await panel.getByRole('button', { name: 'Reconcile pinned components', exact: true }).click();
  await expect(panel.getByLabel('Component reconciliation result')).toHaveAttribute('data-accounting-status', 'noObservedViolations', { timeout: 90000 });
  const evidence = await get(`/api/runs/${run.id}/evidence`); const artifact = evidence.diagnostics.filter(a => a.kind === 'component-accounting').at(-1);
  if (!artifact || !/^[a-f0-9]{64}$/.test(artifact.inputSha256) || !/^[a-f0-9]{64}$/.test(artifact.outputSha256) || artifact.measurementHash !== run.measurementHash || artifact.output.source.sequence !== run.sequence || artifact.output.source.paidRounds !== 100000 || artifact.output.report.exactViolations !== 0) throw new Error('Incomplete retained accounting identity.');
  return artifact;
}
try {
  report.anonymousBlocked = (await context.request.post(base + '/api/runs/unavailable/measurements/accounting', { data: {} })).status() === 401;
  if (!report.anonymousBlocked) throw new Error('Accounting API is anonymously accessible.');
  await page.goto(base + '/simulate'); await page.getByLabel('Username').fill(process.env.MEASUREMENTS_USER ?? 'operator');
  await page.getByLabel('Password').fill((await readFile(process.env.MEASUREMENTS_PASSWORD_FILE ?? root + '/production-secrets/operator-password.txt', 'utf8')).trim());
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle' }), page.getByRole('button', { name: 'Sign in', exact: true }).click()]);
  await page.goto(base + '/build'); await page.getByLabel('Import project file').setInputFiles(root + '/frontend/e2e/fixtures/component-accounting-model.json');
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click(); await page.getByRole('button', { name: 'Create component accounting plan', exact: true }).click();
  await page.getByLabel('Reconciliation name').fill('Three components'); await page.getByLabel('Accounting observation point').selectOption('sink'); await page.getByLabel('Accounting unit').fill('coins'); await page.getByLabel('Accounting external cost').fill('2'); await page.getByLabel('Accounting total value').selectOption('state:total');
  for (const [i, name] of ['x', 'y', 'z'].entries()) { if (i === 2) await page.getByRole('button', { name: 'Add component', exact: true }).click(); await page.getByLabel(`Component ${i + 1} name`).fill(name.toUpperCase()); await page.getByLabel(`Component ${i + 1} value`).selectOption('state:' + name); }
  await page.getByLabel('Accounting cohorts enabled').check(); await page.getByLabel('Accounting cohort node type').selectOption('fieldAccess'); await page.getByLabel('Accounting cohort state path').fill('cohort');
  await page.getByRole('button', { name: 'Save accounting plan', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
  const run = await launch(), artifact = await reconcile(run, 'all'), r = artifact.output.report;
  const variance = run.progress.measurements[0].analysis.moments.sampleVariance;
  if (Math.abs(r.componentVarianceSum - 3 * variance) > 1e-9 || Math.abs(r.twiceCovarianceSum + 2 * variance) > 1e-9 || Math.abs(r.reconstructedVariance - variance) > 1e-9 || r.covariances.length !== 3) throw new Error('Three-component covariance oracle failed.');
  const canonical = await launch('reference', '1');
  expect(canonical.progress.measurements).toEqual(run.progress.measurements);
  await page.goto(base + `/results?run=${run.id}`); await page.evaluate(() => localStorage.clear()); await page.reload();
  const panel = page.locator('#component-accounting'); await panel.evaluate(el => { el.open = true; }); await panel.getByLabel('Accounting result cohort').selectOption({ label: 'all' });
  await expect(panel.getByLabel('Component reconciliation result')).toHaveAttribute('data-accounting-status', 'noObservedViolations');
  await panel.scrollIntoViewIfNeeded(); await page.screenshot({ path: root + '/docs/verification/component-accounting-production-desktop.png' });
  await page.setViewportSize({ width: 390, height: 844 });
  if (!(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1))) throw new Error('Mobile accounting overflows the viewport.');
  await panel.locator('summary').first().scrollIntoViewIfNeeded(); await page.screenshot({ path: root + '/docs/verification/component-accounting-production-mobile.png' });
  Object.assign(report, { runId: run.id, referenceRunId: canonical.id, measurements: 8, count: r.count, varianceDiagonal: r.componentVarianceSum, twiceCrossCovariances: r.twiceCovarianceSum, observedTotalVariance: r.totalSampleVariance, exactViolations: r.exactViolations,
    workerAndEngineBitIdentical: true, emptyBrowserRestored: true, mobileVerified: true, runtimeProvenance: run.runtimeProvenance, retainedArtifact: artifact.id, resultsUrl: base + `/results?run=${run.id}` });
  // A real game uses the same generic builder: settled payout equals the raw
  // award plus the negative cap deduction, with every paid parent included.
  await page.setViewportSize({ width: 1560, height: 1100 }); await page.goto(base + '/build?project=dog-house'); await expect(page.locator('.react-flow__node')).toHaveCount(12);
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click(); await page.getByRole('button', { name: 'Create component accounting plan', exact: true }).click();
  await page.getByLabel('Reconciliation name').fill('Dog House payout cap'); await page.getByLabel('Component 1 name').fill('Raw payout'); await page.getByLabel('Component 1 value').selectOption('measurement:rawPayout');
  await page.getByLabel('Component 2 name').fill('Negative cap deduction'); await page.getByLabel('Component 2 value').selectOption('visual'); await page.getByLabel('Component 2 node type', { exact: true }).selectOption('binary'); await page.getByLabel('Component 2 operator', { exact: true }).selectOption('Sub');
  await page.getByLabel('Component 2 Right node type', { exact: true }).selectOption('fieldAccess'); await page.getByLabel('Component 2 Right namespace', { exact: true }).selectOption('measurement'); await page.getByLabel('Component 2 Right state path', { exact: true }).fill('capDeduction');
  await page.getByRole('button', { name: 'Save accounting plan', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
  const dog = await launch(), cap = await reconcile(dog);
  await page.goto(base + `/results?run=${dog.id}`); await page.getByRole('button', { name: 'Calculate reference', exact: true }).click(); await expect(page.locator('.results-reference-value')).toHaveText('98.000%', { timeout: 90000 });
  const dogReference = await launchFromSaved(dog);
  expect(dogReference.progress.measurements).toEqual(dog.progress.measurements);
  const primaryFrames = frames.filter(f => f.runId === dog.id && f.measurements?.some(m => m.analysis));
  if (!primaryFrames.length) throw new Error('No rich accounting WebSocket frames.');
  Object.assign(report, { dogHouseRunId: dog.id, dogHouseReferenceRunId: dogReference.id, authoredRtp: .98, observedRtp: dog.progress.runningRtp, capHits: dog.progress.capHits, dogHouseAccountingStatus: cap.output.report.status,
    dogHouseExactViolations: cap.output.report.exactViolations, dogHouseEngineAndWorkerBitIdentical: true, richPrimaryFrames: primaryFrames.length, richFrames: frames.length, simulateUrl: base + `/simulate?run=${dog.id}` });
  if (errors.length) throw new Error('Browser errors: ' + errors.join('; '));
  report.browserErrors = []; await writeFile(root + '/docs/verification/component-accounting-production.json', JSON.stringify(report, null, 2) + '\n');
  console.log(JSON.stringify({ runId: run.id, dogHouseRunId: dog.id, observedRtp: report.observedRtp, authoredRtp: .98, simulateUrl: report.simulateUrl }));
} finally {
  await page.goto('about:blank').catch(() => {});
  for (const id of owned) try { const run = await get('/api/runs/' + id); if (!['completed', 'cancelled', 'failed'].includes(run.status)) await context.request.delete(base + '/api/runs/' + id); } catch { /* preserve terminal archives */ }
  await browser.close();
}
async function launchFromSaved(run) {
  await page.goto(base + `/simulate?run=${run.id}`); await page.locator('.measurement-plan > summary').click(); await page.getByRole('button', { name: 'Use this run’s collection plan', exact: true }).click();
  return launch('reference', '1');
}
