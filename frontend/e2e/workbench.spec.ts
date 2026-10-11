import { test, expect } from './fixtures';
test('Overview derives Nine Lives geometry and opens source logic at every supported viewport', { tag: '@critical' }, async ({ page }) => {
  for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
    await page.setViewportSize({ width, height });
    await page.goto('/overview?project=nine-lives');
    await expect(page.getByRole('heading', { name: 'Nine Lives', exact: true })).toBeVisible();
    await expect(page.getByRole('img', { name: /6 reels by 5 rows/ })).toBeVisible();
    await expect(page.locator('.structure-board > span')).toHaveCount(30);
    await expect(page.locator('.overview-page .prov')).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  }
  await page.getByRole('button', { name: /Free spins.*Open logic/ }).click();
  await expect(page.getByTestId('model-navigation')).toContainText('free-spin');
  await page.getByRole('button', { name: 'Open full game graph' }).click();
  await expect(page.locator('.react-flow__node[data-id="round"]')).toBeVisible();
});
