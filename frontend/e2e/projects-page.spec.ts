import { readFile } from 'node:fs/promises';
import { test, expect } from './fixtures';
const savedModel = { schemaVersion: '1.0.0', name: 'Saved payout model', nodes: [{ id: 'sink', nodeType: 'metricsSink', label: 'Payout', winCap: 10, inputs: { in: { name: 'in', type: 'Wins' } } }], edges: [] };
async function review(page: import('./fixtures').Page, state: string) {
  for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
    await page.setViewportSize({ width, height });
    for (const [name, selector] of [['heading', '.projects-heading'], ['library', '.projects-library'], ['import', '.project-import'], ['storage', '.projects-storage'], ['templates', '.project-templates'], ['server', '.projects-server'], ['footer', '.projects-footer']]) {
      await page.locator(selector).scrollIntoViewIfNeeded();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: `../artifacts/ui/projects-page-${test.info().project.name}-${state}-${name}-${width}.png` });
    }
    if (width === 390) for (const [index, card] of (await page.locator('.template-row').all()).entries()) {
      await card.scrollIntoViewIfNeeded(); await page.screenshot({ path: `../artifacts/ui/projects-template-${test.info().project.name}-${index}.png` });
    }
  }
}
test('Private library supports inline rename, independent duplicate, search, saved versions and full-page layout', async ({ page }) => {
  await page.route('**/api/configs', route => route.fulfill({ json: [{ id: 'saved-model', name: savedModel.name, latestVersion: 2 }] }));
  await page.route('**/api/configs/saved-model', route => route.fulfill({ json: { config: savedModel } }));
  await page.goto('/projects'); await page.getByRole('button', { name: 'New blank project', exact: true }).click();
  await page.getByRole('tab', { name: 'My projects', exact: true }).click();
  const library = page.getByLabel('Personal projects');
  await library.getByRole('button', { name: 'Rename', exact: true }).click();
  await page.getByLabel('Project name', { exact: true }).fill('Night shift · payout experiment');
  await page.getByRole('button', { name: 'Save name', exact: true }).click();
  await expect(library.getByRole('heading', { name: 'Night shift · payout experiment', exact: true })).toBeVisible();
  await library.getByRole('button', { name: 'Duplicate', exact: true }).click();
  await page.getByRole('tab', { name: 'My projects', exact: true }).click();
  await expect(library.locator('.project-row')).toHaveCount(2);
  await library.locator('.project-row').first().getByRole('button', { name: 'Rename', exact: true }).click();
  await page.getByLabel('Project name', { exact: true }).fill('Discard this rename'); await page.keyboard.press('Escape');
  await expect(library.getByRole('heading', { name: 'Night shift · payout experiment', exact: true })).toBeVisible();
  await page.getByLabel('Find a project', { exact: true }).fill('no-such-model');
  await expect(page.getByRole('heading', { name: 'No matching projects', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Clear search', exact: true }).click();
  await review(page, 'populated');
  await page.getByRole('button', { name: 'Open saved version', exact: true }).click();
  await expect(page).toHaveURL(/overview/); await expect(page.getByRole('heading', { name: savedModel.name, exact: true, level: 1 })).toBeVisible();
  await page.getByRole('tab', { name: 'My projects', exact: true }).click(); await expect(library.locator('.project-row')).toHaveCount(3);
});
test('Empty library and server failure preserve import recovery with clear local errors', async ({ page }) => {
  await page.route('**/api/configs', route => route.fulfill({ status: 503, json: { error: 'Unavailable' } }));
  await page.goto('/projects'); await expect(page.getByRole('button', { name: 'Retry saved projects', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Start with a game or a blank model', exact: true })).toBeVisible();
  await page.locator('.project-import input').setInputFiles({ name: 'bad.json', mimeType: 'application/json', buffer: Buffer.from('{"nodes":[]}') });
  await expect(page.locator('.project-import').getByRole('alert')).toContainText('Choose a graph JSON');
  await review(page, 'empty-error');
});

 test('An incomplete editable project can be duplicated without losing its original source', async ({ page }) => {
  await page.route('**/api/configs', route => route.fulfill({ json: [] }));
  const draft = { format: 'slotmath-editable-draft-v1', configName: 'Broken draft', nodes: [{ id: 'draw', type: 'draw', position: { x: 0, y: 0 }, data: { nodeType: 'draw', label: 'Draw', weightExpressionId: 'state.', drawWeights: [{ outcomeId: 'one', weight: 1, value: 1 }] } }], edges: [], tables: { schemaVersion: '1.0.0', untouched: { keep: true } }, graphTrail: [] };
  await page.goto('/projects');
  await page.locator('.project-import input').setInputFiles({ name: 'draft.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(draft)) });
  await expect(page.getByRole('alert')).toContainText('Model cannot be serialized');
  await page.getByRole('tab', { name: 'My projects', exact: true }).click();
  await page.getByLabel('Personal projects').getByRole('button', { name: 'Duplicate', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Model cannot be serialized');
  const pending = page.waitForEvent('download'); await page.getByRole('button', { name: 'Download editable draft', exact: true }).click();
  const restored = JSON.parse(await readFile((await (await pending).path())!, 'utf8'));
  expect(restored.nodes[0].data.weightExpressionId).toBe('state.'); expect(restored.tables.untouched).toEqual({ keep: true }); expect(restored.configName).toBe('Broken draft · copy');
  await page.getByRole('tab', { name: 'My projects', exact: true }).click();
  const rows = page.getByLabel('Personal projects').locator('.project-row'); await expect(rows).toHaveCount(2);
  await rows.filter({ has: page.getByRole('heading', { name: 'Broken draft', exact: true }) }).getByRole('button', { name: 'Open project', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Model cannot be serialized');
 });
