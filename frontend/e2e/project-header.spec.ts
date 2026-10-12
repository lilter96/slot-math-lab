import { test, expect } from './fixtures';
import path from 'node:path';

const name = 'Personal model with a deliberately long authored name for several independent game projects';
const model = { schemaVersion: '1.0.0', name, initialState: { win: 1 }, nodes: [
  { id: 'sink', nodeType: 'metricsSink', winCap: 10, winStateKey: 'win', inputs: { in: { name: 'in', type: 'State' } } },
], edges: [] };

test('Project switcher remains visible after scrolling and opens the private library on phone', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/build');
  await page.getByLabel('Import project file').setInputFiles({ name: 'personal.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(model)) });
  const project = page.getByRole('link', { name: 'Choose project', exact: true });
  await expect(project).toBeVisible(); await expect(project).toHaveAttribute('title', name);
  await expect(project).toHaveAttribute('aria-description', `Current project: ${name}`);
  expect((await project.boundingBox())!.height).toBeGreaterThanOrEqual(44);
  await page.getByRole('tab', { name: 'Export', exact: true }).click();
  await page.locator('main').evaluate(element => { element.scrollTop = element.scrollHeight; });
  await expect(project).toBeInViewport();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await project.click();
  await expect(page.getByRole('heading', { name: 'My projects', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'New blank project', exact: true }).click();
  await expect(project).toContainText('Untitled model');
});

test('Signed-in header fits long project names and all actions at requested sizes', async ({ page }) => {
  await page.route('**/api/auth/status', route => route.fulfill({ json: { required: true, authenticated: true, userId: 'header-owner' } }));
  await page.goto('/build');
  await page.getByLabel('Import project file').setInputFiles({ name: 'personal.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(model)) });
  for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
    await page.setViewportSize({ width, height });
    const project = page.getByRole('link', { name: 'Choose project', exact: true });
    await expect(project).toBeVisible();
    await expect(page.getByRole('button', { name: 'Sign out', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Appearance settings', exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: path.resolve('../docs/design/qa', `project-header-${width}.png`), fullPage: true });
  }
});

test('Empty phone workspace has an accessible project entry before selecting any game', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/projects');
  const project = page.getByRole('link', { name: 'Choose project', exact: true });
  await expect(project).toBeVisible(); await expect(project).toContainText('Choose a project');
  await expect(project).toHaveAttribute('aria-description', 'No project selected');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});
