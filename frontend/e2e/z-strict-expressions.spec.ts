import { saveMeasurement, waitForRunLaunch, test, expect, getWithQuota, type Page } from './fixtures';

async function metric(page: Page, name: string, expression: string, assertion = false) {
  await page.getByRole('button', { name: '＋ Track metric', exact: true }).click();
  await page.getByLabel('Metric name').fill(name); await page.getByLabel('Metric observation level').selectOption('node');
  await page.getByLabel('Metric graph node').selectOption('sink'); await page.getByLabel('Metric numeric expression').fill(expression);
  await page.getByLabel('Metric unit').fill('');
  if (assertion) { await page.getByLabel('Enable advanced measurement').check(); await page.getByLabel('Assert exactly zero or false').check(); }
  await saveMeasurement(page);
}

test('Native collection calls infer numeric arrays and retain invalid conversions instead of zero statistics', async ({ page, request }) => {
  test.setTimeout(240000);
  await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles('e2e/fixtures/strict-expression-model.json');
  await expect(page.locator('.react-flow__node')).toHaveCount(2); await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByRole('button', { name: '＋ Track metric', exact: true }).click();
  await page.getByLabel('Metric name').fill('Rejected shape'); await page.getByLabel('Metric value source').selectOption('expression');
  await page.getByLabel('Metric numeric expression').fill('length(1)');
  const rejected = page.waitForResponse(r => r.url().endsWith('/api/runs/measurements/schema') && r.status() === 400, { timeout: 70000 });
  await page.getByRole('button', { name: 'Save measurement', exact: true }).click(); await rejected;
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('array or text'); await page.getByLabel('Close measurement editor').click();
  await metric(page, 'Numeric index', 'index(state.values, 0)');
  await metric(page, 'Exact text roundtrip', 'tonumber(tostring(1 / 3)) - 1 / 3', true);
  await metric(page, 'Invalid text', 'tonumber(state.text)');
  await page.getByLabel('Simulation spins').fill('100');
  let previous: unknown;
  for (const engine of ['auto', 'reference']) {
    await page.locator('#execution-configuration').evaluate((element: HTMLDetailsElement) => { element.open = true; });
    await page.getByLabel('Sampling engine', { exact: true }).selectOption(engine);
    const launch = waitForRunLaunch(page); await page.getByRole('button', { name: /^▶ Start (new )?run$/ }).click();
    const { id } = await (await launch).json(); await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
    const numeric = page.getByRole('article', { name: 'Tracked metric Numeric index', exact: true });
    await expect(numeric.locator('[data-statistic=mean]')).toHaveText('0.5');
    await expect(page.getByRole('article', { name: 'Tracked metric Exact text roundtrip', exact: true })).toContainText('no Observed Violations');
    const invalid = page.getByRole('article', { name: 'Tracked metric Invalid text', exact: true });
    await expect(invalid).toContainText('100 invalid'); await expect(invalid.locator('[data-statistic=mean]')).toHaveText('—');
    await expect(invalid.getByRole('alert')).toContainText('tonumber');
    const evidence = await (await getWithQuota(request, `/api/runs/${id}/evidence`)).json();
    expect(evidence.inputVerified).toBe(true); expect(evidence.run.progress.measurements[2]).toMatchObject({ observations: 100, count: 0, errors: 100, min: null, max: null, mean: null, sum: null });
    if (previous) expect(evidence.run.progress.measurements).toEqual(previous);
    previous = evidence.run.progress.measurements;
    await page.reload(); await expect(invalid).toContainText('100 invalid');
    await page.goto(`/results?run=${id}`);
    const saved = page.getByRole('region', { name: 'Saved measurement Invalid text', exact: true });
    await expect(saved).toBeVisible({ timeout: 70000 });
    const row = page.getByRole('table').filter({ has: page.getByRole('columnheader', { name: 'Measurement / scope', exact: true }) }).getByRole('row').filter({ hasText: 'Invalid text' });
    await expect(row).toContainText('100');
    await page.goto(`/simulate?run=${id}`);
  }
});
