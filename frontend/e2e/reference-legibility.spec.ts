import { test, expect } from './fixtures';
import path from 'node:path';

test('Reference demo labels and replay/game controls meet legibility and target sizes', async ({ page }) => {
  await page.goto('/play');
  await expect(page.getByRole('button', { name: 'SPIN', exact: true })).toBeEnabled({ timeout: 30000 });
  await expect(page.locator('.nl-play > .hint')).toContainText('own authored model');
  for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
    await page.setViewportSize({ width, height });
    await page.evaluate(() => new Promise<void>(resolve => { let frames = 0; const settle = () => { if (++frames === 4) resolve(); else requestAnimationFrame(settle); }; requestAnimationFrame(settle); }));
    const sizes = await page.locator('.nl-top > span, .nl-marquee span, .nl-feature span, .nl-controls > div > span, .nl-lives small, .nl-multiplier small, .nl-buy small, .nl-bottom button').evaluateAll(elements => elements.filter(element => element.getBoundingClientRect().height > 0).map(element => parseFloat(getComputedStyle(element).fontSize)));
    expect(sizes.length).toBeGreaterThan(0); expect(Math.min(...sizes)).toBeGreaterThanOrEqual(12);
    const targets = await page.locator('.nl-graph-tools button, .nl-graph-tools input:not([type=checkbox]), .nl-game button, .nl-game a').evaluateAll(elements => elements.filter(element => element.getBoundingClientRect().height > 0).map(element => element.getBoundingClientRect().height));
    expect(Math.min(...targets)).toBeGreaterThanOrEqual(44);
    await expect(page.getByRole('link', { name: 'Choose project', exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: path.resolve('../artifacts/ui', `reference-legibility-${width}.png`), fullPage: true });
  }
});

test('Reference rules dialog has a heading name and restores keyboard focus without executing a round', async ({ page }) => {
  let rounds = 0;
  page.on('request', request => { if (request.url().endsWith('/api/play/round')) rounds++; });
  await page.goto('/play');
  const rules = page.locator('.nl-rules');
  await expect(rules).toBeEnabled({ timeout: 30000 }); await rules.focus(); await page.keyboard.press('Enter');
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible(); await expect(dialog).toHaveAccessibleName(/.+/);
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0); await expect(rules).toBeFocused();
  expect(rounds).toBe(0);
});

test('Replay and game actions stay disabled while their renderer is still loading', async ({ page }) => {
  let release!: () => void, requested = false;
  const gate = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/assets/images/nine-lives/**', async route => { requested = true; await gate; await route.continue(); });
  try {
    await page.goto('/play');
    await expect.poll(() => requested).toBe(true);
    await page.getByLabel('Replay config hash').fill('a'.repeat(64));
    await expect(page.getByRole('button', { name: 'Replay round', exact: true })).toBeDisabled();
    await expect(page.locator('.nl-rules')).toBeDisabled();
    await expect(page.locator('.nl-turbo')).toBeDisabled();
    release();
    await expect(page.locator('.nl-rules')).toBeEnabled({ timeout: 30000 });
    await expect(page.getByRole('button', { name: 'Replay round', exact: true })).toBeEnabled({ timeout: 30000 });
  } finally { release(); }
});
