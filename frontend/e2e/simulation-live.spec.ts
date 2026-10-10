import { waitForRunLaunch, test, expect, getWithQuota } from './fixtures';
import { readFile } from 'node:fs/promises';

test('Real WebSocket updates during execution, reload restores run, cancellation retains actual payouts', async ({ page }) => {
  test.setTimeout(180000);
  const messages: { sampleCount: number; status: string; sequence: number; histogram: { count: number }[] }[] = [];
  const sockets: string[] = [];
  page.on('websocket', socket => {
    sockets.push(socket.url());
    socket.on('framereceived', ({ payload }) => {
      for (const frame of String(payload).split('\x1e')) {
        try { const data = JSON.parse(frame); if (data.target === 'ProgressUpdate') messages.push(data.arguments[0]); } catch { /* handshake */ }
      }
    });
  });
  await page.goto('/build?project=dog-house');
  await page.getByRole('button', { name: 'Open base game graph', exact: true }).click();
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  // Keep the job alive through quota recovery/reload/cancellation. The Core
  // LiveProgressTests separately verify publication inside the first chunk;
  // a browser can subscribe after that chunk has already completed.
  await page.locator('#execution-configuration').evaluate((element: HTMLDetailsElement) => { element.open = true; });
  await page.getByLabel('Sampling engine', { exact: true }).selectOption('reference');
  await page.getByLabel('Simulation workers', { exact: true }).selectOption('1');
  await page.getByLabel('Simulation spins').fill('10000000');
  const creation = waitForRunLaunch(page);
  await page.getByRole('button', { name: /start run/i }).click();
  const run = await (await creation).json();
  expect(run.configHash).toMatch(/^[a-f0-9]{64}$/);
  const pinned = await (await getWithQuota(page.request, `/api/configs/${run.configId}`)).json();
  expect(pinned.config.nodes).toHaveLength(12);
  expect(pinned.config.nodes.find((node: { id: string }) => node.id === 'bonus-completed')).toMatchObject({ nodeType: 'modifyState', expressionId: 'bonus-completed', outputKey: 'bonusCompleted' });
  await expect(page.getByTestId('stream-status')).toContainText('WebSocket live', { timeout: 70000 });
  await expect.poll(() => messages.some(m => m.status === 'running' && m.sampleCount > 0 && m.sampleCount < 10000000), { timeout: 15000 }).toBe(true);
  await expect(page.getByTestId('live-rtp')).not.toHaveText('—');
  await expect(page.getByTestId('live-hit-frequency')).not.toHaveText('—');
  await expect(page.getByRole('img', { name: /RTP convergence chart/ })).toHaveAttribute('aria-label', /[1-9]\d* observations/);
  for (const m of messages.filter(m => m.sampleCount > 0)) expect(m.histogram.reduce((sum, b) => sum + b.count, 0)).toBe(m.sampleCount);
  await page.screenshot({ path: '../docs/verification/simulation-live.png', fullPage: true });
  await page.reload();
  await expect(page.getByTestId('stream-status')).toContainText('WebSocket live', { timeout: 70000 });
  await expect(page.getByTestId('sample-count')).not.toHaveText('0');
  expect(sockets.filter(url => url.includes('/hubs/runs')).length).toBeGreaterThanOrEqual(2);
  await page.getByRole('button', { name: /cancel run/i }).click();
  await expect(page.locator('.run-status')).toHaveText('cancelled', { timeout: 15000 });
  const stored = await (await getWithQuota(page.request, `/api/runs/${run.id}`)).json();
  const result = JSON.parse(stored.resultJson);
  expect(result.sampleCount).toBeGreaterThan(0);
  expect(result.sampleCount).toBeLessThan(10000000);
  expect(result.sampleCount).toBe(stored.progress.sampleCount);
  expect(stored.progress.histogram.reduce((sum: number, b: { count: number }) => sum + b.count, 0)).toBe(result.sampleCount);
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export evidence' }).click();
  const evidence = JSON.parse(await readFile((await (await download).path())!, 'utf8'));
  expect(evidence.run.configHash).toBe(run.configHash);
  // The archive retains authored enum names and omits null defaults; the
  // config endpoint materializes some enum codes/defaults. Verify canonical
  // input identity and the complete root structure across those representations.
  expect(evidence.pinnedGraph.inputVerified).toBe(true);
  expect(evidence.pinnedGraph.computedConfigHash).toBe(run.configHash);
  const omitNullDefaults = (value: unknown): unknown => Array.isArray(value) ? value.map(omitNullDefaults) : value && typeof value === 'object'
    ? Object.fromEntries(Object.entries(value).filter(([, v]) => v != null).map(([k, v]) => [k, omitNullDefaults(v)])) : value;
  expect(evidence.pinnedGraph.config.nodes).toEqual(omitNullDefaults(pinned.config.nodes));
  expect(evidence.pinnedGraph.config.edges).toEqual(pinned.config.edges);
  expect(evidence.pinnedGraph.config.expressions['bonus-completed']).toMatchObject({ exprType: 'constant', kind: 'Boolean', value: 'true' });
  expect(evidence.progress.sampleCount).toBe(result.sampleCount);
});

