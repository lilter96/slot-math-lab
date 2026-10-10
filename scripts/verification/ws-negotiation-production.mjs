import { waitForRunLaunch, saveMeasurement } from './requests.mjs';
import { chromium } from '../../frontend/node_modules/playwright/index.mjs';
import { expect } from '../../frontend/node_modules/@playwright/test/index.mjs';
import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../..', import.meta.url)), base = process.env.REALTIME_BASE_URL;
if (!base) throw new Error('Set REALTIME_BASE_URL to the authenticated deployment.');
const validationRetrySeconds = Number(process.env.REALTIME_VALIDATION_RETRY_SECONDS ?? '0');
if (!Number.isInteger(validationRetrySeconds) || validationRetrySeconds < 0 || validationRetrySeconds > 60)
  throw new Error('REALTIME_VALIDATION_RETRY_SECONDS must be an integer from 0 to 60.');
const browser = await chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 1050 } });
const page = await context.newPage(), sockets = [], frames = [], errors = [], owned = [];
const report = { checkedAt: new Date().toISOString(), endpoint: base,
  faultScope: 'Browser-injected negotiation and optional validation quota responses; accepted validation, established WebSockets and authoritative run endpoints use the authenticated production API.' };
page.on('pageerror', error => errors.push(error.message));
await page.routeWebSocket('**/hubs/runs?*', socket => {
  sockets.push(socket); const server = socket.connectToServer();
  server.onMessage(message => {
    for (const raw of String(message).split('\x1e')) try {
      const frame = JSON.parse(raw); if (frame.target === 'ProgressUpdate') frames.push(frame.arguments[0]);
    } catch { /* SignalR handshake. */ }
    socket.send(message);
  });
});
async function get(path) {
  for (let attempt = 0; attempt < 3; attempt++) {
    const response = await context.request.get(base + path);
    if (response.status() === 429) {
      await new Promise(resolve => setTimeout(resolve, Math.min(60000, Math.max(1000,
        Number(response.headers()['retry-after'] ?? '1') * 1000)))); continue;
    }
    expect(response.ok(), `Authoritative HTTP ${response.status()}`).toBe(true); return response.json();
  }
  throw new Error('Authoritative read quota did not recover.');
}
async function cancel(id) {
  for (let attempt = 0; attempt < 3; attempt++) {
    const response = await context.request.delete(base + '/api/runs/' + id);
    if (response.ok() || response.status() === 404) return;
    if (response.status() === 409) {
      expect(['completed', 'cancelled', 'failed']).toContain((await get('/api/runs/' + id)).status); return;
    }
    expect(response.status()).toBe(429);
    await new Promise(resolve => setTimeout(resolve, Math.min(60000, Math.max(1000,
      Number(response.headers()['retry-after'] ?? '1') * 1000))));
  }
  throw new Error('Cancellation quota did not recover.');
}
async function signIn() {
  await page.getByLabel('Username').fill(process.env.REALTIME_USER ?? 'operator');
  await page.getByLabel('Password').fill((await readFile(process.env.REALTIME_PASSWORD_FILE
    ?? root + '/production-secrets/operator-password.txt', 'utf8')).trim());
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle' }),
    page.getByRole('button', { name: 'Sign in', exact: true }).click()]);
}
const count = async () => Number((await page.getByTestId('sample-count').innerText()).replaceAll(',', ''));
try {
  await page.goto(base + '/build?project=dog-house'); await signIn();
  const script = await page.locator('script[src*="/assets/index-"]').getAttribute('src');
  if (!script) throw new Error('Could not identify the deployed frontend bundle.');
  const bundle = await context.request.get(base + script);
  expect(bundle.ok()).toBe(true);
  report.frontend = { asset: script, sha256: createHash('sha256').update(await bundle.body()).digest('hex') };
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByRole('button', { name: '＋ Track metric', exact: true }).click();
  await page.getByLabel('Metric name').fill('Sticky FS transport evidence');
  await page.getByLabel('Metric observation level').selectOption('node');
  await page.getByLabel('Metric graph node').selectOption('free-spin/snapshot-winHistory');
  await page.getByLabel('Metric numeric expression').fill('state.spinCoins / 20');
  const validationAttempts = [];
  if (validationRetrySeconds) await page.route('**/api/runs/measurements/schema', route => {
    if (route.request().method() !== 'POST' || !route.request().postDataJSON().measurements?.length) return route.continue();
    validationAttempts.push(Date.now());
    return validationAttempts.length === 1 ? route.fulfill({ status: 429,
      headers: { 'Retry-After': String(validationRetrySeconds) }, body: '' }) : route.continue();
  });
  await Promise.all([saveMeasurement(page), ...(validationRetrySeconds ? [(async () => {
    await expect(page.getByRole('dialog').getByRole('status')).toContainText(`retrying the rejected calculation in ${validationRetrySeconds}s`);
    await expect(page.getByLabel('Metric name')).toBeDisabled();
  })()] : [])]);
  if (validationRetrySeconds) {
    expect(validationAttempts.length).toBeGreaterThanOrEqual(2);
    expect(validationAttempts[1] - validationAttempts[0]).toBeGreaterThanOrEqual(validationRetrySeconds * 1000 - 100);
    await page.unroute('**/api/runs/measurements/schema');
  }
  report.validation = { accepted: true, injectedRetryAfterSeconds: validationRetrySeconds,
    retryDelayMs: validationAttempts.length > 1 ? validationAttempts[1] - validationAttempts[0] : null };
  await page.getByLabel('Simulation spins').fill('10000000');
  const accepted = waitForRunLaunch(page);
  await page.getByRole('button', { name: /start run/i }).click();
  const launched = await accepted; expect(launched.status()).toBe(202);
  const run = await launched.json(); owned.push(run.id);
  await expect(page.getByTestId('stream-status')).toContainText('WebSocket live', { timeout: 70000 });
  await expect.poll(() => frames.some(frame => frame.runId === run.id && frame.measurements?.[0]?.count > 0), { timeout: 20000 }).toBe(true);
  report.run = { id: run.id, seed: run.seed, configHash: run.configHash, measurementHash: run.measurementHash,
    runtimeProvenance: run.runtimeProvenance };
  report.quotas = [];
  for (const format of ['seconds', 'http-date']) {
    const attempts = [], previousFrames = frames.length;
    let deadline = 0;
    await page.route('**/hubs/runs/negotiate**', route => {
      attempts.push(Date.now());
      if (attempts.length > 1) return route.continue();
      const header = format === 'seconds' ? '6' : new Date(Date.now() + 8000).toUTCString();
      deadline = format === 'seconds' ? Date.now() + 6000 : Date.parse(header);
      return route.fulfill({ status: 429, headers: { 'Retry-After': header }, body: '' });
    });
    await sockets.at(-1).close({ code: 1012, reason: 'production negotiation quota verification' });
    await expect.poll(() => attempts.length).toBe(1);
    await expect(page.getByTestId('stream-status')).toContainText('HTTP recovery active');
    await page.getByRole('button', { name: 'Reconnect now', exact: true }).click();
    await expect.poll(() => attempts.length, { timeout: 70000 }).toBeGreaterThanOrEqual(2);
    expect(attempts[1]).toBeGreaterThanOrEqual(deadline - 100);
    await expect(page.getByTestId('stream-status')).toContainText('WebSocket live', { timeout: 70000 });
    await expect.poll(() => frames.length, { timeout: 15000 }).toBeGreaterThan(previousFrames);
    report.quotas.push({ format, retryDelayMs: attempts[1] - attempts[0], noEarlyAttempt: true,
      manualWakePreservedDeadline: true, realFramesResumed: true });
    await page.unroute('**/hubs/runs/negotiate**');
  }
  let attempts = 0;
  await page.route('**/hubs/runs/negotiate**', route => { attempts++; return route.fulfill({ status: 401, body: '' }); });
  await sockets.at(-1).close({ code: 1012, reason: 'production negotiation authentication verification' });
  await expect(page.getByTestId('stream-status')).toContainText('Sign in required');
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();
  const retained = await count();
  await page.evaluate(() => window.dispatchEvent(new Event('pageshow'))); await page.waitForTimeout(2200);
  expect(attempts).toBe(1);
  // HTTP remains independently authorized, so the authentication pause above
  // is proven to originate from the negotiation response alone.
  expect((await get('/api/runs/' + run.id)).status).toBe('running');
  await page.unroute('**/hubs/runs/negotiate**'); await signIn();
  await expect(page.getByTestId('stream-status')).toContainText('WebSocket live', { timeout: 70000 });
  expect(await count()).toBeGreaterThanOrEqual(retained);
  report.authentication = { negotiationOnlyRejection: true, retriesPaused: true,
    signInRestoredSameRun: JSON.parse(await page.evaluate(() => localStorage.getItem('slotmath-simulation-v2'))).run.id === run.id };
  expect(report.authentication.signInRestoredSameRun).toBe(true);
  await cancel(run.id); await expect(page.locator('.run-status')).toHaveText('cancelled', { timeout: 70000 });
  const evidence = await get('/api/runs/' + run.id + '/evidence'), final = evidence.run.progress;
  expect(evidence.inputVerified).toBe(true); expect(evidence.run.configHash).toBe(run.configHash);
  expect(evidence.run.measurementHash).toBe(run.measurementHash); expect(evidence.run.seed).toBe(run.seed);
  expect(final.sampleCount).toBeGreaterThan(0); expect(final.sampleCount).toBeLessThan(10000000);
  expect(final.histogram.reduce((sum, bin) => sum + bin.count, 0)).toBe(final.sampleCount);
  expect(final.measurements[0].count).toBeGreaterThan(0); expect(final.measurements[0].errors).toBe(0);
  const download = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export evidence' }).click();
  const exported = JSON.parse(await readFile(await (await download).path(), 'utf8'));
  expect(exported.progress).toEqual(final); expect(exported.run.id).toBe(run.id);
  await page.reload(); await expect(page.getByTestId('sample-count')).toHaveText(final.sampleCount.toLocaleString(), { timeout: 70000 });
  report.final = { paidRounds: final.sampleCount, sequence: final.sequence, streamEpoch: final.streamEpoch,
    measurement: final.measurements[0], archiveVerified: true, exportVerified: true, reloadVerified: true };
  report.realMeasurementFrames = frames.filter(frame => frame.runId === run.id && frame.measurements?.[0]?.count > 0).length;
  report.browserErrors = errors; expect(errors).toEqual([]); report.success = true;
} catch (error) { report.success = false; report.error = String(error); throw error; }
finally {
  await page.goto('about:blank').catch(() => {});
  for (const id of owned) await cancel(id);
  await writeFile(root + '/docs/verification/' + (process.env.REALTIME_REPORT_FILE ?? 'ws-negotiation-production.json'), JSON.stringify(report, null, 2) + '\n');
  await browser.close();
}
console.log(JSON.stringify({ success: report.success, run: report.run.id, quotas: report.quotas,
  realMeasurementFrames: report.realMeasurementFrames }));
