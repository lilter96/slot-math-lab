import { test, expect } from '@playwright/test';
import { readFile } from 'node:fs/promises';

test('Real WebSocket updates inside first chunk, reload restores run, cancellation retains actual payouts', async ({ page }) => {
  test.setTimeout(60000);
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
  // Keep the job alive through reload/cancellation even with the optimized
  // graph engine. This test intentionally stops after a small partial prefix.
  await page.getByLabel('Simulation spins').fill('10000000');
  const creation = page.waitForResponse(r => r.url().endsWith('/api/runs') && r.request().method() === 'POST');
  await page.getByRole('button', { name: /start run/i }).click();
  const run = await (await creation).json();
  expect(run.configHash).toMatch(/^[a-f0-9]{64}$/);
  const pinned = await (await page.request.get(`/api/configs/${run.configId}`)).json();
  expect(pinned.config.nodes).toHaveLength(11);
  await expect(page.getByTestId('stream-status')).toContainText('WebSocket live');
  await expect.poll(() => messages.some(m => m.status === 'running' && m.sampleCount > 0 && m.sampleCount < 65536), { timeout: 15000 }).toBe(true);
  await expect(page.getByTestId('live-rtp')).not.toHaveText('—');
  await expect(page.getByTestId('live-hit-frequency')).not.toHaveText('—');
  await expect(page.getByRole('img', { name: /RTP convergence chart/ })).toHaveAttribute('aria-label', /[1-9]\d* observations/);
  for (const m of messages.filter(m => m.sampleCount > 0)) expect(m.histogram.reduce((sum, b) => sum + b.count, 0)).toBe(m.sampleCount);
  await page.screenshot({ path: '../docs/verification/simulation-live.png', fullPage: true });
  await page.reload();
  await expect(page.getByTestId('stream-status')).toContainText('WebSocket live');
  await expect(page.getByTestId('sample-count')).not.toHaveText('0');
  expect(sockets.filter(url => url.includes('/hubs/runs')).length).toBeGreaterThanOrEqual(2);
  await page.getByRole('button', { name: /cancel run/i }).click();
  await expect(page.locator('.run-status')).toHaveText('cancelled', { timeout: 15000 });
  const stored = await (await page.request.get(`/api/runs/${run.id}`)).json();
  const result = JSON.parse(stored.resultJson);
  expect(result.sampleCount).toBeGreaterThan(0);
  expect(result.sampleCount).toBeLessThan(10000000);
  expect(result.sampleCount).toBe(stored.progress.sampleCount);
  expect(stored.progress.histogram.reduce((sum: number, b: { count: number }) => sum + b.count, 0)).toBe(result.sampleCount);
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export evidence' }).click();
  const evidence = JSON.parse(await readFile((await (await download).path())!, 'utf8'));
  expect(evidence.run.configHash).toBe(run.configHash);
  expect(evidence.pinnedGraph.config.nodes).toHaveLength(11);
  expect(evidence.progress.sampleCount).toBe(result.sampleCount);
});

test('Fast completion renders chart and distribution and restores them after navigation', async ({ page }) => {
  await page.goto('/build');
  await page.getByRole('button', { name: 'Load coin example' }).click();
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByLabel('Simulation spins').fill('10000');
  await page.getByRole('button', { name: /start run/i }).click();
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 20000 });
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
  await page.getByRole('heading', { name: 'Reproducibility' }).scrollIntoViewIfNeeded();
  await expect(page.getByRole('heading', { name: 'Reproducibility' })).toBeInViewport();
});


test('Blocked socket falls back to authoritative snapshots and reconnects without losing the run', async ({ page }) => {
  test.setTimeout(30000);
  await page.route('**/hubs/runs/negotiate**', route => route.abort());
  await page.goto('/build?project=dog-house');
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByLabel('Simulation spins').fill('10000000');
  await page.getByRole('button', { name: /start run/i }).click();
  await expect(page.getByTestId('stream-status')).toContainText('recovering');
  await expect(page.getByTestId('sample-count')).not.toHaveText('0', { timeout: 10000 });
  const first = Number((await page.getByTestId('sample-count').innerText()).replaceAll(',', ''));
  await page.unroute('**/hubs/runs/negotiate**');
  await expect(page.getByTestId('stream-status')).toContainText('WebSocket live', { timeout: 10000 });
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
