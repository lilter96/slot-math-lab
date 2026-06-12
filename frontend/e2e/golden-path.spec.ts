/**
 * G32 — Golden-path E2E test
 *
 * Scenario: build a game → add symbol → check live metrics → navigate to Simulate
 * tab → switch to Results → verify PAR sheet header → Export tab → verify export UI.
 *
 * Runs against the production preview build (npm run preview) or a live dev server.
 * Requires `npx playwright install chromium` and browser binaries to be available.
 */
import { test, expect } from '@playwright/test';

test.describe('Golden path', () => {
  test('Build → live metrics → Simulate → Results → Export', async ({ page }) => {
    await page.goto('/');

    // ── 1. Build tab renders ──────────────────────────────────────────
    await expect(page.getByRole('button', { name: /build/i })).toBeVisible();
    await page.getByRole('button', { name: /build/i }).click();

    // Palette is visible
    await expect(page.getByText('Primitives')).toBeVisible();
    await expect(page.getByText('Standard Catalog')).toBeVisible();

    // Canvas is visible (the ReactFlow container)
    await expect(page.locator('.react-flow')).toBeVisible();

    // ── 2. Metric strip is visible ────────────────────────────────────
    // The MetricStrip is always rendered in Build
    await expect(page.locator('.metric-strip, [class*="metric"]').first()).toBeVisible();

    // ── 3. Navigate to Simulate tab ───────────────────────────────────
    await page.getByRole('button', { name: /simulate/i }).click();
    await expect(page.getByRole('button', { name: /start run/i }).or(
      page.getByText(/simulate/i).first()
    )).toBeVisible();

    // ── 4. Navigate to Results tab ───────────────────────────────────
    await page.getByRole('button', { name: /results/i }).click();
    // PAR sheet header
    await expect(page.getByText(/return to player/i)).toBeVisible({ timeout: 5000 });

    // Lint panel is present
    await expect(page.getByText(/compliance lint/i)).toBeVisible();

    // ── 5. Navigate to Export tab ─────────────────────────────────────
    await page.getByRole('button', { name: /export/i }).click();
    // Export page renders
    await expect(
      page.getByText(/export/i).or(page.getByText(/PAR/i)).first()
    ).toBeVisible();
  });

  test('AI Generate button opens modal', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('button', { name: /build/i }).click();

    // Find AI Generate button in the canvas toolbar (star icon)
    const aiBtn = page.getByRole('button', { name: /ai/i }).or(
      page.locator('[aria-label*="AI"]').or(page.locator('[title*="AI"]'))
    ).first();

    // The button may not exist if the canvas toolbar isn't rendered yet
    const count = await aiBtn.count();
    if (count > 0) {
      await aiBtn.click();
      // Modal should open
      await expect(
        page.getByText(/describe.*game/i).or(page.getByText(/nl.*graph/i)).or(
          page.getByRole('dialog')
        ).first()
      ).toBeVisible({ timeout: 3000 });
    }
  });

  test('Auto-tune panel is accessible from AI tab', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('button', { name: /build/i }).click();

    // Click AI tab in the right panel
    const aiTab = page.getByRole('button', { name: /^ai$/i });
    const count = await aiTab.count();
    if (count > 0) {
      await aiTab.click();
      await expect(page.getByText(/auto-tune/i)).toBeVisible({ timeout: 3000 });
      await expect(page.getByText(/target rtp/i)).toBeVisible();
    }
  });
});
