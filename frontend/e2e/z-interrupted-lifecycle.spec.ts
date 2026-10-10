import { saveMeasurement, waitForRunLaunch, test, expect, getWithQuota } from './fixtures';
import { readFile } from 'node:fs/promises';

test('A failed feature keeps interrupted boundaries in live and saved UI without publishing partial values', async ({ page, request }) => {
  test.setTimeout(240000);
  // Manual oracle: feature entry precedes a division by zero; no reveal, exit or paid settlement occurs.
  await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles('e2e/fixtures/interrupted-feature-model.json');
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click(); await page.getByRole('button', { name: '＋ Track metric', exact: true }).click();
  await page.getByLabel('Metric name').fill('Interrupted feature');
  await page.getByLabel('Metric observation level').selectOption('node'); await page.getByLabel('Metric graph node').selectOption('end');
  await page.getByLabel('Enable advanced measurement').check(); await page.getByLabel('Measurement source', { exact: true }).selectOption('count');
  await page.getByLabel('Measurement subject', { exact: true }).selectOption('episode');
  await page.getByLabel('Episode entry', { exact: true }).selectOption('fs'); await page.getByLabel('Episode exit', { exact: true }).selectOption('sink');
  await page.getByText('Group, pair and award accounting', { exact: true }).click(); await page.getByLabel('Group / cohort key', { exact: true }).fill('"sticky"');
  for (const name of ['Feature entries · unfinished rounds', 'Feature exits · unfinished rounds', 'Open features at interruption']) await page.getByLabel(name, { exact: true }).check();
  await saveMeasurement(page);
  let previous: unknown;
  for (const engine of ['auto', 'reference']) {
    await page.getByLabel('Simulation spins').fill('100'); await page.locator('#execution-configuration').evaluate((element: HTMLDetailsElement) => { element.open = true; });
    await page.getByLabel('Sampling engine', { exact: true }).selectOption(engine);
    const launch = waitForRunLaunch(page); await page.getByRole('button', { name: /^▶ Start (new )?run$/ }).click(); const { id } = await (await launch).json();
    await expect(page.locator('.run-status')).toHaveText('failed', { timeout: 70000 });
    const card = page.getByRole('article', { name: 'Tracked metric Interrupted feature', exact: true });
    await expect(card.locator('[data-statistic=mean]')).toHaveText('—');
    await expect(card.locator('[data-statistic=interruptedFeatureEntries]')).toHaveText('1');
    await expect(card.locator('[data-statistic=interruptedFeatureExits]')).toHaveText('0');
    await expect(card.locator('[data-statistic=interruptedOpenEpisodes]')).toHaveText('1');
    await card.getByRole('tab', { name: 'accounting', exact: true }).click();
    await expect(card.getByRole('region', { name: 'Interrupted feature lifecycle', exact: true }).getByRole('row').filter({ hasText: 'failed' }).getByRole('cell')).toHaveText(['failed', '1', '1', '0', '1', 'complete']);
    const evidence = await (await getWithQuota(request, `/api/runs/${id}/evidence`)).json(); const metric = evidence.run.progress.measurements[0];
    expect(evidence.inputVerified).toBe(true);
    expect(metric).toMatchObject({ observations: 0, count: 0, errors: 0, mean: null, analysis: { entries: 0, exits: 0, normalization: null,
      interruptedLifecycle: { failed: { interruptedRounds: 1, entries: 1, exits: 0, openInstances: 1, complete: true } } } });
    expect(metric.analysis.groups.sticky.interruptedLifecycle).toEqual(metric.analysis.interruptedLifecycle);
    expect(evidence.run.progress.execution).toMatchObject({ attemptedRounds: 1, completedRounds: 0, failedRounds: 1 });
    await expect(page.getByRole('button', { name: '↓ Export evidence', exact: true })).toBeEnabled();
    const download = page.waitForEvent('download', { timeout: 70000 }); await page.getByRole('button', { name: '↓ Export evidence', exact: true }).click();
    const exported = JSON.parse(await readFile((await (await download).path())!, 'utf8'));
    expect(exported.progress.measurements[0]).toEqual(metric); expect(exported.pinnedGraph.inputVerified).toBe(true);
    expect(exported.integrity.complete).toBe(false); expect(exported.progress.sampleCount).toBe(0);
    if (previous) expect(metric).toEqual(previous); previous = metric;
    await page.reload(); await expect(card.locator('[data-statistic=interruptedOpenEpisodes]')).toHaveText('1');
    await page.goto(`/results?run=${id}`);
    const saved = page.getByRole('region', { name: 'Saved measurement Interrupted feature', exact: true }); await expect(saved).toBeVisible({ timeout: 70000 });
    await saved.getByRole('tab', { name: 'accounting', exact: true }).click(); await saved.getByLabel('Analysis population').selectOption('sticky');
    await expect(saved.getByRole('region', { name: 'Interrupted feature lifecycle', exact: true }).getByRole('row').filter({ hasText: 'failed' }).getByRole('cell')).toHaveText(['failed', '1', '1', '0', '1', 'complete']);
    await expect(saved).toContainText('0 subjects · average —');
    await page.goto(`/simulate?run=${id}`);
  }
});
