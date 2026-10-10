/**
 * Feature-flag E2E — GET /api/features gates every non-1.0 surface.
 *
 * Flags off: the AI, auto-tune, plugins and play entry points must all be
 * absent. Flags on (control): the default UI still renders, proving the mock
 * applies and the absence assertions above are not vacuous.
 */
import { test, expect, type Page } from './fixtures';

const ALL_OFF = { ai: false, autoTune: false, plugins: false, play: false };
const ALL_ON = { ai: true, autoTune: true, plugins: true, play: true };

async function openBuildWithFeatures(page: Page, json: Record<string, boolean>) {
  await page.route('**/api/features', route => route.fulfill({ status: 200, json }));
  const applied = page.waitForResponse(response => new URL(response.url()).pathname === '/api/features');
  await page.goto('/build');
  expect((await applied).status()).toBe(200);
}

test('disabled feature flags hide every non-1.0 surface', { tag: '@critical' }, async ({ page }) => {
  await openBuildWithFeatures(page, ALL_OFF);
  // Positive control: the workspace itself rendered (Layout + RightPanel).
  await expect(page.getByRole('tab', { name: 'Build', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'AI Generate graph' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'AI assist' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'AI', exact: true })).toHaveCount(0);
  await expect(page.getByRole('tab', { name: 'Play', exact: true })).toHaveCount(0);
  // The Plugins sub-tab only exists inside the right panel's Mechanics section.
  await page.getByRole('button', { name: 'Mechanics', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Custom Mechanics', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Plugins', exact: true })).toHaveCount(0);
  // A bookmarked /play URL must be redirected away, not just hidden from the nav.
  await page.goto('/play');
  await expect(page).toHaveURL(/\/build$/);
});

test('enabled feature flags keep the default UI visible', { tag: '@critical' }, async ({ page }) => {
  await openBuildWithFeatures(page, ALL_ON);
  await expect(page.getByRole('tab', { name: 'Play', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'AI', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'AI Generate graph' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'AI assist' })).toBeVisible();
  await page.getByRole('button', { name: 'Mechanics', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Plugins', exact: true })).toBeVisible();
  // The guard must not block /play when the flag is on.
  await page.goto('/play');
  await expect(page).toHaveURL(/\/play$/);
});
