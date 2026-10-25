import { test, expect, type Page } from './fixtures';
import { readFile } from 'node:fs/promises';

async function exportModel(page: Page) {
  const saved = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export project', exact: true }).click();
  return JSON.parse(await readFile((await (await saved).path())!, 'utf8'));
}

test('Visible-node rendering retains the full model, offscreen search and mechanic camera', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/overview?project=nine-lives');
  await page.getByRole('button', { name: /Free spins.*Open logic/ }).click();
  const picker = page.getByLabel('Focus graph node');
  await expect(picker.locator('option')).toHaveCount(85);
  const ids = await picker.locator('option').evaluateAll(options => options.map(option => (option as HTMLOptionElement).value).filter(Boolean));
  const original = await exportModel(page);
  expect(original.mechanics['free-spin'].nodes).toHaveLength(84);
  await picker.selectOption(ids[0]);
  await expect(page.locator(`.react-flow__node[data-id="${ids[0]}"]`)).toBeVisible();
  await page.getByRole('button', { name: 'Close inspector' }).click();
  await expect.poll(() => page.locator('.react-flow__node').count()).toBeLessThan(84);
  await expect(page.locator(`.react-flow__node[data-id="${ids.at(-1)}"]`)).toHaveCount(0);
  await picker.selectOption(ids.at(-1)!);
  await expect(page.locator(`.react-flow__node[data-id="${ids.at(-1)}"]`)).toBeVisible();
  await page.getByRole('button', { name: 'Close inspector' }).click();
  await page.getByRole('button', { name: 'Fit selection', exact: true }).click();
  await expect.poll(() => page.locator('.react-flow__node').count()).toBeLessThan(84);
  expect(await exportModel(page)).toEqual(original);
  const camera = await page.locator('.react-flow__viewport').getAttribute('style');
  await page.getByRole('button', { name: 'Open full game graph', exact: true }).click();
  await expect(picker.locator('option')).toHaveCount(5);
  await page.getByLabel('Find subgraph').fill('free-spin');
  await page.getByRole('button', { name: 'Open free-spin graph', exact: true }).click();
  await expect(picker.locator('option')).toHaveCount(85);
  await expect(page.locator('.react-flow__viewport')).toHaveAttribute('style', camera!);
  await expect(page.locator(`.react-flow__node[data-id="${ids.at(-1)}"]`)).toBeVisible();
  await page.getByRole('button', { name: 'Reset view', exact: true }).click();
  await expect(page.locator('.react-flow__node')).toHaveCount(84);
});
