import { chromium } from '../../frontend/node_modules/playwright/index.mjs';
import { expect } from '../../frontend/node_modules/@playwright/test/index.mjs';
import { readFile, writeFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
const root = fileURLToPath(new URL('../..', import.meta.url));
const base = process.env.REALTIME_BASE_URL, container = process.env.REALTIME_CRASH_CONTAINER;
if (!base || !container) throw new Error('Set REALTIME_BASE_URL and REALTIME_CRASH_CONTAINER. This verification kills and restarts the specified API container.');
const browser = await chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 1050 } });
const page = await context.newPage(), sockets = [], errors = [], frames = [], report = { checkedAt: new Date().toISOString(), endpoint: base };
let muted = false;
const ownedRuns = [];
page.on('pageerror', error => errors.push(error.message));
await page.routeWebSocket('**/hubs/runs?*', socket => {
  const server = socket.connectToServer(); const index = sockets.length; sockets.push(socket);
  server.onMessage(message => {
    for (const raw of String(message).split('\x1e')) {
      try { const f = JSON.parse(raw); if (f.target === 'ProgressUpdate') frames.push(f.arguments[0]); } catch {}
    }
    if (!(muted && index === 0)) socket.send(message);
  });
});
async function signIn() {
  await page.getByLabel('Username').fill(process.env.REALTIME_USER ?? 'operator');
  await page.getByLabel('Password').fill((await readFile(process.env.REALTIME_PASSWORD_FILE ?? root + '/production-secrets/operator-password.txt', 'utf8')).trim());
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle' }), page.getByRole('button', { name: 'Sign in', exact: true }).click()]);
  await page.getByRole('tab', { name: 'Simulate', exact: true }).waitFor();
}
async function create(spins) {
  await page.getByLabel('Simulation spins').fill(String(spins));
  const response = page.waitForResponse(r => r.url().endsWith('/api/runs') && r.request().method() === 'POST');
  await page.getByRole('button', { name: /start.*run/i }).click();
  const run = await (await response).json(); ownedRuns.push(run.id);
  await expect(page.getByTestId('stream-status')).toContainText('WebSocket live', { timeout: 30000 });
  await expect(page.getByTestId('sample-count')).not.toHaveText('0'); return run;
}
const count = async () => Number((await page.getByTestId('sample-count').innerText()).replaceAll(',', ''));
const get = async id => (await context.request.get(base + '/api/runs/' + id)).json();
try {
  await page.goto(base + '/build?project=dog-house'); await signIn();
  await page.getByRole('button', { name: 'Open full game graph', exact: true }).waitFor();
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  const run = await create(10000000);
  const atStart = await count(); muted = true;
  await expect.poll(count, { timeout: 18000 }).toBeGreaterThan(atStart);
  await expect.poll(() => sockets.length, { timeout: 22000 }).toBe(2);
  await expect(page.getByTestId('stream-status')).toContainText('WebSocket live');
  report.silentSocket = { replaced: true, httpAdvanced: true, before: atStart, after: await count(), socketConnections: sockets.length };
  await sockets.at(-1).close({ code: 1012, reason: 'production reconnect verification' });
  await expect.poll(() => sockets.length).toBe(3);
  await expect(page.getByTestId('stream-status')).toContainText('WebSocket live');
  report.repeatedRecovery = true;
  await context.clearCookies();
  await sockets.at(-1).close({ code: 1008, reason: 'session expiry verification' });
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible({ timeout: 10000 });
  const beforeLogin = await count(); await signIn();
  await expect(page.getByTestId('stream-status')).toContainText('WebSocket live', { timeout: 30000 });
  const afterLogin = await get(run.id);
  if (afterLogin.configHash !== run.configHash || afterLogin.seed !== run.seed) throw new Error('Pinned input changed across login');
  report.authentication = { expiredSessionPrompt: true, recoveredExistingRun: true, sameSeedAndHash: true, before: beforeLogin, after: await count() };
  await expect.poll(count, { timeout: 15000 }).toBeGreaterThan(50000);
  const beforeCrash = await get(run.id), observedBeforeCrash = await count();
  for (const args of [['kill', '--signal=KILL', container], ['start', container]]) {
    const result = spawnSync('docker', args, { encoding: 'utf8' }); if (result.status !== 0) throw new Error('Crash operation failed');
  }
  await expect(page.locator('.run-status')).toHaveText('failed', { timeout: 45000 });
  await expect(page.getByRole('alert')).toContainText('Server restarted');
  const recovered = await get(run.id), state = JSON.parse(await page.evaluate(() => localStorage.getItem('slotmath-simulation-v2')));
  if (recovered.streamEpoch === beforeCrash.streamEpoch || JSON.parse(recovered.resultJson).code !== 'RUN_INTERRUPTED') throw new Error('Missing crash epoch');
  if (recovered.configHash !== run.configHash || recovered.seed !== run.seed || state.run.id !== run.id) throw new Error('Crash changed immutable input');
  if (recovered.progress.histogram.reduce((sum, bin) => sum + bin.count, 0) !== recovered.progress.sampleCount) throw new Error('Checkpoint histogram inconsistent');
  if (await count() !== recovered.progress.sampleCount || state.points.some(point => point.n > recovered.progress.sampleCount)) throw new Error('Undurable observations survived crash reset');
  report.crashRecovery = { runId: run.id, interrupted: true, epochChanged: true, checkpointRounds: recovered.progress.sampleCount,
    observedBeforeCrash, beforeRevision: beforeCrash.sequence, checkpointRevision: recovered.sequence, inputPreserved: true,
    chartResetToDurablePrefix: true, result: JSON.parse(recovered.resultJson), checkpointAgeMs: beforeCrash.progress.elapsedMs - recovered.progress.elapsedMs };
  await page.reload(); await expect(page.locator('.run-status')).toHaveText('failed');
  await expect(page.getByTestId('sample-count')).toHaveText(recovered.progress.sampleCount.toLocaleString());
  report.crashRecovery.reloadStable = true;
  const style = await page.addStyleTag({ content: 'body{height:auto!important;overflow:auto!important}#root,.app{height:auto!important;min-height:100vh}.simulation-workspace{overflow:visible!important}' });
  await page.screenshot({ path: root + '/docs/verification/realtime-crash-production.png', fullPage: true }); await style.evaluate(node => node.remove());
  const started = Date.now(), complete = await create(100000);
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 30000 });
  const stored = await get(complete.id), result = JSON.parse(stored.resultJson);
  if (result.rtp !== 0.9967189999999915 || result.sampleCount !== 100000) throw new Error('Deterministic RTP changed');
  report.completeRun = { runId: complete.id, elapsedMs: result.elapsedMs, wallMs: Date.now() - started, roundCount: result.sampleCount,
    observedRtp: result.rtp, sameDeterministicMath: true, resultDeliveredInline: frames.some(p => p.runId === complete.id && p.status === 'completed' && p.resultJson),
    revision: stored.sequence, graphHash: complete.configHash, seed: complete.seed, streamScheme: complete.streamScheme };
  report.browserErrors = errors;
  if (errors.length) throw new Error('Browser errors: ' + errors.join(', '));
  await writeFile(root + '/docs/verification/realtime-production.json', JSON.stringify(report, null, 2) + '\n');
  console.log(JSON.stringify(report));
} finally {
  for (const id of ownedRuns) await context.request.delete(base + '/api/runs/' + id).catch(() => {});
  await browser.close();
}
