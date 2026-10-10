import { test, expect, getWithQuota } from './fixtures';

test('A native three-component plan collects all covariances and retains accounting across both engines and an empty browser', async ({ page }) => {
  test.setTimeout(240000);
  await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles('e2e/fixtures/component-accounting-model.json');
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByRole('button', { name: 'Create component accounting plan', exact: true }).click();
  await page.getByLabel('Reconciliation name').fill('Three components'); await page.getByLabel('Accounting observation point').selectOption('sink');
  await page.getByLabel('Accounting unit').fill('coins'); await page.getByLabel('Accounting external cost').fill('2'); await page.getByLabel('Accounting total value').selectOption('state:total');
  await page.getByLabel('Component 1 name').fill('X'); await page.getByLabel('Component 1 value').selectOption('state:x');
  await page.getByLabel('Component 2 name').fill('Y'); await page.getByLabel('Component 2 value').selectOption('state:y');
  await page.getByRole('button', { name: 'Add component', exact: true }).click(); await page.getByLabel('Component 3 name').fill('Z'); await page.getByLabel('Component 3 value').selectOption('state:z');
  await page.getByLabel('Accounting cohorts enabled').check(); await page.getByLabel('Accounting cohort node type').selectOption('fieldAccess'); await page.getByLabel('Accounting cohort state path').fill('cohort');
  await page.getByRole('button', { name: 'Save accounting plan', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
  await expect(page.locator('.measurement-plan > summary')).toContainText('8 / 32 metrics');
  await page.getByLabel('Simulation spins').fill('100');
  let prior: unknown;
  for (const engine of ['auto', 'reference']) {
    await page.getByLabel('Simulation spins').fill('100');
    await page.locator('#execution-configuration').evaluate((el: HTMLDetailsElement) => { el.open = true; }); await page.getByLabel('Sampling engine', { exact: true }).selectOption(engine);
    const launch = page.waitForResponse(r => r.url().endsWith('/api/runs') && r.request().method() === 'POST' && r.ok(), { timeout: 70000 });
    await page.getByRole('button', { name: /^▶ Start (new )?run$/ }).click(); const { id } = await (await launch).json();
    await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
    const evidence = await (await getWithQuota(page.request, `/api/runs/${id}/evidence`)).json(); const metrics = evidence.run.progress.measurements;
    expect(metrics).toHaveLength(8); expect(metrics.every((m: { count: number; errors: number }) => m.count === 100 && m.errors === 0)).toBe(true);
    if (prior) expect(metrics).toEqual(prior); prior = metrics;
    const check = page.locator('#component-accounting'); await check.locator('summary').first().click(); await expect(check.getByLabel('Accounting plan')).not.toHaveValue('');
    await check.getByLabel('Accounting result cohort').selectOption({ label: 'all' });
    await check.getByRole('button', { name: 'Reconcile pinned components', exact: true }).click();
    const result = check.getByLabel('Component reconciliation result'); await expect(result).toHaveAttribute('data-accounting-status', 'noObservedViolations', { timeout: 70000 });
    await expect(result.locator('[data-accounting=exactViolations]')).toHaveText('0');
    const retained = await (await getWithQuota(page.request, `/api/runs/${id}/evidence`)).json(); const artifact = retained.diagnostics.find((d: { kind: string }) => d.kind === 'component-accounting');
    const report = artifact.output.report, variance = metrics[0].analysis.moments.sampleVariance;
    expect(report.componentVarianceSum).toBeCloseTo(3 * variance, 10); expect(report.twiceCovarianceSum).toBeCloseTo(-2 * variance, 10); expect(report.reconstructedVariance).toBeCloseTo(variance, 10);
    expect(report.covariances).toHaveLength(3);
    report.covariances.forEach((c: { covariance: number }, i: number) => expect(c.covariance).toBeCloseTo(i ? -variance : variance, 10));
    expect(artifact.output.source).toMatchObject({ configHash: evidence.run.configHash, measurementHash: evidence.run.measurementHash, sequence: evidence.run.sequence, paidRounds: 100 });
    await page.goto(`/results?run=${id}`); await page.evaluate(() => localStorage.clear()); await page.reload();
    const saved = page.locator('#component-accounting'); await saved.locator('summary').first().click(); await saved.getByLabel('Accounting result cohort').selectOption({ label: 'all' });
    await expect(saved.getByLabel('Component reconciliation result')).toHaveAttribute('data-accounting-status', 'noObservedViolations');
    await expect(saved).toContainText('Retained server calculation restored'); await expect(page.getByLabel('Retained diagnostic evidence')).toContainText('component-accounting');
    await page.setViewportSize({ width: 390, height: 844 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await expect(saved.getByLabel('Component reconciliation result').getByRole('table')).toHaveCount(2);
    await page.setViewportSize({ width: 1280, height: 720 }); await page.goto(`/simulate?run=${id}`);
    if (engine === 'auto') {
      await page.goto(`/results?run=${id}&view=reproducibility`); await page.getByRole('button', { name: 'Open pinned graph ↗', exact: true }).click();
      await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
      await page.locator('.measurement-plan > summary').click(); await page.getByRole('button', { name: 'Use this run’s collection plan', exact: true }).click();
    }
  }
});

test('Compiler errors reject the entire accounting plan and a corrected residual exposes a real mismatch', async ({ page }) => {
  test.setTimeout(180000);
  await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles('e2e/fixtures/component-accounting-model.json'); await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByRole('button', { name: 'Create component accounting plan', exact: true }).click();
  await page.getByLabel('Reconciliation name').fill('Incomplete components'); await page.getByLabel('Accounting observation point').selectOption('sink'); await page.getByLabel('Accounting total value').selectOption('state:total');
  await page.getByLabel('Component 1 value').selectOption('state:x'); await page.getByLabel('Component 2 value').selectOption('visual'); await page.getByLabel('Component 2 state path').fill('missing');
  await page.getByRole('button', { name: 'Save accounting plan', exact: true }).click(); await expect(page.getByRole('dialog').getByRole('alert')).toContainText('missing');
  await page.getByLabel('Component 2 state path').fill('y'); await page.getByRole('button', { name: 'Save accounting plan', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
  await expect(page.locator('.measurement-plan > summary')).toContainText('5 / 32 metrics');
  await page.getByLabel('Simulation spins').fill('100'); const launch = page.waitForResponse(r => r.url().endsWith('/api/runs') && r.request().method() === 'POST' && r.ok(), { timeout: 70000 });
  await page.getByRole('button', { name: /^▶ Start run$/ }).click(); await launch; await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
  const check = page.locator('#component-accounting'); await check.locator('summary').first().click(); await check.getByRole('button', { name: 'Reconcile pinned components', exact: true }).click();
  const result = check.getByLabel('Component reconciliation result'); await expect(result).toHaveAttribute('data-accounting-status', 'discrepancy', { timeout: 70000 });
  expect(Number(await result.locator('[data-accounting=exactViolations]').textContent())).toBeGreaterThan(0);
});

test('Dismissing whole-plan validation cannot append a late accounting batch', async ({ page }) => {
  let release!: () => void, started!: () => void, handled!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; }), accepted = new Promise<void>(resolve => { started = resolve; }), settled = new Promise<void>(resolve => { handled = resolve; });
  await page.route('**/api/runs/measurements/schema', async route => {
    if (!route.request().postDataJSON().measurements?.length) return route.continue();
    started(); await gate;
    try { await route.fulfill({ status: 200, contentType: 'application/json', body: '{"points":[],"fields":[]}' }); } catch { /* Closing aborts this validation. */ }
    finally { handled(); }
  });
  await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles('e2e/fixtures/component-accounting-model.json'); await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByRole('button', { name: 'Create component accounting plan', exact: true }).click();
  await page.getByLabel('Component 1 value').selectOption('state:x'); await page.getByLabel('Component 2 value').selectOption('state:y');
  await page.getByRole('button', { name: 'Save accounting plan', exact: true }).click(); await accepted;
  await page.getByLabel('Close accounting editor').click(); await expect(page.getByRole('dialog')).not.toBeVisible();
  release(); await settled; await page.reload(); await expect(page.locator('.measurement-plan > summary')).toContainText('0 / 32 metrics');
});
