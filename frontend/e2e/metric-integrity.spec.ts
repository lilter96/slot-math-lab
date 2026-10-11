import { test, expect } from './fixtures';
test('Pending, absent and failed metrics withhold proof; computed zero survives', { tag: '@critical' }, async ({ page }) => {
  let response: Record<string, unknown> = { strategy: 'Exact', provenance: 'Exact', rtp: 0, hitFrequency: 0, volatility: 0 };
  let status = 200;
  await page.route('**/api/evaluate/light', async route => {
    await new Promise(resolve => setTimeout(resolve, 500));
    await route.fulfill({ status, json: response });
  });
  await page.goto('/build');
  const metrics = page.getByLabel('Model evaluation');
  await expect(metrics).toContainText('Calculating');
  await expect(metrics.locator('.prov')).toHaveCount(0);
  await expect(metrics).toContainText('0.00');
  await expect(metrics.locator('.prov.exact')).toHaveCount(3);
  response = { strategy: 'Exact', provenance: 'Exact' };
  await page.getByRole('button', { name: 'Load coin example' }).click();
  await expect(metrics).toContainText('Calculating');
  await expect(metrics.locator('.prov')).toHaveCount(0);
  await expect(metrics).toContainText('Not evaluated');
  await expect(metrics).not.toContainText('0.00');
  status = 503;
  await page.getByRole('button', { name: 'Build Nine Lives graph' }).click();
  await expect(metrics).toContainText('Unavailable');
  await expect(metrics.locator('.prov')).toHaveCount(0);
});
