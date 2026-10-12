import { test, expect, waitForRunLaunch, calculateWithQuota } from './fixtures';
import path from 'node:path';

test('Simulation controls and evidence labels remain readable with phone-size targets', async ({ page }) => {
  await page.goto('/build');
  await page.getByRole('button', { name: 'Load coin example', exact: true }).click();
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await expect(page.getByLabel('Simulation spins')).toBeVisible();
  for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
    await page.setViewportSize({ width, height });
    const sizes = await page.locator('.run-controls label, .run-status, .simulation-next-model, .simulation-progress footer').evaluateAll(elements => elements.filter(element => element.getBoundingClientRect().height > 0).map(element => parseFloat(getComputedStyle(element).fontSize)));
    expect(sizes.length).toBeGreaterThan(0); expect(Math.min(...sizes)).toBeGreaterThanOrEqual(12);
    if (width === 390) {
      expect((await page.getByLabel('Simulation spins').boundingBox())!.height).toBeGreaterThanOrEqual(44);
      expect((await page.getByRole('button', { name: /^▶ Start run$/ }).boundingBox())!.height).toBeGreaterThanOrEqual(44);
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: path.resolve('../docs/design/qa', `evidence-legibility-simulate-${width}.png`), fullPage: true });
  }
});

test('Export exposes the complete source fingerprint inside the phone content width', async ({ page }) => {
  await page.goto('/export?project=nine-lives');
  for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
    await page.setViewportSize({ width, height });
    const hash = page.locator('.par-meta .mono');
    await expect(hash).toHaveText(/^[a-f0-9]{64}$/);
    const bounds = (await hash.boundingBox())!;
    expect(bounds.x).toBeGreaterThanOrEqual(0); expect(bounds.x + bounds.width).toBeLessThanOrEqual(width);
    expect(await page.locator('.code-block').evaluate(element => parseFloat(getComputedStyle(element).fontSize))).toBeGreaterThanOrEqual(12);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: path.resolve('../docs/design/qa', `evidence-legibility-export-${width}.png`), fullPage: true });
  }
});

test('Actual saved Results evidence has readable method/status labels at four sizes', async ({ page }) => {
  test.setTimeout(180000);
  await page.goto('/build'); await page.getByRole('button', { name: 'Load coin example', exact: true }).click();
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByLabel('Simulation spins').fill('100');
  const accepted = waitForRunLaunch(page);
  await page.getByRole('button', { name: /start run/i }).click();
  const run = await (await accepted).json();
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
  await page.getByRole('tab', { name: 'Results', exact: true }).click();
  await page.locator('.results-run-list button').filter({ hasText: run.id.slice(0, 10) }).click();
  await expect(page.locator('.results-detail')).toContainText('100 / 100 rounds');
  for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
    await page.setViewportSize({ width, height });
    await page.locator('.results-detail').scrollIntoViewIfNeeded();
    const sizes = await page.locator('.results-status, .results-source-tag, .results-run-list code').evaluateAll(elements => elements.filter(element => element.getBoundingClientRect().height > 0).map(element => parseFloat(getComputedStyle(element).fontSize)));
    expect(sizes.length).toBeGreaterThan(0); expect(Math.min(...sizes)).toBeGreaterThanOrEqual(12);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: path.resolve('../docs/design/qa', `evidence-legibility-results-${width}.png`), fullPage: true });
  }
});

test('Actual trace indices remain readable and recorded hit positions visible on phone', async ({ page }) => {
  test.setTimeout(180000);
  await page.goto('/debug?project=nine-lives');
  await page.getByLabel('Debug seed').fill('1');
  await calculateWithQuota(page, '/api/play/round', 'Run current model');
  await expect(page.getByRole('heading', { name: 'Executed round', exact: true })).toBeVisible();
  await page.locator('.trace-hit').first().click();
  await expect(page.locator('.trace-cell.hit')).toHaveCount(5);
  for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
    await page.setViewportSize({ width, height });
    await page.locator('.trace-board').scrollIntoViewIfNeeded();
    const sizes = await page.locator('.trace-cell small').evaluateAll(elements => elements.map(element => parseFloat(getComputedStyle(element).fontSize)));
    expect(sizes).toHaveLength(30); expect(Math.min(...sizes)).toBeGreaterThanOrEqual(12);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: path.resolve('../docs/design/qa', `evidence-legibility-debug-${width}.png`), fullPage: true });
  }
});
