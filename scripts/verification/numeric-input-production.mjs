import { waitForRunLaunch } from './requests.mjs';
import { chromium } from '../../frontend/node_modules/playwright/index.mjs';
import { expect } from '../../frontend/node_modules/@playwright/test/index.mjs';
import { readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../..', import.meta.url)), base = process.env.MEASUREMENTS_BASE_URL;
if (!base) throw new Error('Set MEASUREMENTS_BASE_URL to the authenticated deployment.');
const browser = await chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1560, height: 1100 } }), page = await context.newPage();
const owned = [], errors = [], frames = [], report = { checkedAt: new Date().toISOString(), endpoint: base, specification: 'Every valid observation has exact authored value 1/1000000000. Its exact decimal residual is zero; each round pays 1.' };
page.on('pageerror', e => errors.push(e.message));
page.on('websocket', socket => socket.on('framereceived', ({ payload }) => {
  for (const raw of String(payload).split('\x1e')) try { const frame = JSON.parse(raw); if (frame.target === 'ProgressUpdate') frames.push(frame.arguments[0]); } catch { /* handshake */ }
}));
async function get(path) {
  for (let attempt = 0; attempt < 3; attempt++) {
    const response = await context.request.get(base + path);
    if (response.status() === 429) { await new Promise(resolve => setTimeout(resolve, Math.min(60000, Math.max(1000, Number(response.headers()['retry-after'] ?? '1') * 1000)))); continue; }
    if (!response.ok()) throw new Error(`Authoritative read HTTP ${response.status()}`); return response.json();
  }
  throw new Error('Authoritative read quota did not recover.');
}
async function metric(name, expression, assertion = false) {
  await page.getByRole('button', { name: '＋ Track metric', exact: true }).click();
  await page.getByLabel('Metric name').fill(name); await page.getByLabel('Metric observation level').selectOption('node');
  await page.getByLabel('Metric graph node').selectOption('sink'); await page.getByLabel('Metric numeric expression').fill(expression); await page.getByLabel('Metric unit').fill('signal');
  if (assertion) { await page.getByLabel('Enable advanced measurement').check(); await page.getByLabel('Assert exactly zero or false').check(); }
  else { await page.getByLabel('Metric filter mode').selectOption('expression'); await page.getByLabel('Metric filter expression').fill('state.signal > 0'); }
  await page.getByRole('button', { name: 'Save measurement', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
}
async function checkSignal() {
  const signal = page.getByRole('article', { name: 'Tracked metric Tiny signal', exact: true });
  for (const reducer of ['min', 'max', 'mean']) await expect(signal.locator(`[data-statistic=${reducer}]`)).toHaveText('1e-9 signal');
  await expect(signal).toContainText('0 invalid'); await expect(signal.locator('svg .plot-label').filter({ hasText: /e-9/ }).first()).toBeVisible();
  return signal;
}
try {
  report.anonymousBlocked = (await context.request.post(base + '/api/runs/missing/verification', { data: {} })).status() === 401;
  if (!report.anonymousBlocked) throw new Error('Anonymous verification execution was allowed.');
  await page.goto(base + '/simulate'); await page.getByLabel('Username').fill(process.env.MEASUREMENTS_USER ?? 'operator');
  await page.getByLabel('Password').fill((await readFile(process.env.MEASUREMENTS_PASSWORD_FILE ?? root + '/production-secrets/operator-password.txt', 'utf8')).trim());
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle' }), page.getByRole('button', { name: 'Sign in', exact: true }).click()]);
  if (process.env.MEASUREMENTS_PREVIOUS_RUN_ID) {
    const previous = await get(`/api/runs/${process.env.MEASUREMENTS_PREVIOUS_RUN_ID}/evidence`);
    expect(previous.inputVerified).toBe(true); expect(previous.computedConfigHash).toBe(previous.run.configHash);
    report.previousIntegerInput = { runId: previous.run.id, inputVerified: previous.inputVerified,
      configHash: previous.run.configHash, computedConfigHash: previous.computedConfigHash, originalProducer: previous.run.runtimeProvenance };
  }
  await page.goto(base + '/build'); await page.getByLabel('Import project file').setInputFiles(root + '/frontend/e2e/fixtures/numeric-input-model.json');
  await expect(page.locator('.react-flow__node')).toHaveCount(2); await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await metric('Tiny signal', 'state.signal'); await metric('Decimal residual', 'state.signal - 0.000000001', true);
  await page.getByRole('button', { name: 'Configure verification profile', exact: true }).click(); await page.getByLabel('Verification profile name').fill('Decimal input identity');
  await page.getByLabel('Required check 1', { exact: true }).selectOption('observation-integrity'); await page.getByLabel('Required minimum 1').fill('100000');
  await page.getByRole('button', { name: 'Add required check', exact: true }).click(); await page.getByLabel('Required measurement 2').selectOption({ label: 'Decimal residual' });
  await page.getByLabel('Required check 2', { exact: true }).selectOption('exact-zero-assertion'); await page.getByLabel('Required minimum 2').fill('100000');
  await page.getByRole('button', { name: 'Save verification profile', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
  const evidence = [];
  for (const engine of ['auto', 'reference']) {
    await page.getByLabel('Simulation spins').fill('100000'); await page.locator('#execution-configuration').evaluate(el => { el.open = true; });
    await page.getByLabel('Sampling engine', { exact: true }).selectOption(engine);
    const launch = waitForRunLaunch(page);
    await page.getByRole('button', { name: /^▶ Start (new )?run$/ }).click(); const response = await launch;
    if (response.status() !== 202) throw new Error(`Launch HTTP ${response.status()}`); const run = await response.json(); owned.push(run.id);
    await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 120000 }); await checkSignal();
    await page.getByRole('button', { name: 'Evaluate pinned verification profile', exact: true }).click();
    await expect(page.getByLabel('Verification profile result')).toHaveAttribute('data-profile-status', 'criteriaMet', { timeout: 90000 });
    const saved = await get(`/api/runs/${run.id}/evidence`); evidence.push(saved);
    expect(saved.run.progress.measurements[0]).toMatchObject({ count: 100000, min: 1e-9, max: 1e-9, mean: 1e-9, errors: 0 });
    expect(saved.run.progress.measurements[1].analysis.assertion).toMatchObject({ checked: 100000, violations: 0, status: 'noObservedViolations' });
    expect(saved.run.progress.runningRtp).toBe(1); expect(saved.pinnedConfig.initialState.signal).toBe(1e-9); expect(saved.inputVerified).toBe(true);
    const download = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export evidence' }).click();
    const bundle = JSON.parse(await readFile(await (await download).path(), 'utf8'));
    expect(bundle.progress).toEqual(saved.run.progress); expect(bundle.run.runtimeProvenance).toEqual(saved.run.runtimeProvenance);
    expect(bundle.pinnedGraph.config.initialState.signal).toBe(1e-9);
    await page.reload(); await checkSignal();
  }
  expect(evidence[1].run.progress.measurements).toEqual(evidence[0].run.progress.measurements);
  expect(evidence[1].run.verificationProfileHash).toBe(evidence[0].run.verificationProfileHash);
  const id = evidence[0].run.id; await page.goto(base + `/results?run=${id}`);
  const row = page.getByRole('table').filter({ has: page.getByRole('columnheader', { name: 'Measurement / scope', exact: true }) }).getByRole('row').filter({ hasText: 'Tiny signal' });
  await expect(row).toContainText('1e-9 signal');
  await page.getByRole('button', { name: 'Replay pinned run', exact: true }).click(); await page.getByLabel('Pinned run engine').selectOption('reference');
  const launched = waitForRunLaunch(page);
  await page.getByRole('button', { name: 'Start pinned run', exact: true }).click(); const replay = await (await launched).json(); owned.push(replay.id);
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 120000 }); await checkSignal();
  expect((await get(`/api/runs/${replay.id}`)).progress.measurements).toEqual(evidence[0].run.progress.measurements);
  // Empty browser workspace state restores the authenticated server archive.
  await page.evaluate(() => localStorage.clear()); await page.goto(base + `/simulate?run=${id}`); await page.reload(); await checkSignal();
  await expect(page.getByLabel('Verification profile result')).toHaveAttribute('data-profile-status', 'criteriaMet');
  await page.getByRole('article', { name: 'Tracked metric Tiny signal', exact: true }).getByRole('heading').scrollIntoViewIfNeeded();
  await page.screenshot({ path: root + '/docs/verification/numeric-input-desktop.png' });
  await page.setViewportSize({ width: 390, height: 844 }); await page.reload(); await checkSignal();
  if (!await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)) throw new Error('Numeric dashboard overflows mobile viewport.');
  await page.getByRole('article', { name: 'Tracked metric Tiny signal', exact: true }).getByRole('heading').scrollIntoViewIfNeeded();
  await page.screenshot({ path: root + '/docs/verification/numeric-input-mobile.png' });
  report.runs = evidence.map(e => ({ id: e.run.id, execution: e.run.execution, paidRounds: e.run.progress.sampleCount, measurements: e.run.progress.measurements, profileHash: e.run.verificationProfileHash, profileReport: e.diagnostics.find(d => d.kind === 'verification-profile')?.output, runtimeProvenance: e.run.runtimeProvenance }));
  report.replayId = replay.id; report.bitIdenticalAcrossEngines = true; report.authoritativeExportVerified = true; report.emptyWorkspaceRestoreVerified = true;
  report.responsive = { desktop: true, mobile: true }; report.webSocketFrames = frames.filter(f => owned.includes(f.runId) && f.measurements?.length).length;
  if (!report.webSocketFrames) throw new Error('No real measurement WS snapshots received.');
  report.browserErrors = errors; if (errors.length) throw new Error(errors.join('\n')); report.success = true;
} catch (error) { report.success = false; report.error = String(error); throw error; }
finally {
  for (const id of owned) { const response = await context.request.get(base + '/api/runs/' + id).catch(() => null); if (response?.ok() && ['pending', 'running'].includes((await response.json()).status)) await context.request.delete(base + '/api/runs/' + id); }
  await writeFile(root + '/docs/verification/numeric-input-production.json', JSON.stringify(report, null, 2) + '\n'); await browser.close();
}
console.log(JSON.stringify({ success: report.success, runs: report.runs.map(r => r.id), replay: report.replayId, measurementFrames: report.webSocketFrames }));
