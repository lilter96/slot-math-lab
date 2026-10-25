import { test, expect } from './fixtures';

test('No-board Overview is compact and points to actual inputs without inventing geometry', async ({ page }) => {
  await page.goto('/build');
  await page.getByRole('button', { name: 'Load coin example', exact: true }).click();
  await page.getByRole('tab', { name: 'Overview', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Model structure', exact: true })).toBeVisible();
  await expect(page.locator('.structure-board')).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Open its complete logic graph', exact: true })).toBeVisible();
  for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
    await page.setViewportSize({ width, height });
    const facts = page.locator('.model-board-region');
    expect((await facts.boundingBox())!.height).toBeLessThan(180);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    const hints = await page.locator('.overview-page .hint').evaluateAll(items => items.map(item => Number.parseFloat(getComputedStyle(item).fontSize)));
    expect(hints.every(size => size >= 12)).toBe(true);
  }
});

test('Graph keyboard dialog restores focus and labelled inspector fields remain readable', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  const legacy = { name: 'Accessible primitive model', nodes: [
    { id: 'draw', type: 'draw', position: { x: 80, y: 100 }, data: { nodeType: 'draw', label: 'Weighted draw', drawWeights: [{ outcomeId: 'zero', weight: 1, value: 0 }, { outcomeId: 'one', weight: 1, value: 1 }] } },
    { id: 'sink', type: 'sink', position: { x: 400, y: 100 }, data: { nodeType: 'sink', label: 'Payout', winCap: 10000 } },
  ], edges: [{ id: 'edge', source: 'draw', target: 'sink', sourceHandle: 'out', targetHandle: 'in' }], tables: { schemaVersion: '1.0.0' }, mechanics: [] };
  await page.goto('/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(legacy))).toString('base64')));
  const data = page.getByRole('button', { name: 'Edit project data and expressions', exact: true });
  await data.focus(); await page.keyboard.press('Enter');
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  expect(await dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
  await expect(data).toBeFocused();
  await page.getByLabel('Focus graph node').selectOption('draw');
  const inspector = page.getByRole('complementary', { name: 'Context inspector' });
  const outcome = inspector.getByLabel('Outcome ID 1', { exact: true });
  await expect(outcome).toBeVisible();
  expect(await outcome.evaluate(element => Number.parseFloat(getComputedStyle(element).fontSize))).toBeGreaterThanOrEqual(12);
  const id = await outcome.inputValue();
  await expect(inspector.getByLabel(`Weight for ${id}`, { exact: true })).toBeVisible();
  await expect(inspector.getByLabel(`Value for ${id}`, { exact: true })).toBeVisible();
  await expect(inspector.getByRole('button', { name: `Remove outcome ${id}`, exact: true })).toBeVisible();
  await inspector.getByRole('button', { name: 'Inspector', exact: true }).click();
  const hint = inspector.getByText('Use Add nodes to extend the graph', { exact: true });
  await expect(hint).toBeVisible();
  expect(await hint.evaluate(element => Number.parseFloat(getComputedStyle(element).fontSize))).toBeGreaterThanOrEqual(12);
  const durations = await data.evaluate(element => ({ animation: getComputedStyle(element).animationDuration, transition: getComputedStyle(element).transitionDuration }));
  expect(durations.transition.split(',').every(value => Number.parseFloat(value) <= .01)).toBe(true);
});
