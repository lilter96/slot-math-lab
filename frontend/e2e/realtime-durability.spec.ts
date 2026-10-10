import { test, expect, getWithQuota, cancelOwnedRun, type Page, type WebSocketRoute, type APIRequestContext } from './fixtures';

type Frame = { type?: number; target?: string; arguments?: Record<string, unknown>[] };
async function launch(page: Page) {
  // Launch can wait through an explicit 60s production quota rejection. Socket
  // recovery/heartbeat deadlines below remain independent and unchanged.
  test.setTimeout(Math.max(test.info().timeout, 180000));
  await page.goto('/build?project=dog-house');
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByLabel('Simulation spins').fill('10000000');
  const created = page.waitForResponse(r => r.url().endsWith('/api/runs') && r.request().method() === 'POST' && r.ok(), { timeout: 70000 });
  await page.getByRole('button', { name: /start run/i }).click();
  const run = await (await created).json();
  await expect(page.getByTestId('stream-status')).toContainText('WebSocket live');
  await expect(page.getByTestId('sample-count')).not.toHaveText('0');
  return run;
}
const count = async (page: Page) => Number((await page.getByTestId('sample-count').innerText()).replaceAll(',', ''));
async function stopped(page: Page, request: APIRequestContext, id: string) {
  await cancelOwnedRun(request, id);
  await expect(page.locator('.run-status')).toHaveText('cancelled', { timeout: 15000 });
  const snapshot = await (await getWithQuota(request, `/api/runs/${id}`)).json();
  await expect(page.getByTestId('sample-count')).toHaveText(snapshot.progress.sampleCount.toLocaleString());
  expect(snapshot.sequence).toBe(snapshot.progress.sequence);
  expect(snapshot.streamEpoch).toBe(snapshot.progress.streamEpoch);
  expect(JSON.parse(snapshot.resultJson).sampleCount).toBe(snapshot.progress.sampleCount);
  return snapshot;
}
test('app shell preserves one socket and live observations across constructor navigation', async ({ page, request }) => {
  let sockets = 0; page.on('websocket', () => sockets++);
  const run = await launch(page), before = await count(page);
  for (let i = 0; i < 3; i++) {
    await page.getByRole('tab', { name: 'Build', exact: true }).click();
    await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
    await expect(page.getByTestId('stream-status')).toContainText('WebSocket live');
  }
  expect(sockets).toBe(1); await expect.poll(() => count(page)).toBeGreaterThan(before);
  await stopped(page, request, run.id);
});

test('established sockets recover repeatedly with stable run identity and cumulative metrics', async ({ page, request }) => {
  test.setTimeout(45000);
  const sockets: WebSocketRoute[] = [];
  await page.routeWebSocket('**/hubs/runs?*', socket => { sockets.push(socket); socket.connectToServer(); });
  const run = await launch(page);
  for (let i = 0; i < 3; i++) {
    const before = await count(page); await sockets.at(-1)!.close({ code: 1012, reason: 'durability test' });
    await expect.poll(() => sockets.length).toBe(i + 2);
    await expect(page.getByTestId('stream-status')).toContainText('WebSocket live');
    await expect.poll(() => count(page)).toBeGreaterThanOrEqual(before);
  }
  await expect(page.getByTestId('stream-status')).toContainText('3 reconnects');
  const final = await stopped(page, request, run.id);
  expect(final.configHash).toBe(run.configHash); expect(final.seed).toBe(run.seed);
  await page.screenshot({ path: '../docs/verification/realtime-recovered.png', fullPage: true });
});

test('a silent established socket is replaced by the heartbeat while HTTP metrics continue', async ({ page, request }) => {
  test.setTimeout(45000);
  let blackhole = false, sockets = 0;
  await page.routeWebSocket('**/hubs/runs?*', socket => {
    sockets++; const first = sockets === 1; const server = socket.connectToServer();
    server.onMessage(message => { if (!(first && blackhole)) socket.send(message); });
  });
  const run = await launch(page), before = await count(page); blackhole = true;
  await expect.poll(() => count(page), { timeout: 18000 }).toBeGreaterThan(before);
  await expect.poll(() => sockets, { timeout: 22000 }).toBe(2);
  await expect(page.getByTestId('stream-status')).toContainText('WebSocket live');
  await expect(page.getByTestId('stream-status')).toContainText('1 reconnects');
  await stopped(page, request, run.id);
});

test('network return reconciles terminal results missed offline and stops reconnecting', async ({ page, context, request }) => {
  test.setTimeout(35000);
  let sockets = 0; page.on('websocket', () => sockets++);
  const run = await launch(page);
  await context.setOffline(true);
  await expect(page.getByTestId('stream-status')).toContainText('offline');
  const observed = await count(page), priorSockets = sockets;
  await cancelOwnedRun(request, run.id);
  await expect.poll(async () => (await (await getWithQuota(request, `/api/runs/${run.id}`)).json()).status, { timeout: 70000 }).toBe('cancelled');
  expect(await count(page)).toBe(observed); expect(sockets).toBe(priorSockets);
  await context.setOffline(false);
  await expect(page.locator('.run-status')).toHaveText('cancelled');
  const final = await stopped(page, request, run.id);
  const restored = JSON.parse(await page.evaluate(() => localStorage.getItem('slotmath-simulation-v2')!));
  expect(restored.run.id).toBe(run.id); expect(restored.history.filter((r: { id: string }) => r.id === run.id)).toHaveLength(0); // Current run identity is stored once.
  expect(restored.run.resultJson).toBeNull(); // Rich final evidence is rehydrated from the server.
  expect(restored.progress.sampleCount).toBe(final.progress.sampleCount);
  await page.reload(); await expect(page.locator('.run-status')).toHaveText('cancelled');
  await expect.poll(() => count(page)).toBe(final.progress.sampleCount);
  await expect(page.getByTestId('stream-status')).toContainText('Stream idle');
});

