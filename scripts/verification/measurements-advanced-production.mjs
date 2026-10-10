import { waitForRunLaunch } from './requests.mjs';
import { chromium } from '../../frontend/node_modules/playwright/index.mjs';
import { expect } from '../../frontend/node_modules/@playwright/test/index.mjs';
import { readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
const root = fileURLToPath(new URL('../..', import.meta.url));
const base = process.env.MEASUREMENTS_BASE_URL;
if (!base) throw new Error('Set MEASUREMENTS_BASE_URL to the authenticated deployment.');
const browser = await chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1560, height: 1100 } });
const page = await context.newPage(), errors = [], frames = [], owned = [];
const report = { checkedAt: new Date().toISOString(), endpoint: base };
page.on('pageerror', e => errors.push(e.message));
page.on('response', response => {
  if (new URL(response.url()).pathname === '/api/runs' && response.request().method() === 'POST' && response.status() !== 202)
    void response.text().then(body => console.error(`Run request rejected: HTTP ${response.status()} ${body.slice(0, 1200)}`)).catch(() => {});
});
page.on('websocket', s => s.on('framereceived', ({ payload }) => {
  for (const raw of String(payload).split('\x1e')) try { const f = JSON.parse(raw); if (f.target === 'ProgressUpdate') frames.push(f.arguments[0]); } catch { /* handshake */ }
}));
async function get(path) {
  for (let n = 0; n < 3; n++) {
    const response = await context.request.get(base + path);
    if (response.status() === 429) { await new Promise(resolve => setTimeout(resolve, Math.min(60000, Math.max(1000, Number(response.headers()['retry-after'] ?? '1') * 1000)))); continue; }
    if (!response.ok()) throw new Error(`${path}: HTTP ${response.status()}`);
    return response.json();
  }
  throw new Error('Read quota did not recover.');
}
async function metric(name, { node, expression, source, subject, group, filter, entry, exit, reduction, groupLimit, independent = false } = {}) {
  await page.getByRole('button', { name: '＋ Track metric', exact: true }).click(); await page.getByLabel('Metric name').fill(name);
  if (node) { await page.getByLabel('Metric observation level').selectOption('node'); await page.getByLabel('Metric graph node').selectOption(node); }
  await page.getByLabel('Enable advanced measurement').check();
  if (subject) await page.getByLabel('Measurement subject', { exact: true }).selectOption(subject);
  if (entry) await page.getByLabel('Episode entry', { exact: true }).selectOption(entry);
  if (exit) await page.getByLabel('Episode exit', { exact: true }).selectOption(exit);
  if (reduction) await page.getByLabel('Subject reduction', { exact: true }).selectOption(reduction);
  if (source) await page.getByLabel('Measurement source', { exact: true }).selectOption(source);
  if (expression) { await page.getByLabel('Metric value source').selectOption('expression'); await page.getByLabel('Metric numeric expression').fill(expression); }
  if (filter) { await page.getByLabel('Metric filter mode').selectOption('expression'); await page.getByLabel('Metric filter expression').fill(filter); }
  if (group) { await page.getByText('Group, pair and award accounting', { exact: true }).click(); await page.getByLabel('Group / cohort key', { exact: true }).fill(group); if (groupLimit) await page.getByLabel('Maximum groups', { exact: true }).fill(String(groupLimit)); }
  if (independent) { await page.getByText('Uncertainty, precision and reference checks', { exact: true }).click(); await page.getByLabel('Independent measurement subjects').check(); }
  await page.getByRole('button', { name: 'Save measurement', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
}
async function finished(id) {
  await expect.poll(async () => (await get('/api/runs/' + id)).status, { timeout: 120000, intervals: [1000, 2000] }).toBe('completed');
  return get('/api/runs/' + id);
}
async function screenshot(name) { await page.screenshot({ path: root + '/docs/verification/' + name, fullPage: true }); }
try {
  report.anonymousBlocked = (await context.request.post(base + '/api/runs/measurements/schema', { data: { config: {} } })).status() === 401;
  if (!report.anonymousBlocked) throw new Error('Measurement API is anonymously accessible.');
  await page.goto(base + '/simulate'); await page.getByLabel('Username').fill(process.env.MEASUREMENTS_USER ?? 'operator');
  await page.getByLabel('Password').fill((await readFile(process.env.MEASUREMENTS_PASSWORD_FILE ?? root + '/production-secrets/operator-password.txt', 'utf8')).trim());
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle' }), page.getByRole('button', { name: 'Sign in', exact: true }).click()]);
  await page.goto(base + '/build?project=dog-house'); await expect(page.locator('.react-flow__node')).toHaveCount(12);
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await metric('Complete round payout law', { independent: true });
  await metric('Sticky FS payout by bonus length', { node: 'free-spin/snapshot-winHistory', expression: 'state.spinCoins / 20', group: 'state.fsCount', groupLimit: 19 });
  await metric('Long bonus FS payout', { node: 'free-spin/snapshot-winHistory', expression: 'state.spinCoins / 20', filter: 'state.fsCount >= 18' });
  await metric('FS reveals per paid round', { node: 'free-spin/snapshot-winHistory', subject: 'round', source: 'count', independent: true });
  await metric('Bonus awarded reveals', { node: 'free-spins', expression: 'state.fsCount', independent: true });
  await metric('Bonus selection lifecycle', { node: 'bonus-count', subject: 'episode', source: 'count',
    entry: 'bonus-grid-loop', exit: 'free-spins', reduction: 'average', group: '"bonus selection"', groupLimit: 1, filter: 'false' });
  await metric('Sticky bonus episode payout', { node: 'free-spin/snapshot-winHistory', expression: 'state.spinCoins / 20',
    subject: 'episode', entry: 'free-spins', exit: 'bonus-completed', reduction: 'sum', group: 'state.fsCount', groupLimit: 19 });
  await page.getByLabel('Simulation spins').fill('100000'); await page.getByLabel('Simulation workers').selectOption('2');
  const launch = waitForRunLaunch(page);
  await page.getByRole('button', { name: /^▶ Start run$/ }).click(); const accepted = await launch;
  if (accepted.status() !== 202) throw new Error(`Run was not accepted: HTTP ${accepted.status()} ${await accepted.text()}`);
  const created = await accepted.json(); owned.push(created.id);
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 120000 }); const run = await finished(created.id), values = run.progress.measurements;
  if (values.length !== 7 || values.some(m => m.errors || m.observations !== m.count + m.excluded + m.errors)) throw new Error('Invalid advanced observation accounting.');
  if (values[0].count !== 100000 || values[3].count !== 100000 || values[1].count !== values[3].sum || values[1].count !== values[4].sum) throw new Error('Paid-round / reveal denominators disagree.');
  if (values.some(m => m.analysis.normalization?.paidRounds !== 100000 || m.analysis.normalization.externalTurnover !== 100000)
    || Object.values(values[1].analysis.groups).some(g => g.normalization?.paidRounds !== 100000 || g.normalization.externalTurnover !== 100000)) throw new Error('FS/cohort contributions lost their complete external-turnover denominator.');
  if (!values[1].analysis.groupsComplete || Object.keys(values[1].analysis.groups).length < 2 || values[1].analysis.meanInterval !== null) throw new Error('Grouped FS evidence is incomplete or claims independent reveals.');
  const lifecycle = values[5].analysis.groups['bonus selection'];
  if (values[5].count !== 0 || values[5].mean !== null || !lifecycle || lifecycle.entries !== values[4].count
    || lifecycle.exits !== lifecycle.entries || lifecycle.unclosedEpisodes !== 0 || lifecycle.count !== 0
    || lifecycle.normalization?.paidRounds !== 100000 || lifecycle.tails.some(t => t.probability !== null || t.secondMoment !== null)) throw new Error('Empty bonus cohort lost lifecycle exposure or invented a numeric value.');
  const episode = values[6], parents = episode.analysis.parentExposure;
  if (episode.count !== values[4].count || episode.analysis.entries !== episode.count || episode.analysis.exits !== episode.count || episode.errors !== 0
    || !parents || parents.paidRoundsWithMatchingChildren !== episode.count || parents.episodesWithMatchingChildren !== episode.count
    || Math.abs(episode.sum - values[1].sum) > 1e-6 || episode.analysis.normalization.paidRounds !== 100000 || !episode.analysis.groupsComplete
    || Object.values(episode.analysis.groups).reduce((count, g) => count + g.parentExposure.episodesWithMatchingChildren, 0) !== episode.count
    || Object.entries(episode.analysis.groups).some(([key, g]) => g.normalization.paidRounds !== 100000 || Math.abs(g.sum - values[1].analysis.groups[key].sum) > 1e-6)) throw new Error('Whole-bonus episode payout or distinct parent exposure disagrees with its reveal ledger.');
  if (!frames.some(f => f.sampleCount > 0 && f.measurements?.some(m => m.analysis))) throw new Error('No rich production WebSocket frame.');
  const fsChart = page.getByRole('article', { name: 'Tracked metric Sticky FS payout by bonus length', exact: true });
  await fsChart.getByLabel('Chart statistic for Sticky FS payout by bonus length', { exact: true }).selectOption('sampleVariance');
  await expect(fsChart.locator('.plot-readout strong')).toContainText('Sample variance');
  const bonusChart = page.getByRole('article', { name: 'Tracked metric Sticky bonus episode payout', exact: true });
  await bonusChart.getByLabel('Chart statistic for Sticky bonus episode payout', { exact: true }).selectOption('matchingEpisodes');
  await expect(bonusChart.locator('.plot-readout strong')).toHaveText(`Feature episodes with matching children ${episode.count.toLocaleString('en-US')}`);
  await page.reload(); await expect(page.getByTestId('sample-count')).toHaveText('100,000');
  await expect(fsChart.getByLabel('Chart statistic for Sticky FS payout by bonus length', { exact: true })).toHaveValue('sampleVariance');
  await expect(bonusChart.getByLabel('Chart statistic for Sticky bonus episode payout', { exact: true })).toHaveValue('matchingEpisodes');
  await expect(bonusChart.locator('.plot-readout strong')).toHaveText(`Feature episodes with matching children ${episode.count.toLocaleString('en-US')}`);
  const checkpoint = JSON.parse(await page.evaluate(() => localStorage.getItem('slotmath-simulation-v2')));
  if (checkpoint.points.at(-1).measurements[1].statistics.sampleVariance !== values[1].analysis.moments.sampleVariance
    || checkpoint.points.at(-1).measurements[6].statistics.matchingEpisodes !== episode.count
    || checkpoint.points.some(p => p.measurements?.some(m => m.analysis))) throw new Error('Reload lost compact statistic trends or retained rich distributions per point.');
  report.scalarChartHistoryRetained = true;
  const law = page.getByRole('article', { name: 'Tracked metric Complete round payout law', exact: true });
  await law.locator('.measurement-witnesses > summary').click(); await law.getByRole('button', { name: /Replay first witness/ }).click();
  await expect(law.getByLabel('Reconstructed witness')).toContainText('logical stream prefix', { timeout: 90000 });
  await page.goto(base + `/results?run=${run.id}`); await expect(page.getByLabel('Retained diagnostic evidence')).toContainText('witness-replay');
  await page.getByRole('button', { name: 'Calculate reference', exact: true }).click(); await expect(page.locator('.results-reference-value')).toHaveText('98.000%', { timeout: 90000 });
  await page.goto(base + `/simulate?run=${run.id}`);
  await page.locator('#reference-workbench > summary').click(); await page.locator('#exact-law-comparison > summary').click();
  await page.getByRole('button', { name: 'Load equal-98%-mean examples', exact: true }).click();
  await page.getByRole('button', { name: 'Compare exact laws', exact: true }).click();
  const comparison = page.getByLabel('Exact law comparison result');
  await expect(comparison).toContainText('Left / right mean 49/50 / 49/50', { timeout: 90000 });
  await expect(comparison).toContainText('Exact total variation 1/2');
  const retained = await get(`/api/runs/${run.id}/evidence`);
  if (!retained.diagnostics.some(d => d.kind === 'independent-law-comparison' && /^[a-f0-9]{64}$/.test(d.inputSha256) && /^[a-f0-9]{64}$/.test(d.outputSha256))) throw new Error('Authenticated rational law comparison was not retained with its evidence identity.');
  await page.goto(base + `/results?run=${run.id}`);
  await page.reload();
  await expect(page.getByLabel('Retained diagnostic evidence')).toContainText('independent-law-comparison');
  await screenshot('advanced-measurements-production-desktop.png');
  const lifecycleCard = page.getByRole('region', { name: 'Saved measurement Bonus selection lifecycle', exact: true });
  await expect(lifecycleCard.locator('.measurement-quantiles')).toHaveCSS('display', 'grid');
  await lifecycleCard.getByRole('tab', { name: 'accounting', exact: true }).click();
  await lifecycleCard.getByLabel('Analysis population').selectOption('bonus selection');
  await expect(lifecycleCard.getByRole('row').filter({ hasText: 'Feature entries / exits / unclosed' })).toContainText(`${lifecycle.entries} / ${lifecycle.entries} / 0`);
  await lifecycleCard.scrollIntoViewIfNeeded(); await screenshot('advanced-cohort-lifecycle-production.png');
  await page.setViewportSize({ width: 390, height: 844 });
  await lifecycleCard.scrollIntoViewIfNeeded();
  if (!await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)) throw new Error('Retained cohort evidence overflows mobile viewport.');
  await screenshot('advanced-cohort-lifecycle-production-mobile.png');
  await page.setViewportSize({ width: 1560, height: 1100 });
  const episodeCard = page.getByRole('region', { name: 'Saved measurement Sticky bonus episode payout', exact: true });
  await episodeCard.getByRole('tab', { name: 'accounting', exact: true }).click();
  await expect(episodeCard.getByRole('row').filter({ hasText: 'Owning feature episodes with matching children' })).toContainText(String(episode.count));
  await episodeCard.scrollIntoViewIfNeeded(); await screenshot('advanced-whole-bonus-production.png');
  await page.getByRole('button', { name: 'Replay pinned run', exact: true }).click(); await page.getByLabel('Pinned run workers').selectOption('1');
  const replayLaunch = waitForRunLaunch(page);
  await page.getByRole('button', { name: 'Start pinned run', exact: true }).click(); const replay = await (await replayLaunch).json(); owned.push(replay.id); const replayed = await finished(replay.id);
  if (JSON.stringify(replayed.progress.measurements) !== JSON.stringify(values) || JSON.stringify(replayed.progress.execution.loopTerminations) !== JSON.stringify(run.progress.execution.loopTerminations)) throw new Error('Rich measurements or loop evidence changed with worker count.');
  await page.goto(base + `/results?run=${replay.id}&view=compare&compare=${run.id}`); await expect(page.getByTestId('results-comparison-verdict')).toHaveText('Replay matches');
  await page.goto(base + `/results?run=${run.id}`); await page.getByRole('button', { name: 'Replay pinned run', exact: true }).click();
  await page.getByLabel('Pinned run workers').selectOption('1'); await page.getByLabel('Pinned run engine').selectOption('reference');
  const referenceLaunch = waitForRunLaunch(page);
  await page.getByRole('button', { name: 'Start pinned run', exact: true }).click(); const reference = await (await referenceLaunch).json(); owned.push(reference.id);
  const interpreted = await finished(reference.id);
  if (interpreted.progress.runningRtp !== run.progress.runningRtp || JSON.stringify(interpreted.progress.measurements) !== JSON.stringify(values)
    || JSON.stringify(interpreted.progress.execution.loopTerminations) !== JSON.stringify(run.progress.execution.loopTerminations)) throw new Error('Canonical reference interpreter disagrees with the compiled graph measurement evidence.');
  await page.goto(base + `/results?run=${reference.id}&view=compare&compare=${run.id}`); await expect(page.getByTestId('results-comparison-verdict')).toHaveText('Replay matches');
  await page.goto(base + `/simulate?run=${run.id}`); await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByRole('link', { name: 'Permalink to this run ↗', exact: true })).toHaveAttribute('href', `/simulate?run=${run.id}`);
  await expect(page.getByTestId('sample-count')).toHaveText('100,000');
  await expect(page.getByRole('article', { name: 'Tracked metric Sticky FS payout by bonus length', exact: true })).toBeVisible();
  if (!await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)) throw new Error('Advanced dashboard overflows mobile viewport.');
  await screenshot('advanced-measurements-production-mobile.png');
  await page.getByRole('article', { name: 'Tracked metric Sticky FS payout by bonus length', exact: true }).scrollIntoViewIfNeeded();
  await screenshot('advanced-measurements-production-mobile-metric.png');
  if (errors.length) throw new Error(errors.join('; '));
  Object.assign(report, { runId: run.id, replayRunId: replay.id, referenceRunId: reference.id, crossEngineBitIdentical: true, referenceElapsedMs: interpreted.progress.elapsedMs,
    rounds: run.progress.sampleCount, observedRtp: run.progress.runningRtp, authoredReferenceRtp: .98,
    configHash: run.configHash, measurementHash: run.measurementHash, runtimeProvenance: run.runtimeProvenance, measurements: values,
    execution: run.progress.execution, elapsedMs: run.progress.elapsedMs, roundsPerSecond: 100000000 / run.progress.elapsedMs,
    liveFrames: frames.length, primaryRunLiveFrames: frames.filter(f => f.runId === run.id).length,
    paidTurnoverReconciled: true, independentLawComparisonRetained: true, cohortLifecycleReconciled: true, wholeBonusPayoutReconciled: true, wholeBonusCohortsReconciled: true,
    workerReplayBitIdentical: true, mobileFits: true, browserErrors: errors,
    simulateUrl: base + `/simulate?run=${run.id}`, resultsUrl: base + `/results?run=${run.id}` });
  await writeFile(root + '/docs/verification/advanced-measurements-production.json', JSON.stringify(report, null, 2) + '\n');
  console.log(JSON.stringify({ runId: run.id, observedRtp: report.observedRtp, referenceRtp: .98, liveFrames: frames.length, simulateUrl: report.simulateUrl }));
} finally { for (const id of owned) await context.request.delete(base + '/api/runs/' + id).catch(() => {}); await browser.close(); }
