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
test('Graph selection opens a contextual inspector and closing preserves the selected model', { tag: '@critical' }, async ({ page }) => {
  await page.goto('/build?project=nine-lives');
  await expect(page.getByRole('complementary', { name: 'Context inspector' })).toHaveCount(0);
  await page.getByLabel('Focus graph node').selectOption('round');
  await expect(page.getByRole('complementary', { name: 'Context inspector' })).toBeVisible();
  await page.getByRole('button', { name: 'Close inspector' }).click();
  await expect(page.getByRole('complementary', { name: 'Context inspector' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Show inspector' }).click();
  await expect(page.getByLabel('Subgraph name')).toHaveValue('round-entry');
  await page.keyboard.press('Escape');
  await expect(page.getByRole('complementary', { name: 'Context inspector' })).toHaveCount(0);
  const canvas = await page.locator('.canvas-wrap').boundingBox();
  expect(canvas!.width).toBeGreaterThan(800);
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByText('Graph movement and connections are read only on this screen.', { exact: false })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});