test('duplicate, reordered and malformed frames cannot regress or poison the dashboard', async ({ page, request }) => {
  let socket: WebSocketRoute, previous: Frame | null = null;
  await page.routeWebSocket('**/hubs/runs?*', route => {
    socket = route; const server = route.connectToServer();
    server.onMessage(message => {
      for (const raw of String(message).split('\x1e')) {
        try { const f = JSON.parse(raw); if (f.target === 'ProgressUpdate' && f.arguments[0].sampleCount > 0 && !previous) previous = f; } catch { /* handshake */ }
      }
      route.send(message);
    });
  });
  const run = await launch(page);
  await expect.poll(() => previous).not.toBeNull();
  const before = await count(page);
  for (let i = 0; i < 4; i++) socket!.send(JSON.stringify(previous) + '\x1e');
  const invalid = structuredClone(previous!); invalid.arguments![0].sequence = 99999999;
  invalid.arguments![0].sampleCount = 9999999; invalid.arguments![0].runningRtp = 999;
  socket!.send(JSON.stringify(invalid) + '\x1e');
  await expect.poll(() => count(page)).toBeGreaterThanOrEqual(before);
  expect(await count(page)).toBeLessThan(9999999);
  await expect(page.getByTestId('live-rtp')).not.toContainText('99900');
  const final = await stopped(page, request, run.id);
  expect(final.progress.sequence).toBeLessThan(99999999);
});

test('expired session asks for sign in while retaining the run and last observations', async ({ page }) => {
  let socket: WebSocketRoute;
  await page.routeWebSocket('**/hubs/runs?*', route => { socket = route; route.connectToServer(); });
  const run = await launch(page);
  await page.route('**/api/runs/*', route => route.fulfill({ status: 401, json: { error: 'expired session' } }));
  await socket!.close({ code: 1008, reason: 'session expired' });
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();
  await expect(page.getByText('Session expired. Sign in to recover your existing run.')).toBeVisible();
  const state = JSON.parse(await page.evaluate(() => localStorage.getItem('slotmath-simulation-v2')!));
  expect(state.run.id).toBe(run.id); expect(state.run.status).toBe('running'); expect(state.progress.sampleCount).toBeGreaterThan(0);
});

test('missing server run keeps evidence and enables a new run without fabricating a failed result', async ({ page }) => {
  let socket: WebSocketRoute;
  await page.routeWebSocket('**/hubs/runs?*', route => { socket = route; route.connectToServer(); });
  const run = await launch(page), before = await count(page);
  await page.route('**/api/runs/*', route => route.fulfill({ status: 404, json: { error: 'missing' } }));
  await socket!.close({ code: 1012, reason: 'server lost run' });
  await expect(page.locator('.run-status')).toHaveText('unavailable');
  await expect(page.getByRole('button', { name: /start new run/i })).toBeEnabled();
  expect(await count(page)).toBeGreaterThanOrEqual(before);
  const state = JSON.parse(await page.evaluate(() => localStorage.getItem('slotmath-simulation-v2')!));
  expect(state.run.id).toBe(run.id); expect(state.run.status).toBe('running'); expect(state.run.resultJson).toBeFalsy();
});

test('an authoritative crash checkpoint resets the epoch and charts to the durable prefix', async ({ page, request }) => {
  let socket: WebSocketRoute;
  await page.routeWebSocket('**/hubs/runs?*', route => { socket = route; route.connectToServer(); });
  const run = await launch(page);
  const checkpoint = await (await getWithQuota(request, `/api/runs/${run.id}`)).json();
  await expect.poll(() => count(page)).toBeGreaterThan(checkpoint.progress.sampleCount);
  const epoch = 'b'.repeat(32), resultJson = JSON.stringify({ code: 'RUN_INTERRUPTED', error: 'Server restarted; last checkpoint retained.' });
  const recovered = { ...checkpoint, streamEpoch: epoch, status: 'failed', resultJson,
    progress: { ...checkpoint.progress, streamEpoch: epoch, status: 'failed', resultJson } };
  await page.route(`**/api/runs/${run.id}`, route => route.fulfill({ json: recovered }));
  await socket!.close({ code: 1012, reason: 'crash checkpoint verification' });
  await expect(page.locator('.run-status')).toHaveText('failed');
  await expect(page.getByRole('alert')).toContainText('Server restarted');
  await expect(page.getByTestId('sample-count')).toHaveText(checkpoint.progress.sampleCount.toLocaleString());
  const state = JSON.parse(await page.evaluate(() => localStorage.getItem('slotmath-simulation-v2')!));
  expect(state.run.id).toBe(run.id); expect(state.run.configHash).toBe(run.configHash); expect(state.run.seed).toBe(run.seed);
  expect(state.run.streamEpoch).toBe(epoch);
  expect(state.points.every((point: { n: number }) => point.n <= checkpoint.progress.sampleCount)).toBe(true);
  expect(state.points.at(-1).n).toBe(checkpoint.progress.sampleCount);
  expect(state.points.at(-1).rtp).toBe(checkpoint.progress.runningRtp);
  await page.reload();
  await expect(page.locator('.run-status')).toHaveText('failed');
  await expect(page.getByTestId('sample-count')).toHaveText(checkpoint.progress.sampleCount.toLocaleString());
});
