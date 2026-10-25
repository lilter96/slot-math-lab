import { test, expect } from './fixtures';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
const model = { schemaVersion: '1.0.0', name: 'Raw numbers fixture', nodes: [{ id: 'sink', nodeType: 'metricsSink', winCap: 10 }], edges: [], initialState: { counter: 7, weights: [2, 4, 8] }, unknown: { retained: '9223372036854775807' } };
const link = '/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(model))).toString('base64'));
async function open(page: import('./fixtures').Page) {
  await page.goto(link); await page.getByRole('button', { name: 'Edit project data and expressions', exact: true }).click();
  return page.getByRole('dialog', { name: 'Project data & expressions' });
}
async function exported(page: import('./fixtures').Page) {
  await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
  const pending = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export project', exact: true }).click();
  return JSON.parse(await readFile((await (await pending).path())!, 'utf8'));
}
test('Raw scalar text rejects rounded boundaries and incomplete drafts, then exports a supported long value', async ({ page }) => {
  const dialog = await open(page), field = dialog.getByLabel('State field value', { exact: true });
  await field.fill('9007199254740991.1');
  await expect(field).toHaveValue('9007199254740991.1'); await expect(field).toHaveAttribute('aria-invalid', 'true');
  await expect(dialog.getByRole('alert')).toContainText('exact browser range');
  await field.fill(''); await expect(dialog.getByRole('alert')).toContainText('complete finite number');
  await field.pressSequentially('-1e10');
  await expect(field).toBeFocused(); await expect(field).toHaveValue('-1e10');
  await expect(dialog.getByRole('alert')).toHaveCount(0);
  expect(await exported(page)).toMatchObject({ ...model, initialState: { ...model.initialState, counter: -10000000000 } });
});
test('Numeric rows retain their source on invalid input and export supported decimal edits without changing strings', async ({ page }) => {
  const dialog = await open(page); await dialog.getByLabel('Data table', { exact: true }).selectOption('weights');
  const row = dialog.getByLabel('Data row 2', { exact: true });
  await row.fill('9007199254740993'); await expect(dialog.getByRole('alert')).toContainText('exact browser range');
  for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
    await page.setViewportSize({ width, height });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: path.resolve('../artifacts/ui', `raw-number-${test.info().project.name}-${width}.png`) });
  }
  const rejected = await exported(page); expect(rejected).toMatchObject(model);
  await page.getByRole('button', { name: 'Edit project data and expressions', exact: true }).click();
  await page.getByLabel('Data table', { exact: true }).selectOption('weights');
  await page.getByLabel('Data row 2', { exact: true }).fill('.05');
  expect(await exported(page)).toMatchObject({ ...model, initialState: { ...model.initialState, weights: [2, 0.05, 8] } });
});
