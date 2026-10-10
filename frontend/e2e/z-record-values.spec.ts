import { saveMeasurement, waitForRunLaunch, test, expect, getWithQuota } from './fixtures';

test('Visual record filtering preserves authored fractions and nested arrays, rejects false paths and retains both-engine evidence', { tag: '@critical' }, async ({ page, request }) => {
  test.setTimeout(240000);
  await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles('e2e/fixtures/record-model.json');
  await expect(page.locator('.react-flow__node')).toHaveCount(5);
  await page.getByLabel('Focus graph node').selectOption('filter');
  await expect(page.getByLabel('Expression item type', { exact: true })).toHaveValue('Record');
  await page.getByLabel('Expression item type', { exact: true }).selectOption('Number');
  await page.getByLabel('Expression item type', { exact: true }).selectOption('Record');
  await expect(page.getByLabel('Expression Predicate Left state path', { exact: true })).toHaveValue('offer.tag');
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByRole('button', { name: '＋ Track metric', exact: true }).click();
  await page.getByLabel('Metric name').fill('Rejected suffix'); await page.getByLabel('Metric value source').selectOption('expression');
  await page.getByLabel('Metric numeric expression').fill('state.payout.suffix');
  const rejected = page.waitForResponse(r => r.url().endsWith('/api/runs/measurements/schema') && r.status() === 400, { timeout: 70000 });
  await page.getByRole('button', { name: 'Save measurement', exact: true }).click(); await rejected;
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('payout.suffix');
  await page.getByLabel('Close measurement editor').click();
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
  await page.getByLabel('Simulation spins').fill('100');
  let previous: unknown;
  for (const engine of ['auto', 'reference']) {
    await page.locator('#execution-configuration').evaluate((element: HTMLDetailsElement) => { element.open = true; });
    await page.getByLabel('Sampling engine', { exact: true }).selectOption(engine);
    const launch = waitForRunLaunch(page); await page.getByRole('button', { name: /^▶ Start (new )?run$/ }).click();
    const { id } = await (await launch).json(); await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
    const award = page.getByRole('article', { name: 'Tracked metric Selected record award', exact: true });
    await expect(award.locator('[data-statistic=min]')).toHaveText('0.25'); await expect(award.locator('[data-statistic=max]')).toHaveText('1.75');
    const history = page.getByRole('article', { name: 'Tracked metric Nested history', exact: true });
    await expect(history.locator('[data-statistic=min]')).toHaveText('1'); await expect(history.locator('[data-statistic=max]')).toHaveText('3');
    const evidence = await (await getWithQuota(request, `/api/runs/${id}/evidence`)).json();
    expect(evidence.inputVerified).toBe(true); expect(evidence.run.progress.measurements.map((m: { count: number; errors: number }) => [m.count, m.errors])).toEqual([[100, 0], [100, 0]]);
    if (previous) expect(evidence.run.progress.measurements).toEqual(previous);
    previous = evidence.run.progress.measurements;
    await page.reload(); await expect(award.locator('[data-statistic=min]')).toHaveText('0.25');
    await page.goto(`/results?run=${id}`); await expect(page.getByRole('region', { name: 'Saved measurement Selected record award', exact: true })).toBeVisible({ timeout: 70000 });
    await page.goto(`/simulate?run=${id}`);
  }
});
