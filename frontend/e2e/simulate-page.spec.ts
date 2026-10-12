import { test, expect, saveMeasurement, waitForRunLaunch, type Page } from './fixtures';
async function capture(page: Page, state: string) {
  for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
    await page.setViewportSize({ width, height });
    for (const [name, selector] of [['setup', '.simulation-setup'], ['progress', '.simulation-progress'], ['metrics', '.simulation-metrics'], ['convergence', '.convergence-card'], ['distribution', '.simulation-two-columns'], ['measurements', '.measurement-workspace:not(.reference-workbench)'], ['reference', '.reference-workbench'], ['planning', '#sample-planning'], ['footer', '.simulation-page-footer']]) {
      const target = page.locator(selector).first(); if (!await target.count()) continue;
      await target.scrollIntoViewIfNeeded(); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: `../docs/design/qa/simulate-page-${test.info().project.name}-${state}-${name}-${width}.png` });
    }
  }
}
test('Complete Simulate page keeps run results above analysis and supports all execution/reference forms', async ({ page }) => {
  test.setTimeout(120000);
  await page.goto('/build'); await page.getByRole('button', { name: 'Load coin example', exact: true }).click();
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await expect(page.getByTestId('live-rtp')).toHaveText('—');
  await page.locator('.execution-configuration > summary').click();
  for (const regime of ['independentRounds', 'persistent', 'sessions']) {
    await page.getByLabel('Execution regime', { exact: true }).selectOption(regime);
    if (regime === 'sessions') await page.getByLabel('Stop policy', { exact: true }).selectOption('profitTarget');
    for (const [width, height] of [[1440, 900], [390, 844]]) {
      await page.setViewportSize({ width, height }); await page.locator('.execution-configuration').scrollIntoViewIfNeeded();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: `../docs/design/qa/simulate-execution-${test.info().project.name}-${regime}-${width}.png` });
    }
  }
  await page.getByLabel('Execution regime', { exact: true }).selectOption('independentRounds');
  await page.getByRole('button', { name: '＋ Track metric', exact: true }).click();
  await page.getByLabel('Metric name').fill('Paid round payout');
  await page.screenshot({ path: `../docs/design/qa/simulate-metric-dialog-${test.info().project.name}.png` });
  await saveMeasurement(page);
  for (const button of ['Create component accounting plan', 'Configure verification profile']) {
    await page.getByRole('button', { name: button, exact: true }).click();
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.screenshot({ path: `../docs/design/qa/simulate-dialog-${test.info().project.name}-${button.split(' ')[0]}.png` });
    await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
  }
  await page.getByRole('button', { name: 'Customize dashboard', exact: true }).click();
  await page.locator('.measurement-display').first().scrollIntoViewIfNeeded();
  await page.screenshot({ path: `../docs/design/qa/simulate-dashboard-settings-${test.info().project.name}.png` });
  await page.locator('#reference-workbench > summary').click();
  for (const type of ['distribution', 'markov', 'stationary']) {
    await page.getByLabel('Reference model type').selectOption(type);
    await page.getByRole('button', { name: 'Load independent example', exact: true }).click();
    await page.locator('.reference-workbench').scrollIntoViewIfNeeded();
    await page.screenshot({ path: `../docs/design/qa/simulate-reference-${test.info().project.name}-${type}.png` });
  }
  const planning = page.getByText('Plan sufficient observations · precision, rare events and repeated monitoring', { exact: true });
  await planning.click();
  await capture(page, 'configured');
  await page.getByLabel('Simulation spins').fill('5000');
  const accepted = waitForRunLaunch(page); await page.getByRole('button', { name: /start run/i }).click(); await accepted;
  await expect(page.locator('.simulation-heading .run-status')).toHaveText('completed', { timeout: 70000 });
  await expect(page.getByTestId('sample-count')).toHaveText('5,000'); await expect(page.getByTestId('live-rtp')).not.toHaveText('—');
  const metrics = await page.locator('.simulation-metrics').boundingBox(), analysis = await page.locator('.simulation-analysis').boundingBox();
  expect(metrics!.y).toBeLessThan(analysis!.y);
  await page.locator('#reference-workbench > summary').click();
  for (const tool of ['Parameters & policies', 'Verified sampling design', 'Resource-stop impact', 'Rule / spec / asset evidence']) {
    const panel = page.getByRole('region', { name: 'Verification experiments' });
    await panel.getByRole('tab', { name: tool, exact: true }).click();
    if (tool === 'Parameters & policies') await panel.getByRole('button', { name: 'Add variant', exact: true }).click();
    if (tool === 'Verified sampling design') await panel.getByRole('button', { name: 'Load independent two-atom example', exact: true }).click();
    if (tool === 'Resource-stop impact') await panel.getByRole('button', { name: 'Add state', exact: true }).click();
    await panel.scrollIntoViewIfNeeded();
    await page.screenshot({ path: `../docs/design/qa/simulate-verification-${test.info().project.name}-${tool.split(' ')[0]}.png` });
  }
  await capture(page, 'completed');
});
test('An empty workspace cannot launch a pretend model and unavailable linked runs remain recoverable', async ({ page }) => {
  await page.goto('/simulate'); await expect(page.getByRole('link', { name: 'Open My projects', exact: true })).toHaveAttribute('href', '/projects');
  await expect(page.getByRole('button', { name: /start run/i })).toBeDisabled(); await expect(page.getByTestId('live-rtp')).toHaveText('—');
  await page.screenshot({ path: `../docs/design/qa/simulate-empty-${test.info().project.name}.png` });
  await page.route('**/api/runs/missing/evidence', route => route.fulfill({ status: 404, json: { error: 'Saved private run unavailable.' } }));
  await page.goto('/simulate?run=missing'); await expect(page.getByRole('alert')).toContainText('Saved private run unavailable');
  await expect(page.getByRole('button', { name: 'Retry saved run', exact: true })).toBeVisible();
});
