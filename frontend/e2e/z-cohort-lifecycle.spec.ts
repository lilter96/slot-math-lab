import { waitForRunLaunch, test, expect, getWithQuota } from './fixtures';

test('A feature cohort with no matching children retains lifecycle exposure in live and saved UI', async ({ page }) => {
  test.setTimeout(180000);
  await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles('e2e/fixtures/measurement-model.json');
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByRole('button', { name: '＋ Track metric', exact: true }).click();
  await page.getByLabel('Metric name').fill('Empty feature cohort');
  await page.getByLabel('Metric observation level').selectOption('node'); await page.getByLabel('Metric graph node').selectOption('end');
  await page.getByLabel('Metric numeric expression').fill('state.spinWin');
  await page.getByLabel('Enable advanced measurement').check();
  await page.getByLabel('Measurement subject', { exact: true }).selectOption('episode');
  await page.getByLabel('Subject reduction', { exact: true }).selectOption('average');
  await page.getByLabel('Episode entry', { exact: true }).selectOption('fs'); await page.getByLabel('Episode exit', { exact: true }).selectOption('sink');
  await page.getByLabel('Metric filter mode').selectOption('expression'); await page.getByLabel('Metric filter expression').fill('false');
  await page.getByText('Group, pair and award accounting', { exact: true }).click(); await page.getByLabel('Group / cohort key', { exact: true }).fill('"empty"');
  await page.getByRole('button', { name: 'Save measurement', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
  await page.getByLabel('Simulation spins').fill('100');
  let previous: unknown;
  for (const engine of ['auto', 'reference']) {
    await page.locator('#execution-configuration').evaluate((element: HTMLDetailsElement) => { element.open = true; }); await page.getByLabel('Sampling engine', { exact: true }).selectOption(engine);
    const launch = waitForRunLaunch(page);
    await page.getByRole('button', { name: /^▶ Start (new )?run$/ }).click(); const { id } = await (await launch).json();
    await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
    const card = page.getByRole('article', { name: 'Tracked metric Empty feature cohort', exact: true });
    await expect(card).toContainText('100 eligible feature episodes');
    const tails = card.getByRole('table').filter({ has: page.getByRole('columnheader', { name: 'Tail ≥', exact: true }) });
    await expect(tails).toHaveCSS('border-collapse', 'collapse');
    expect(await tails.locator('tbody tr td:nth-child(3)').allTextContents()).toEqual(['—', '—', '—']);
    await card.getByRole('tab', { name: 'accounting', exact: true }).click(); await card.getByLabel('Analysis population').selectOption('empty');
    await expect(card.getByRole('row').filter({ hasText: 'Feature entries / exits / unclosed' })).toContainText('100 / 100 / 0');
    await expect(card).toContainText('entries − exits − unclosed = 0');
    const evidence = await (await getWithQuota(page.request, `/api/runs/${id}/evidence`)).json();
    const metric = evidence.run.progress.measurements[0];
    expect(metric).toMatchObject({ count: 0, excluded: 100, errors: 0, mean: null, analysis: { entries: 100, exits: 100,
      groups: { empty: { count: 0, mean: null, entries: 100, exits: 100, unclosedEpisodes: 0, distinctParents: 0, normalization: { paidRounds: 100 } } } } });
    if (previous) expect(metric).toEqual(previous); previous = metric;
    // A direct retained link must work without first loading the lazy Simulate route.
    await page.goto(`/results?run=${id}`);
    // Reproduce a legal pause longer than the local paint assertion. This is
    // an explicitly rejected read; production limiter settings are unchanged.
    await page.route(`**/api/runs/${id}/evidence`, route => route.fulfill({ status: 429, headers: { 'Retry-After': '6' }, body: '' }), { times: 1 });
    await page.reload();
    const saved = page.getByRole('region', { name: 'Saved measurement Empty feature cohort', exact: true });
    // Loading honors an explicit server Retry-After window. Check CSS only
    // after authoritative evidence is present; rendering keeps its 5s limit.
    await expect(saved).toBeVisible({ timeout: 70000 });
    await expect(saved.locator('.measurement-quantiles')).toHaveCSS('display', 'grid');
    await expect(saved.getByRole('tab', { name: 'distribution', exact: true })).toHaveCSS('background-color', 'rgb(20, 86, 72)');
    await saved.getByRole('tab', { name: 'accounting', exact: true }).click(); await saved.getByLabel('Analysis population').selectOption('empty');
    await expect(saved.getByRole('row').filter({ hasText: 'Feature entries / exits / unclosed' })).toContainText('100 / 100 / 0');
    await page.setViewportSize({ width: 390, height: 844 });
    expect(await saved.locator('.measurement-summary-table').evaluate(e => e.scrollWidth <= e.parentElement!.clientWidth)).toBe(true);
    await page.setViewportSize({ width: 1280, height: 720 });
    await page.goto(`/simulate?run=${id}`);
  }
});