test('Fast completion renders chart and distribution and restores them after navigation', async ({ page }) => {
  test.setTimeout(180000);
  await page.goto('/build');
  await page.getByRole('button', { name: 'Load coin example' }).click();
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByLabel('Simulation spins').fill('10000');
  const accepted = waitForRunLaunch(page);
  await page.getByRole('button', { name: /start run/i }).click();
  await accepted;
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
  await expect(page.getByTestId('sample-count')).toHaveText('10,000');
  await expect(page.getByRole('img', { name: /RTP convergence chart/ })).toHaveAttribute('aria-label', /[1-9]\d* observations/);
  await expect(page.getByText('Exact expectation', { exact: true }).locator('..')).toContainText('75.000%');
  await page.getByRole('tab', { name: 'Build', exact: true }).click();
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await expect(page.getByTestId('sample-count')).toHaveText('10,000');
  await page.reload();
  await expect(page.getByTestId('sample-count')).toHaveText('10,000');
  await expect(page.locator('.run-status')).toHaveText('completed');
});

test('Evidence export recovers a quota rejection and withholds files on errors, wrong identity and cancelled reads', async ({ page }) => {
  test.setTimeout(180000);
  await page.goto('/build'); await page.getByRole('button', { name: 'Load coin example' }).click();
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click(); await page.getByLabel('Simulation spins').fill('100');
  const created = waitForRunLaunch(page);
  await page.getByRole('button', { name: /start run/i }).click(); const run = await (await created).json();
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
  const stored = await (await getWithQuota(page.request, `/api/runs/${run.id}/evidence`)).json();
  const downloads: string[] = []; page.on('download', file => downloads.push(file.suggestedFilename()));
  let mode = '503', calls = 0;
  await page.route(`**/api/runs/${run.id}/evidence`, async route => {
    calls++;
    if (mode === '503') return route.fulfill({ status: 503, contentType: 'application/json', body: '{"error":"Archive temporarily unavailable."}' });
    if (mode === 'wait' || mode === 'quota' && calls === 1) return route.fulfill({ status: 429, headers: { 'Retry-After': mode === 'wait' ? '60' : '1' }, body: '{}' });
    const value = structuredClone(stored);
    if (mode === 'wrong') value.run.id = value.run.progress.runId = 'a-different-run';
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(value) });
  });
  await page.getByRole('button', { name: 'Export evidence' }).click();
  await expect(page.getByRole('alert')).toContainText('Archive temporarily unavailable'); expect(downloads).toHaveLength(0); expect(calls).toBe(1);
  mode = 'wrong'; calls = 0; await page.getByRole('button', { name: 'Retry export', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('different run'); expect(downloads).toHaveLength(0);
  mode = 'wait'; calls = 0; await page.getByRole('button', { name: 'Retry export', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('retrying evidence in 60s');
  await expect(page.getByRole('button', { name: 'Preparing evidence…', exact: true })).toBeDisabled();
  await page.getByRole('button', { name: 'Cancel export', exact: true }).click(); expect(downloads).toHaveLength(0); expect(calls).toBe(1);
  mode = 'quota'; calls = 0; const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export evidence' }).click();
  const exported = JSON.parse(await readFile((await (await download).path())!, 'utf8'));
  expect(calls).toBe(2); expect(downloads).toHaveLength(1); expect(exported.schemaVersion).toBe('slotmath.simulation.v2');
  expect(exported.pinnedGraph.inputVerified).toBe(true); expect(exported.run.id).toBe(run.id);
  expect(exported.run.progress).toEqual(exported.progress); expect(exported.progress).toEqual(stored.run.progress);
  expect(exported.pinnedGraph.computedConfigHash).toBe(run.configHash);
  mode = 'wait'; calls = 0; await page.getByRole('button', { name: 'Export evidence' }).click();
  await expect(page.getByRole('status')).toContainText('retrying evidence in 60s');
  await page.getByRole('tab', { name: 'Build', exact: true }).click();
  expect(downloads).toHaveLength(1); expect(calls).toBe(1);
});

test('Mobile simulation dashboard fits viewport and exposes controls and charts', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/build?project=dog-house');
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await expect(page.getByRole('button', { name: /start run/i })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'RTP convergence' })).toBeVisible();
  await expect(page.getByText('Target RTP', { exact: true }).locator('..')).toContainText('98.000%');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: '../docs/verification/simulation-mobile.png', fullPage: true });
  await page.getByRole('heading', { name: 'Payout distribution' }).scrollIntoViewIfNeeded();
  await expect(page.getByRole('heading', { name: 'Payout distribution' })).toBeInViewport();
  await expect.poll(() => page.getByRole('img', { name: /RTP convergence chart/ }).evaluate((svg: SVGSVGElement) => svg.getScreenCTM()!.a * parseFloat(getComputedStyle(svg.querySelector('.plot-label')!).fontSize))).toBeGreaterThanOrEqual(9.5);
  await page.getByRole('heading', { name: 'Reproducibility' }).scrollIntoViewIfNeeded();
  await expect(page.getByRole('heading', { name: 'Reproducibility' })).toBeInViewport();
});


