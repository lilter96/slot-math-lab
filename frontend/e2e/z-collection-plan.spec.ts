import { test, expect, getWithQuota } from './fixtures';

test('Saving validates shared collection storage and an adjusted complete plan runs', async ({ page }) => {
  test.setTimeout(180000);
  await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles('e2e/fixtures/measurement-model.json');
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  for (const name of ['Population A', 'Population B', 'Population C', 'Population D']) {
    await page.getByRole('button', { name: '＋ Track metric', exact: true }).click(); await page.getByLabel('Metric name').fill(name); await page.getByLabel('Metric unit').fill('visits');
    await page.getByLabel('Metric observation level').selectOption('node'); await page.getByLabel('Metric graph node').selectOption('end');
    await page.getByLabel('Enable advanced measurement').check(); await page.getByLabel('Measurement source', { exact: true }).selectOption('count');
    await page.getByText('Group, pair and award accounting', { exact: true }).click(); await page.getByLabel('Group / cohort key', { exact: true }).fill('"constant cohort"');
    await page.getByLabel('Maximum groups', { exact: true }).fill('8');
    await page.getByText('Distribution and tail coverage', { exact: true }).click(); await page.getByLabel('Support limit', { exact: true }).fill('1024');
    if (name === 'Population D') {
      const rejected = page.waitForResponse(r => r.url().endsWith('/measurements/schema') && r.status() === 400);
      await page.getByRole('button', { name: 'Save measurement', exact: true }).click();
      expect((await rejected).request().postDataJSON().measurements).toHaveLength(4);
      await expect(page.getByRole('dialog').getByRole('alert')).toContainText('32768-cell storage budget');
      await expect(page.locator('.measurement-plan > summary')).toContainText('3 / 32 metrics');
      // Every declaration meets its individual 8,192-support-cell limit.
      // Four together exceed 32,768 cells; one constant cohort for D fits.
      await page.getByLabel('Maximum groups', { exact: true }).fill('1');
    }
    await page.getByRole('button', { name: 'Save measurement', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
  }
  await expect(page.locator('.measurement-plan > summary')).toContainText('4 / 32 metrics');
  await page.getByLabel('Simulation spins').fill('100'); const launch = page.waitForResponse(r => r.url().endsWith('/api/runs') && r.request().method() === 'POST' && r.ok());
  await page.getByRole('button', { name: /^▶ Start run$/ }).click(); const { id } = await (await launch).json();
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
  const evidence = await (await getWithQuota(page.request, `/api/runs/${id}/evidence`)).json();
  expect(evidence.run.progress.measurements).toHaveLength(4);
  for (const metric of evidence.run.progress.measurements) expect(metric).toMatchObject({ count: 300, errors: 0, mean: 1, analysis: { parentExposure: { paidRoundsWithMatchingChildren: 100 } } });
});

test('Closing an in-flight plan validation cannot save a late metric', async ({ page }) => {
  let release!: () => void, started!: () => void, handled!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; }), accepted = new Promise<void>(resolve => { started = resolve; }), settled = new Promise<void>(resolve => { handled = resolve; });
  await page.route('**/api/runs/measurements/schema', async route => {
    if (!route.request().postDataJSON().measurements?.length) return route.continue();
    started(); await gate;
    try { await route.fulfill({ status: 200, contentType: 'application/json', body: '{"points":[],"fields":[]}' }); } catch { /* The editor deliberately aborts the read. */ }
    finally { handled(); }
  });
  await page.goto('/simulate'); await page.getByRole('button', { name: '＋ Track metric', exact: true }).click(); await page.getByLabel('Metric name').fill('Cancelled validation');
  await page.getByRole('button', { name: 'Save measurement', exact: true }).click(); await accepted;
  await page.getByLabel('Close measurement editor').click(); await expect(page.getByRole('dialog')).not.toBeVisible();
  release(); await settled; await page.reload();
  await expect(page.locator('.measurement-plan > summary')).toContainText('0 / 32 metrics');
});
