import { test, expect, getWithQuota } from './fixtures';

test('Fractional constructor arithmetic survives the UI, both samplers, exact law and saved evidence', async ({ page }) => {
  test.setTimeout(180000);
  await page.goto('/build');
  const calculation = page.waitForResponse(r => r.url().includes('/api/evaluate/light') && r.request().method() === 'POST' && r.ok() && r.request().postDataJSON()?.config?.expressions?.fractions?.stateKey === 'rawValues', { timeout: 70000 });
  await page.getByLabel('Import project file').setInputFiles('e2e/fixtures/fractional-model.json');
  await expect(page.locator('.react-flow__node')).toHaveCount(4);
  // Manual rule: sum([-1/2,2/3,5/4])*12 + min(3/2,7/4)*4
  //              + abs(-1/2)*2 + floor(-1/2)+1 = 17+6+1+0 = 24.
  expect(await (await calculation).json()).toMatchObject({ rtp: 24, strategy: 'Exact' });
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByRole('button', { name: '＋ Track metric', exact: true }).click();
  await page.getByLabel('Metric name').fill('Fractional magnitude');
  await page.getByLabel('Metric value source').selectOption('expression');
  await page.getByLabel('Metric numeric expression').fill('abs(state.fractions[0])');
  await page.getByLabel('Enable advanced measurement').check();
  await page.getByRole('button', { name: 'Save measurement', exact: true }).click();
  await expect(page.getByRole('dialog')).not.toBeVisible();
  await page.getByLabel('Simulation spins').fill('100');
  let previous: unknown;
  for (const engine of ['auto', 'reference']) {
    await page.locator('#execution-configuration').evaluate((element: HTMLDetailsElement) => { element.open = true; });
    await page.getByLabel('Sampling engine', { exact: true }).selectOption(engine);
    const launch = page.waitForResponse(r => r.url().endsWith('/api/runs') && r.request().method() === 'POST' && r.ok(), { timeout: 70000 });
    await page.getByRole('button', { name: /^▶ Start (new )?run$/ }).click(); const created = await (await launch).json();
    await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
    const card = page.getByRole('article', { name: 'Tracked metric Fractional magnitude', exact: true });
    await expect(card.locator('[data-statistic=mean]')).toHaveText('0.5 × stake');
    const evidence = await (await getWithQuota(page.request, `/api/runs/${created.id}/evidence`)).json();
    expect(evidence.inputVerified).toBe(true); expect(evidence.run.progress.runningRtp).toBe(24);
    const measurement = evidence.run.progress.measurements[0];
    expect(measurement).toMatchObject({ count: 100, errors: 0, min: .5, max: .5, mean: .5 });
    expect(measurement.analysis.support).toEqual([{ value: .5, count: 100, sum: 50 }]);
    if (previous) expect(measurement).toEqual(previous); previous = measurement;
    await page.locator('.graph-measurement-reference > summary').click();
    await page.getByRole('button', { name: 'Enumerate pinned measurements', exact: true }).click();
    await expect(page.getByLabel('Pinned graph enumeration result')).toContainText('24/1', { timeout: 70000 });
    await page.reload(); await expect(card.locator('[data-statistic=mean]')).toHaveText('0.5 × stake');
    await page.goto(`/results?run=${created.id}`);
    await expect(page.getByLabel('Retained diagnostic evidence')).toContainText('graph-enumeration', { timeout: 70000 });
    await page.goto(`/simulate?run=${created.id}`);
  }
});