test('Blocked socket falls back to authoritative snapshots and reconnects without losing the run', async ({ page }) => {
  test.setTimeout(180000);
  await page.route('**/hubs/runs/negotiate**', route => route.abort());
  await page.goto('/build?project=dog-house');
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByLabel('Simulation spins').fill('10000000');
  await page.getByRole('button', { name: /start run/i }).click();
  await expect(page.getByTestId('stream-status')).toContainText('recovering', { timeout: 70000 });
  await expect(page.getByTestId('sample-count')).not.toHaveText('0', { timeout: 70000 });
  const first = Number((await page.getByTestId('sample-count').innerText()).replaceAll(',', ''));
  await page.unroute('**/hubs/runs/negotiate**');
  // Negotiation and HTTP recovery honor the shared server Retry-After window.
  await expect(page.getByTestId('stream-status')).toContainText('WebSocket live', { timeout: 70000 });
  await expect.poll(async () => Number((await page.getByTestId('sample-count').innerText()).replaceAll(',', ''))).toBeGreaterThan(first);
  await page.getByRole('button', { name: /cancel run/i }).click();
  await expect(page.locator('.run-status')).toHaveText('cancelled');
});


test('One observed round does not claim a zero-width confidence interval or known sample variance', async ({ page }) => {
  test.setTimeout(120000);
  // Production quotas remain active in CI. Wait on safe GETs; never retry an
  // accepted run POST or parse an empty 429 response as run evidence.
  async function readWithQuota(path: string) {
    for (let attempt = 0; attempt < 3; attempt++) {
      const response = await page.request.get(path);
      if (response.status() !== 429) { expect(response.ok()).toBe(true); return response; }
      const seconds = Number(response.headers()['retry-after'] ?? '1');
      await new Promise(resolve => setTimeout(resolve, Math.min(60, Math.max(1, seconds)) * 1000));
    }
    throw new Error('API quota did not recover.');
  }
  await readWithQuota('/api/auth/status');
  await page.goto('/build');
  await page.getByRole('button', { name: 'Load coin example' }).click();
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByLabel('Simulation spins').fill('1');
  await page.getByRole('button', { name: /start run/i }).click();
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
  await expect(page.getByTestId('sample-count')).toHaveText('1');
  const stored = JSON.parse(await page.evaluate(() => localStorage.getItem('slotmath-simulation-v2')!));
  const run = await (await readWithQuota(`/api/runs/${stored.run.id}`)).json();
  expect(JSON.parse(run.resultJson).ci95).toBeNull();
  expect(JSON.parse(run.resultJson).volatility).toBeNull();
  await expect(page.getByText('95% CI half-width', { exact: true }).locator('..')).toContainText('Requires at least 2 rounds');
  await expect(page.getByText('Payout volatility', { exact: true }).locator('..').locator('strong')).toHaveText('—');
  await expect(page.getByText('95% confidence interval', { exact: true }).locator('..').locator('dd')).toHaveText('—');
});
