import { saveMeasurement, waitForRunLaunch } from './requests.mjs';
import { chromium } from '../../frontend/node_modules/playwright/index.mjs';
import { expect } from '../../frontend/node_modules/@playwright/test/index.mjs';
import { readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';

const root = fileURLToPath(new URL('../..', import.meta.url)), base = process.env.MEASUREMENTS_BASE_URL;
if (!base) throw new Error('Set MEASUREMENTS_BASE_URL to the authenticated deployment.');
const browser = await chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1560, height: 1100 } });
const page = await context.newPage(), errors = [], owned = [], records = [];
page.on('pageerror', error => errors.push(error.message));
async function get(path) {
  for (let attempt = 0; attempt < 3; attempt++) {
    const response = await context.request.get(base + path);
    if (response.status() === 429) { await new Promise(resolve => setTimeout(resolve, Math.max(1000, Number(response.headers()['retry-after'] ?? '1') * 1000))); continue; }
    if (!response.ok()) throw new Error(`Authoritative read HTTP ${response.status()}`);
    return response.json();
  }
  throw new Error('Read quota did not recover.');
}
async function metric(name, expression, assertion = false) {
  await page.getByRole('button', { name: '＋ Track metric', exact: true }).click();
  await page.getByLabel('Metric name').fill(name); await page.getByLabel('Metric observation level').selectOption('node');
  await page.getByLabel('Metric graph node').selectOption('sink'); await page.getByLabel('Metric numeric expression').fill(expression);
  await page.getByLabel('Metric unit').fill('');
  if (assertion) { await page.getByLabel('Enable advanced measurement').check(); await page.getByLabel('Assert exactly zero or false').check(); }
  await saveMeasurement(page);
}
try {
  await page.goto(base + '/simulate'); await page.getByLabel('Username').fill(process.env.MEASUREMENTS_USER ?? 'operator');
  await page.getByLabel('Password').fill((await readFile(process.env.MEASUREMENTS_PASSWORD_FILE ?? root + '/production-secrets/operator-password.txt', 'utf8')).trim());
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle' }), page.getByRole('button', { name: 'Sign in', exact: true }).click()]);
  await page.goto(base + '/build'); await page.getByLabel('Import project file').setInputFiles(root + '/frontend/e2e/fixtures/strict-expression-model.json');
  await expect(page.locator('.react-flow__node')).toHaveCount(2); await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await metric('Numeric index', 'index(state.values, 0)');
  await metric('Exact text roundtrip', 'tonumber(tostring(1 / 3)) - 1 / 3', true);
  await metric('Invalid text', 'tonumber(state.text)');
  await page.getByLabel('Simulation spins').fill('1000');
  for (const engine of ['auto', 'reference']) {
    await page.locator('#execution-configuration').evaluate(element => { element.open = true; });
    await page.getByLabel('Sampling engine', { exact: true }).selectOption(engine);
    const accepted = waitForRunLaunch(page); await page.getByRole('button', { name: /^▶ Start (new )?run$/ }).click();
    const { id } = await (await accepted).json(); owned.push(id);
    await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 90000 });
    const numeric = page.getByRole('article', { name: 'Tracked metric Numeric index', exact: true });
    await expect(numeric.locator('[data-statistic=mean]')).toHaveText('0.5');
    await expect(page.getByRole('article', { name: 'Tracked metric Exact text roundtrip', exact: true })).toContainText('no Observed Violations');
    const invalid = page.getByRole('article', { name: 'Tracked metric Invalid text', exact: true });
    await expect(invalid).toContainText('1,000 invalid'); await expect(invalid.locator('[data-statistic=mean]')).toHaveText('—');
    const evidence = await get(`/api/runs/${id}/evidence`), metrics = evidence.run.progress.measurements;
    expect(evidence.inputVerified).toBe(true); expect(metrics[0]).toMatchObject({ count: 1000, min: .5, max: .5, mean: .5, errors: 0 });
    expect(metrics[1].analysis.assertion).toMatchObject({ checked: 1000, violations: 0, status: 'noObservedViolations' });
    expect(metrics[2]).toMatchObject({ observations: 1000, count: 0, errors: 1000, mean: null, sum: null });
    if (records.length) expect(metrics).toEqual(records[0].measurements);
    const download = page.waitForEvent('download'); await page.getByRole('button', { name: '↓ Export evidence', exact: true }).click();
    const exported = JSON.parse(await readFile(await (await download).path(), 'utf8'));
    expect(exported.progress.measurements).toEqual(metrics); expect(exported.pinnedGraph.inputVerified).toBe(true);
    await page.goto(base + `/results?run=${id}`);
    await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('slotmath-simulation-v2') ?? '{}').run?.id),
      { message: 'The compact browser checkpoint owns the inspected run' }).toBe(id);
    let rejectedAt = 0;
    await page.route(`**/api/runs/${id}`, route => {
      if (route.request().method() !== 'GET') return route.continue();
      rejectedAt ||= Date.now();
      const remaining = 6000 - (Date.now() - rejectedAt);
      if (remaining > 0) return route.fulfill({ status: 429, headers: { 'Retry-After': String(Math.ceil(remaining / 1000)) }, body: '' });
      return route.continue();
    });
    const rejection = page.waitForResponse(response => new URL(response.url()).pathname === `/api/runs/${id}` && response.status() === 429, { timeout: 90000 });
    await page.reload(); await rejection;
    const row = page.getByRole('table').filter({ has: page.getByRole('columnheader', { name: 'Measurement / scope', exact: true }) }).getByRole('row').filter({ hasText: 'Numeric index' });
    await expect(row).toContainText('0.5', { timeout: 90000 });
    await expect(page.getByText('The terminal result has not been retrieved.', { exact: true })).toHaveCount(0);
    await page.unroute(`**/api/runs/${id}`);
    records.push({ engine, runId: id, inputVerified: evidence.inputVerified, configHash: evidence.run.configHash,
      measurementHash: evidence.run.measurementHash, producer: evidence.run.runtimeProvenance, measurements: metrics,
      exportVerified: true, archivedResultRetainedDuringRejectedSnapshot: true });
    await page.goto(base + `/simulate?run=${id}`);
  }
  expect(errors).toEqual([]);
  const asset = await page.locator('script[type=module][src]').first().getAttribute('src');
  const bytes = await (await context.request.get(base + asset)).body();
  const report = { checkedAt: new Date().toISOString(), endpoint: base, specification: 'Each settled round contains values=[1/2,1/4] and text=H. Numeric index yields 1/2; tonumber(tostring(1/3))-1/3 is exactly zero; explicit conversion of H yields one invalid observation and no numeric statistic.',
    faultScope: 'Browser-injected six-second snapshot quota rejection; accepted API runs, archives, exports and producer artifacts are real.',
    frontendAsset: asset, frontendSha256: createHash('sha256').update(bytes).digest('hex'), runs: records, browserErrors: errors };
  await writeFile(process.env.MEASUREMENTS_REPORT_FILE ?? root + '/docs/verification/strict-expressions-production.json', JSON.stringify(report, null, 2) + '\n');
  console.log(JSON.stringify({ runId: records[0].runId, enginesAgree: true, browserErrors: errors, simulateUrl: base + '/simulate?run=' + records[0].runId }));
} finally {
  await page.goto('about:blank').catch(() => {});
  for (const id of owned) {
    const run = await get(`/api/runs/${id}`).catch(() => null);
    if (run && !['completed', 'cancelled', 'failed'].includes(run.status)) await context.request.delete(base + `/api/runs/${id}`);
  }
  await browser.close();
}
