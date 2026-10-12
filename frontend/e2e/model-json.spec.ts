import { test, expect, type Page } from './fixtures';
import { readFile } from 'node:fs/promises';

const model = { schemaVersion: '1.0.0', name: 'Precision retained', nodes: [{ nodeType: 'metricsSink', id: 'sink', winCap: 10, inputs: { in: { name: 'in', type: 'Wins' } } }], edges: [], initialState: { wide: 10000000000 }, metadata: { integer: 9007199254740991, text: '9223372036854775807' } };
const source = JSON.stringify(model);
const unsafe = source.replace('9007199254740991', '9007199254740993');
const file = (text: string) => ({ name: 'model.json', mimeType: 'application/json', buffer: Buffer.from(text) });
const link = (text: string) => '/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(text)).toString('base64'));
async function exported(page: Page) {
  await page.goto('/build');
  const pending = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export project', exact: true }).click();
  return JSON.parse(await readFile((await (await pending).path())!, 'utf8'));
}

test('Project-library import rejects original unsafe tokens and retains supported values in exported JSON', async ({ page }) => {
  await page.goto('/projects');
  const input = page.locator('.project-import input');
  await input.setInputFiles(file(unsafe));
  await expect(page.getByRole('alert')).toContainText('exact browser range');
  await expect(page.getByRole('heading', { name: 'Start with a game or a blank model' })).toBeVisible();
  await input.setInputFiles(file(source));
  await expect(page).toHaveURL(/overview/);
  expect(await exported(page)).toMatchObject(model);
});

test('Toolbar and Export reject unsafe imports without replacing the current model', async ({ page }) => {
  await page.goto(link(source));
  await page.getByLabel('Import project file').setInputFiles(file(unsafe));
  await expect(page.getByRole('alert')).toContainText('exact browser range');
  expect(await exported(page)).toMatchObject(model);
  await page.goto('/export');
  await page.locator('input[type=file]').setInputFiles(file(unsafe));
  await expect(page.getByText(/outside the exact browser range/)).toBeVisible();
  expect(await exported(page)).toMatchObject(model);
});

test('Legacy inbound import reports unsafe numbers and keeps the existing project', async ({ page }) => {
  await page.goto(link(source));
  await expect(page.getByRole('link', { name: 'Choose project', exact: true })).toHaveAttribute('title', model.name);
  await expect(page).not.toHaveURL(/load=/);
  await page.goto(link(unsafe));
  await expect(page.getByRole('alert')).toContainText('exact browser range');
  expect(await exported(page)).toMatchObject(model);
});

test('Saved-model opening rejects raw unsafe server JSON before creating a draft', async ({ page }) => {
  await page.route('**/api/configs', route => route.fulfill({ json: [{ id: 'wide-saved', name: 'Unsafe saved model', latestVersion: 1 }] }));
  await page.route('**/api/configs/wide-saved', route => route.fulfill({ contentType: 'application/json', body: `{"config":${unsafe}}` }));
  await page.goto(link(source));
  await expect(page.getByRole('link', { name: 'Choose project', exact: true })).toHaveAttribute('title', model.name);
  await expect(page).not.toHaveURL(/load=/);
  await page.goto('/projects');
  await page.getByRole('button', { name: 'Open saved version', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('exact browser range');
  await expect(page).toHaveURL(/projects/);
  expect(await exported(page)).toMatchObject(model);
});
