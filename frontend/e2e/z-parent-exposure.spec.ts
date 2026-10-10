import { waitForRunLaunch, test, expect, getWithQuota } from './fixtures';

test('Native scoped feature counters distinguish matching children, paid parents and owning episodes', async ({ page }) => {
  test.setTimeout(180000);
  // Independent rule: the feature has sticky/other/sticky children. Its sticky
  // child count is two, but only one paid parent and one owning episode match.
  await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles('e2e/fixtures/measurement-model.json');
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click(); await page.getByRole('button', { name: '＋ Track metric', exact: true }).click();
  await page.getByLabel('Metric name').fill('Sticky parent exposure');
  await page.getByLabel('Metric unit').fill('reveals');
  await page.getByLabel('Metric observation level').selectOption('node'); await page.getByLabel('Metric graph node').selectOption('end');
  await page.getByLabel('Enable advanced measurement').check(); await page.getByLabel('Measurement source', { exact: true }).selectOption('count');
  await page.getByLabel('Measurement subject', { exact: true }).selectOption('episode'); await page.getByLabel('Subject reduction', { exact: true }).selectOption('count');
  await page.getByLabel('Episode entry', { exact: true }).selectOption('fs'); await page.getByLabel('Episode exit', { exact: true }).selectOption('sink');
  await page.getByLabel('Metric filter mode').selectOption('expression'); await page.getByLabel('Metric filter expression').fill('state.fsType == "sticky"');
  await page.getByLabel('Paid rounds with matching children', { exact: true }).check(); await page.getByLabel('Feature episodes with matching children', { exact: true }).check();
  await page.getByRole('button', { name: 'Save measurement', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
  await page.getByLabel('Simulation spins').fill('100');
  let prior: unknown;
  for (const engine of ['auto', 'reference']) {
    await page.locator('#execution-configuration').evaluate((element: HTMLDetailsElement) => { element.open = true; }); await page.getByLabel('Sampling engine', { exact: true }).selectOption(engine);
    const launch = waitForRunLaunch(page);
    await page.getByRole('button', { name: /^▶ Start (new )?run$/ }).click(); const { id } = await (await launch).json();
    await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
    const card = page.getByRole('article', { name: 'Tracked metric Sticky parent exposure', exact: true });
    await expect(card.locator('[data-statistic=mean]')).toHaveText('2 reveals');
    await expect(card.locator('[data-statistic=distinctParents]')).toHaveText('100'); await expect(card.locator('[data-statistic=matchingEpisodes]')).toHaveText('100');
    await card.getByRole('tab', { name: 'accounting', exact: true }).click(); await expect(card.getByRole('row').filter({ hasText: 'Owning feature episodes with matching children' })).toContainText('100');
    const evidence = await (await getWithQuota(page.request, `/api/runs/${id}/evidence`)).json(); const metric = evidence.run.progress.measurements[0];
    expect(metric).toMatchObject({ count: 100, mean: 2, analysis: { parentExposure: { paidRoundsWithMatchingChildren: 100, episodesWithMatchingChildren: 100 } } });
    if (prior) expect(metric).toEqual(prior); prior = metric;
    await page.getByText('Enumerate the pinned graph · complete measurement laws for small models', { exact: true }).click();
    await page.getByRole('button', { name: 'Enumerate pinned measurements', exact: true }).click();
    const enumeration = page.getByLabel('Pinned graph enumeration result'); await expect(enumeration).toContainText('Enumerated', { timeout: 70000 });
    await enumeration.getByText('Sticky parent exposure · probability support', { exact: true }).click();
    await expect(enumeration).toContainText('matching paid parents / round 1/1'); await expect(enumeration).toContainText('owning matching episodes / round 1/1');
    await page.goto(`/results?run=${id}`); await page.reload(); const saved = page.getByRole('region', { name: 'Saved measurement Sticky parent exposure', exact: true });
    await saved.getByRole('tab', { name: 'accounting', exact: true }).click(); await expect(saved.getByRole('row').filter({ hasText: 'Owning feature episodes with matching children' })).toContainText('100');
    await page.goto(`/simulate?run=${id}`);
  }
});
