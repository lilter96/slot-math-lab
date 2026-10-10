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
    if (response.status() === 429) {
      const seconds = Number(response.headers()['retry-after'] ?? '1');
      await new Promise(resolve => setTimeout(resolve, Number.isFinite(seconds) ? Math.min(65000, Math.max(1000, seconds * 1000)) : 1000)); continue;
    }
    if (!response.ok()) throw new Error(`Authoritative read HTTP ${response.status()}`);
    return response.json();
  }
  throw new Error('Read quota did not recover.');
}
async function assertTable(container) {
  await expect(container.getByRole('region', { name: 'Interrupted feature lifecycle', exact: true }).getByRole('row').filter({ hasText: 'failed' }).getByRole('cell'))
    .toHaveText(['failed', '1', '1', '0', '1', 'complete']);
}
try {
  await page.goto(base + '/simulate'); await page.getByLabel('Username').fill(process.env.MEASUREMENTS_USER ?? 'operator');
  await page.getByLabel('Password').fill((await readFile(process.env.MEASUREMENTS_PASSWORD_FILE ?? root + '/production-secrets/operator-password.txt', 'utf8')).trim());
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle' }), page.getByRole('button', { name: 'Sign in', exact: true }).click()]);
  await page.goto(base + '/build'); await page.getByLabel('Import project file').setInputFiles(root + '/frontend/e2e/fixtures/interrupted-feature-model.json');
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click(); await page.getByRole('button', { name: '＋ Track metric', exact: true }).click();
  await page.getByLabel('Metric name').fill('Interrupted feature');
  await page.getByLabel('Metric observation level').selectOption('node'); await page.getByLabel('Metric graph node').selectOption('end');
  await page.getByLabel('Enable advanced measurement').check(); await page.getByLabel('Measurement source', { exact: true }).selectOption('count');
  await page.getByLabel('Measurement subject', { exact: true }).selectOption('episode');
  await page.getByLabel('Episode entry', { exact: true }).selectOption('fs'); await page.getByLabel('Episode exit', { exact: true }).selectOption('sink');
  await page.getByText('Group, pair and award accounting', { exact: true }).click(); await page.getByLabel('Group / cohort key', { exact: true }).fill('"sticky"');
  for (const name of ['Feature entries · unfinished rounds', 'Feature exits · unfinished rounds', 'Open features at interruption']) await page.getByLabel(name, { exact: true }).check();
  await saveMeasurement(page);
  for (const engine of ['auto', 'reference']) {
    await page.getByLabel('Simulation spins').fill('100'); await page.locator('#execution-configuration').evaluate(element => { element.open = true; });
    await page.getByLabel('Sampling engine', { exact: true }).selectOption(engine);
    const launch = waitForRunLaunch(page); await page.getByRole('button', { name: /^▶ Start (new )?run$/ }).click();
    const { id } = await (await launch).json(); owned.push(id);
    await expect(page.locator('.run-status')).toHaveText('failed', { timeout: 90000 });
    const card = page.getByRole('article', { name: 'Tracked metric Interrupted feature', exact: true });
    await expect(card.locator('[data-statistic=mean]')).toHaveText('—');
    await expect(card.locator('[data-statistic=interruptedFeatureEntries]')).toHaveText('1');
    await expect(card.locator('[data-statistic=interruptedFeatureExits]')).toHaveText('0');
    await expect(card.locator('[data-statistic=interruptedOpenEpisodes]')).toHaveText('1');
    await card.getByRole('tab', { name: 'accounting', exact: true }).click(); await assertTable(card);
    const evidence = await get(`/api/runs/${id}/evidence`), metric = evidence.run.progress.measurements[0];
    expect(evidence.inputVerified).toBe(true); expect(evidence.run.runtimeProvenance.numericalMethods).toContain('interrupted-feature-lifecycle-v1');
    expect(metric).toMatchObject({ observations: 0, count: 0, errors: 0, mean: null, analysis: { entries: 0, exits: 0, normalization: null,
      interruptedLifecycle: { failed: { interruptedRounds: 1, entries: 1, exits: 0, openInstances: 1, complete: true } } } });
    expect(metric.analysis.groups.sticky.interruptedLifecycle).toEqual(metric.analysis.interruptedLifecycle);
    expect(evidence.run.progress.execution).toMatchObject({ attemptedRounds: 1, completedRounds: 0, failedRounds: 1 });
    if (records.length) expect(metric).toEqual(records[0].measurement);
    await expect(page.getByRole('button', { name: '↓ Export evidence', exact: true })).toBeEnabled();
    const download = page.waitForEvent('download', { timeout: 90000 }); await page.getByRole('button', { name: '↓ Export evidence', exact: true }).click();
    const exported = JSON.parse(await readFile(await (await download).path(), 'utf8'));
    expect(exported.progress.measurements[0]).toEqual(metric); expect(exported.pinnedGraph.inputVerified).toBe(true);
    expect(exported.integrity.complete).toBe(false); expect(exported.progress.sampleCount).toBe(0);
    await page.reload(); await expect(card.locator('[data-statistic=interruptedOpenEpisodes]')).toHaveText('1');
    await page.goto(base + `/results?run=${id}`);
    const saved = page.getByRole('region', { name: 'Saved measurement Interrupted feature', exact: true }); await expect(saved).toBeVisible({ timeout: 90000 });
    await saved.getByRole('tab', { name: 'accounting', exact: true }).click(); await saved.getByLabel('Analysis population').selectOption('sticky');
    await assertTable(saved); await expect(saved).toContainText('0 subjects · average —');
    await page.setViewportSize({ width: 390, height: 844 });
    await expect(saved.getByRole('region', { name: 'Interrupted feature lifecycle', exact: true })).toBeVisible();
    expect(await saved.locator('.results-table-scroll').last().evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
    await page.setViewportSize({ width: 1560, height: 1100 });
    records.push({ engine, runId: id, inputVerified: evidence.inputVerified, configHash: evidence.run.configHash,
      measurementHash: evidence.run.measurementHash, producer: evidence.run.runtimeProvenance, measurement: metric,
      exportVerified: true, liveReloadSavedCohortAgree: true, mobileTableScrollable: true });
    await page.goto(base + `/simulate?run=${id}`);
  }
  expect(errors).toEqual([]);
  const asset = await page.locator('script[type=module][src]').first().getAttribute('src');
  const bytes = await (await context.request.get(base + asset)).body();
  const report = { checkedAt: new Date().toISOString(), endpoint: base,
    specification: 'One feature entry precedes division by zero, with no reveal, exit or settled paid round. One failed paid round and one open feature instance are retained only as lifecycle evidence; all numeric statistics remain undefined.',
    frontendAsset: asset, frontendSha256: createHash('sha256').update(bytes).digest('hex'), runs: records, browserErrors: errors };
  await writeFile(process.env.MEASUREMENTS_REPORT_FILE ?? root + '/docs/verification/interrupted-lifecycle-production.json', JSON.stringify(report, null, 2) + '\n');
  console.log(JSON.stringify({ runId: records[0].runId, enginesAgree: true, browserErrors: errors, resultsUrl: base + '/results?run=' + records[0].runId }));
} finally {
  await page.goto('about:blank').catch(() => {});
  for (const id of owned) {
    const run = await get(`/api/runs/${id}`).catch(() => null);
    if (run && !['completed', 'cancelled', 'failed'].includes(run.status)) await context.request.delete(base + `/api/runs/${id}`);
  }
  await browser.close();
}
