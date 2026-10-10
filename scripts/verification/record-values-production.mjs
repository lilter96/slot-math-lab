import { saveMeasurement, waitForRunLaunch, waitForDeployment } from './requests.mjs';
import { chromium } from '../../frontend/node_modules/playwright/index.mjs';
import { expect } from '../../frontend/node_modules/@playwright/test/index.mjs';
import { readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';

const root = fileURLToPath(new URL('../..', import.meta.url)), base = process.env.MEASUREMENTS_BASE_URL;
if (!base) throw new Error('Set MEASUREMENTS_BASE_URL to the authenticated deployment.');
const browser = await chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1560, height: 1100 } });
const page = await context.newPage(), errors = [], runs = [], frames = [];
page.on('pageerror', error => errors.push(error.message));
page.on('websocket', socket => socket.on('framereceived', event => {
  for (const item of event.payload.toString().split('\x1e')) {
    try { const message = JSON.parse(item); if (message.type === 1 && message.target === 'ProgressUpdate') frames.push(message.arguments[0]); } catch { /* Hub control frame. */ }
  }
}));
async function get(path) {
  for (let attempt = 0; attempt < 3; attempt++) {
    const response = await context.request.get(base + path);
    if (response.status() === 429) {
      const seconds = Number(response.headers()['retry-after'] ?? '1');
      await new Promise(resolve => setTimeout(resolve, Number.isFinite(seconds) ? Math.min(65000, Math.max(1000, seconds * 1000)) : 1000)); continue;
    }
    if (!response.ok()) throw new Error(`Authoritative read HTTP ${response.status()}`);
    return response.json();
  }
  throw new Error('Read quota did not recover.');
}
try {
  await waitForDeployment(context.request, base);
  await page.goto(base + '/simulate'); await page.getByLabel('Username').fill(process.env.MEASUREMENTS_USER ?? 'operator');
  await page.getByLabel('Password').fill((await readFile(process.env.MEASUREMENTS_PASSWORD_FILE ?? root + '/production-secrets/operator-password.txt', 'utf8')).trim());
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle' }), page.getByRole('button', { name: 'Sign in', exact: true }).click()]);
  await page.goto(base + '/build'); await page.getByLabel('Import project file').setInputFiles(root + '/frontend/e2e/fixtures/record-model.json');
  await expect(page.locator('.react-flow__node')).toHaveCount(5); await page.getByLabel('Focus graph node').selectOption('filter');
  await expect(page.getByLabel('Expression item type', { exact: true })).toHaveValue('Record');
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByRole('button', { name: '＋ Track metric', exact: true }).click();
  await page.getByLabel('Metric name').fill('Rejected suffix'); await page.getByLabel('Metric value source').selectOption('expression');
  await page.getByLabel('Metric numeric expression').fill('state.payout.suffix');
  const rejected = page.waitForResponse(response => response.url().endsWith('/api/runs/measurements/schema') && response.status() === 400, { timeout: 90000 });
  await page.getByRole('button', { name: 'Save measurement', exact: true }).click(); await rejected;
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('payout.suffix'); await page.getByLabel('Close measurement editor').click();
  for (const [name, expression] of [['Selected record award', 'state.selected.money.award'], ['Nested history', 'state.selected.history[0][0]']]) {
    await page.getByRole('button', { name: '＋ Track metric', exact: true }).click();
    await page.getByLabel('Metric name').fill(name); await page.getByLabel('Metric observation level').selectOption('node');
    await page.getByLabel('Metric graph node').selectOption('sink');
    if (name === 'Selected record award') {
      await page.getByLabel('Insert metric value field').selectOption({ label: 'selected.money.award' });
      await expect(page.getByLabel('Metric numeric expression')).toHaveValue('state["selected"]["money"]["award"]');
    } else await page.getByLabel('Metric numeric expression').fill(expression);
    await page.getByLabel('Metric unit').fill(''); await saveMeasurement(page);
  }
  for (const engine of ['auto', 'reference']) {
    await page.getByLabel('Simulation spins').fill('10000'); await page.locator('#execution-configuration').evaluate(element => { element.open = true; });
    await page.getByLabel('Sampling engine', { exact: true }).selectOption(engine);
    const launch = waitForRunLaunch(page); await page.getByRole('button', { name: /^▶ Start (new )?run$/ }).click();
    const { id } = await (await launch).json();
    await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 90000 });
    const card = page.getByRole('article', { name: 'Tracked metric Selected record award', exact: true });
    await expect(card.locator('[data-statistic=min]')).toHaveText('0.25'); await expect(card.locator('[data-statistic=max]')).toHaveText('1.75');
    const enumeration = page.waitForResponse(response => response.url().endsWith(`/api/runs/${id}/measurements/reference`) && response.request().method() === 'POST' && response.status() !== 429, { timeout: 90000 });
    await page.locator('.graph-measurement-reference > summary').click(); await page.getByRole('button', { name: 'Enumerate pinned measurements', exact: true }).click();
    const response = await enumeration; expect(response.status()).toBe(200); const reference = await response.json();
    expect(reference.report.roundMean).toEqual({ lower: '11/8', upper: '11/8' });
    expect(reference.report.measurements[0].support).toEqual([{ value: .25, massPerPaidRound: '1/4' }, { value: 1.75, massPerPaidRound: '3/4' }]);
    expect(reference.report.measurements[0].supportComplete).toBe(true); expect(reference.report.unresolvedRoundMass).toBe('0/1');
    await expect(page.getByLabel('Pinned graph enumeration result')).toContainText('11/8');
    const evidence = await get(`/api/runs/${id}/evidence`), measurements = evidence.run.progress.measurements;
    expect(evidence.inputVerified).toBe(true); expect(evidence.run.runtimeProvenance.numericalMethods).toContain('strict-state-shapes-v1');
    expect(measurements.map(m => [m.count, m.errors])).toEqual([[10000, 0], [10000, 0]]);
    expect(measurements[0].mean).toBe(evidence.run.progress.runningRtp);
    expect(measurements[1]).toMatchObject({ min: 1, max: 3 });
    if (runs.length) expect(measurements).toEqual(runs[0].measurements);
    const download = page.waitForEvent('download', { timeout: 90000 }); await page.getByRole('button', { name: '↓ Export evidence', exact: true }).click();
    const exported = JSON.parse(await readFile(await (await download).path(), 'utf8'));
    expect(exported.progress.measurements).toEqual(measurements); expect(exported.pinnedGraph.inputVerified).toBe(true);
    await page.reload(); await expect(card.locator('[data-statistic=min]')).toHaveText('0.25');
    await page.goto(base + `/results?run=${id}`);
    const saved = page.getByRole('region', { name: 'Saved measurement Selected record award', exact: true }); await expect(saved).toBeVisible({ timeout: 90000 });
    await expect(page.getByLabel('Retained diagnostic evidence')).toContainText('graph-enumeration');
    await page.setViewportSize({ width: 390, height: 844 }); await expect(saved).toBeVisible();
    expect(await page.locator('body').evaluate(element => element.scrollWidth <= window.innerWidth + 1)).toBe(true);
    await page.setViewportSize({ width: 1560, height: 1100 });
    runs.push({ engine, runId: id, inputVerified: evidence.inputVerified, producer: evidence.run.runtimeProvenance,
      configHash: evidence.run.configHash, measurementHash: evidence.run.measurementHash, measurements, reference,
      exportVerified: true, liveReloadSavedAgree: true, mobileFits: true });
    await page.goto(base + `/simulate?run=${id}`);
  }
  expect(errors).toEqual([]);
  const asset = await page.locator('script[type=module][src]').first().getAttribute('src'), bytes = await (await context.request.get(base + asset)).body();
  await writeFile(process.env.MEASUREMENTS_REPORT_FILE ?? root + '/docs/verification/record-values-production.json', JSON.stringify({ checkedAt: new Date().toISOString(), endpoint: base,
    specification: 'A 1:3 weighted choice selects one record through a typed filter and index. Awards 1/4 and 7/4 have probabilities 1/4 and 3/4; EV=11/8. Nested history retains values 1 or 3. An unused null remains null; a scalar suffix must fail compilation.',
    frontendAsset: asset, frontendSha256: createHash('sha256').update(bytes).digest('hex'), richWebSocketFrames: frames.filter(frame => frame.measurements?.length).length,
    runs, browserErrors: errors }, null, 2) + '\n');
  console.log(JSON.stringify({ runId: runs[0].runId, exactExpectation: '11/8', enginesAgree: true, browserErrors: errors, resultsUrl: base + '/results?run=' + runs[0].runId }));
} finally { await browser.close(); }
