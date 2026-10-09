import { chromium } from '../../frontend/node_modules/playwright/index.mjs';
import { expect } from '../../frontend/node_modules/@playwright/test/index.mjs';
import { readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
const root = fileURLToPath(new URL('../..', import.meta.url));
const base = process.env.RESULTS_BASE_URL;
if (!base) throw new Error('Set RESULTS_BASE_URL to the production deployment to verify.');
const browser = await chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1560, height: 1100 } });
const page = await context.newPage(), errors = [], owned = [], report = { checkedAt: new Date().toISOString(), endpoint: base };
page.on('pageerror', err => errors.push(err.message));
async function capture(name) {
  const style = await page.addStyleTag({ content: 'body{height:auto!important;overflow:auto!important}#root,.app{height:auto!important;min-height:100vh}.results-workspace{overflow:visible!important}' });
  await page.screenshot({ path: root + '/docs/verification/' + name, fullPage: true }); await style.evaluate(node => node.remove());
}
try {
  report.anonymousArchiveBlocked = (await context.request.get(base + '/api/runs')).status() === 401;
  if (!report.anonymousArchiveBlocked) throw new Error('Production archive permits anonymous access.');
  await page.goto(base + '/results');
  await page.getByLabel('Username').fill(process.env.RESULTS_USER ?? 'operator');
  await page.getByLabel('Password').fill((await readFile(process.env.RESULTS_PASSWORD_FILE ?? root + '/production-secrets/operator-password.txt', 'utf8')).trim());
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle' }), page.getByRole('button', { name: 'Sign in', exact: true }).click()]);
  // Select retained, previously verified production evidence when available.
  const previous = JSON.parse(await readFile(root + '/docs/verification/realtime-production.json', 'utf8'));
  const id = process.env.RESULTS_RUN_ID ?? previous.completeRun.runId;
  const source = await (await context.request.get(base + `/api/runs/${id}/evidence`)).json();
  if (!source.inputVerified || source.run.status !== 'completed') throw new Error('A completed pinned production run is required.');
  await page.goto(base + `/results?run=${id}`);
  await expect(page.getByRole('heading', { name: source.model.name, exact: true })).toBeVisible();
  await expect(page.getByTestId('results-rtp')).toHaveText('99.672%');
  await page.getByRole('button', { name: 'Calculate reference', exact: true }).click();
  await expect(page.locator('.results-reference-value')).toHaveText('98.000%');
  report.referenceKind = await page.locator('.results-reference .results-source-tag').innerText();
  if (report.referenceKind !== 'ExactExpectation') throw new Error('Incorrect provenance for the constructor expectation.');
  report.authoredTarget = source.model.targetRtp;
  report.observedRtp = source.run.progress.runningRtp;
  report.pinnedVersion = source.run.configVersion; report.configHash = source.run.configHash; report.modelHash = source.model.modelHash;
  await capture('results-production-overview.png');
  await page.getByRole('tab', { name: 'Distribution', exact: true }).click();
  await page.getByRole('button', { name: 'Cumulative', exact: true }).click();
  await expect(page.getByRole('img', { name: /Cumulative payout probability/ })).toBeVisible();
  await page.getByText(/Inspect \d+ nonempty bins/).click(); await capture('results-production-distribution.png');
  report.histogramCount = source.run.progress.histogram.reduce((sum, bin) => sum + bin.count, 0);
  if (report.histogramCount !== source.run.progress.sampleCount) throw new Error('Histogram count disagrees with observations.');
  await page.getByRole('tab', { name: 'Reproducibility', exact: true }).click();
  const pending = page.waitForEvent('download'); await page.getByRole('button', { name: 'Download evidence JSON', exact: true }).click();
  const bundle = JSON.parse(await readFile(await (await pending).path(), 'utf8'));
  if (bundle.evidence.run.id !== id || bundle.evidence.run.configHash !== source.run.configHash || bundle.reference.rtp !== .98 || !bundle.reference.proof) throw new Error('Export does not preserve the pinned graph and reference.');
  report.reference = { rtp: bundle.reference.rtp, rational: bundle.reference.rational, components: bundle.reference.components };
  report.evidenceExport = { schema: bundle.schemaVersion, inputVerified: bundle.evidence.inputVerified, pinnedNodes: bundle.evidence.pinnedConfig.nodes.length };
  await page.getByRole('button', { name: 'Replay pinned run', exact: true }).click();
  const workers = source.run.degreeOfParallelism === 1 ? 2 : 1;
  await page.getByLabel('Pinned run workers').selectOption(String(workers));
  const creation = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/api/runs'));
  await page.getByRole('button', { name: 'Start pinned run', exact: true }).click(); const replay = await (await creation).json(); owned.push(replay.id);
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 30000 });
  await page.goto(base + `/results?run=${replay.id}&view=compare&compare=${id}`);
  await expect(page.getByTestId('results-comparison-verdict')).toHaveText('Replay matches'); await capture('results-production-comparison.png');
  report.replay = { runId: replay.id, originalRunId: id, originalWorkers: source.run.degreeOfParallelism, workers,
    sameHash: replay.configHash === source.run.configHash, pinnedVersion: replay.configVersion, verdict: 'Replay matches' };
  await page.evaluate(() => localStorage.clear()); await page.reload();
  await expect(page.getByTestId('results-comparison-verdict')).toHaveText('Replay matches'); report.emptyBrowserStorage = true;
  await page.setViewportSize({ width: 390, height: 844 });
  for (const section of ['Overview', 'Distribution', 'Compare', 'Reproducibility']) {
    await page.getByRole('tab', { name: section, exact: true }).click();
    if (!await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)) throw new Error('Mobile horizontal overflow in ' + section);
  }
  await page.getByRole('tab', { name: 'Overview', exact: true }).click(); await capture('results-production-mobile.png');
  report.mobileSectionsVerified = 4; report.browserErrors = errors;
  if (errors.length) throw new Error(errors.join(', '));
  report.resultsUrl = base + `/results?run=${id}`; report.comparisonUrl = base + `/results?run=${replay.id}&view=compare&compare=${id}`;
  await writeFile(root + '/docs/verification/results-production.json', JSON.stringify(report, null, 2) + '\n');
  console.log(JSON.stringify(report));
} finally {
  for (const id of owned) await context.request.delete(base + '/api/runs/' + id).catch(() => {});
  await browser.close();
}
