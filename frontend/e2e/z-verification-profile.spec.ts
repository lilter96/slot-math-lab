import { waitForRunLaunch, test, expect, getWithQuota } from './fixtures';
import type { Page } from '@playwright/test';

async function metric(page: Page, id: string) {
  await page.getByRole('button', { name: '＋ Track metric', exact: true }).click();
  await page.getByLabel('Metric name').fill(`Mean ${id.toUpperCase()}`); await page.getByLabel('Metric observation level').selectOption('node'); await page.getByLabel('Metric graph node').selectOption('sink');
  await page.getByLabel('Metric numeric expression').fill(`state.${id}`); await page.getByLabel('Enable advanced measurement').check();
  await page.getByText('Uncertainty, precision and reference checks', { exact: true }).click();
  await page.getByLabel('Independent measurement subjects').check(); await page.getByLabel('Reference mean', { exact: true }).fill('1'); await page.getByLabel('Acceptance tolerance').fill('.2');
  await page.getByRole('button', { name: 'Save measurement', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
}
async function declare(page: Page) {
  await page.locator('#metric-catalogue > summary').click(); await page.getByLabel('Search metric catalogue').fill('inference.sufficiency');
  await page.locator('.metric-catalogue-list').getByRole('button').click(); await page.getByRole('button', { name: 'Configure verification profile →', exact: true }).click();
  await page.getByLabel('Verification profile name').fill('Required X and Y'); await page.getByLabel('Required check 1', { exact: true }).selectOption('mean-equivalence'); await page.getByLabel('Required minimum 1').fill('1000');
  await page.getByRole('button', { name: 'Add required check', exact: true }).click(); await page.getByLabel('Required measurement 2').selectOption({ label: 'Mean Y' });
  await page.getByLabel('Required check 2', { exact: true }).selectOption('mean-equivalence'); await page.getByLabel('Required minimum 2').fill('1000');
}
test('Native predeclared mean family rejects underallocation, retains final criteria and replays the original profile in the reference engine', async ({ page }) => {
  test.setTimeout(240000);
  await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles('e2e/fixtures/component-accounting-model.json'); await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await metric(page, 'x'); await metric(page, 'y'); await declare(page);
  await page.getByLabel('Allocate verification family budget').uncheck(); await page.getByRole('button', { name: 'Save verification profile', exact: true }).click();
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('shared family error budget');
  await page.getByLabel('Allocate verification family budget').check(); await page.getByRole('button', { name: 'Save verification profile', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
  await page.getByLabel('Simulation spins').fill('2000'); const launch = waitForRunLaunch(page);
  await page.getByRole('button', { name: /^▶ Start run$/ }).click(); const run = await (await launch).json();
  expect(run.verificationProfile.criteria).toHaveLength(2); expect(run.measurements.every((m: { options: { errorFamilySize: number } }) => m.options.errorFamilySize === 2)).toBe(true);
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
  await page.getByRole('button', { name: 'Evaluate pinned verification profile', exact: true }).click(); await expect(page.getByLabel('Verification profile result')).toHaveAttribute('data-profile-status', 'criteriaMet', { timeout: 70000 });
  // Launch again in the mounted route. Report components need distinct keys;
  // a route reload would hide stale/duplicated children after reconciliation.
  await page.locator('#execution-configuration').evaluate((el: HTMLDetailsElement) => { el.open = true; }); await page.getByLabel('Sampling engine', { exact: true }).selectOption('reference');
  const next = waitForRunLaunch(page);
  await page.getByRole('button', { name: /^▶ Start new run$/ }).click(); const consecutive = await (await next).json();
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 }); await expect(page.locator('#verification-profile')).toHaveCount(1); await expect(page.locator('#component-accounting')).toHaveCount(1);
  await expect(page.getByRole('button', { name: 'Evaluate pinned verification profile', exact: true })).toHaveCount(1);
  expect(consecutive.verificationProfileHash).toBe(run.verificationProfileHash);
  await page.getByRole('button', { name: 'Configure verification profile', exact: true }).click(); await page.getByLabel('Required minimum 1').fill('10000000');
  await page.getByRole('button', { name: 'Save verification profile', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
  await expect(page.locator('#verification-profile')).toContainText('1,000'); await expect(page.locator('#verification-profile')).not.toContainText('10,000,000');
  await page.goto(`/results?run=${run.id}`); await page.evaluate(() => localStorage.clear()); await page.reload();
  await expect(page.getByLabel('Verification profile result')).toHaveAttribute('data-profile-status', 'criteriaMet'); await expect(page.locator('#verification-profile')).toContainText('Retained server profile restored');
  await page.setViewportSize({ width: 390, height: 844 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await page.setViewportSize({ width: 1280, height: 720 }); await page.getByRole('button', { name: 'Replay pinned run', exact: true }).click(); await page.getByLabel('Pinned run engine').selectOption('reference');
  const replay = waitForRunLaunch(page);
  await page.getByRole('button', { name: 'Start pinned run', exact: true }).click(); const repeated = await (await replay).json();
  expect(repeated.verificationProfileHash).toBe(run.verificationProfileHash); expect(repeated.verificationProfile).toEqual(run.verificationProfile);
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 }); await page.getByRole('button', { name: 'Evaluate pinned verification profile', exact: true }).click();
  await expect(page.getByLabel('Verification profile result')).toHaveAttribute('data-profile-status', 'criteriaMet', { timeout: 70000 });
  const evidence = await (await getWithQuota(page.request, `/api/runs/${run.id}/evidence`)).json(); const other = await (await getWithQuota(page.request, `/api/runs/${repeated.id}/evidence`)).json();
  expect(other.run.progress.measurements).toEqual(evidence.run.progress.measurements); expect(other.diagnostics[0].output.report).toEqual(evidence.diagnostics[0].output.report);
});

test('Cancelling profile validation cannot save late criteria or change collection interval budgets', async ({ page }) => {
  await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles('e2e/fixtures/component-accounting-model.json'); await page.getByRole('tab', { name: 'Simulate', exact: true }).click(); await metric(page, 'x');
  let release!: () => void, accepted!: () => void, settled!: () => void;
  const gate = new Promise<void>(r => { release = r; }), started = new Promise<void>(r => { accepted = r; }), handled = new Promise<void>(r => { settled = r; });
  await page.route('**/api/runs/measurements/schema', async route => {
    if (!route.request().postDataJSON().verificationProfile) return route.continue();
    accepted(); await gate; try { await route.fulfill({ status: 200, contentType: 'application/json', body: '{"points":[],"fields":[]}' }); } catch { /* Dismissal aborts validation. */ } finally { settled(); }
  });
  await page.getByRole('button', { name: 'Configure verification profile', exact: true }).click(); await page.getByLabel('Verification family confidence').fill('.99'); await page.getByLabel('Required check 1', { exact: true }).selectOption('mean-equivalence');
  await page.getByRole('button', { name: 'Save verification profile', exact: true }).click(); await started; await page.getByLabel('Close verification editor').click(); release(); await handled; await page.reload();
  const saved = await page.evaluate(() => JSON.parse(localStorage.getItem('slotmath-measurements-v1')!)); expect(saved.verificationProfile ?? null).toBeNull(); expect(saved.metrics[0].options.confidence).toBe(.95);
});

test('Numeric nonzero-event probability cannot pass a bounded numeric-mean reference through the native editor', async ({ page }) => {
  test.setTimeout(180000);
  await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles('e2e/fixtures/component-accounting-model.json'); await page.getByRole('tab', { name: 'Simulate', exact: true }).click(); await metric(page, 'x');
  await page.locator('.measurement-plan > summary').click(); await page.locator('.measurement-plan').getByRole('button', { name: 'Edit', exact: true }).click();
  await page.getByText('Uncertainty, precision and reference checks', { exact: true }).click(); await page.getByLabel('Reference statistic', { exact: true }).selectOption('probability'); await page.getByLabel('Proven minimum', { exact: true }).fill('0'); await page.getByLabel('Proven maximum', { exact: true }).fill('2');
  await page.getByRole('button', { name: 'Save measurement', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
  await page.getByRole('button', { name: 'Configure verification profile', exact: true }).click(); await page.getByLabel('Required check 1', { exact: true }).selectOption('mean-equivalence');
  await page.getByRole('button', { name: 'Save verification profile', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
  await page.getByLabel('Simulation spins').fill('2000'); const launch = waitForRunLaunch(page);
  await page.getByRole('button', { name: /^▶ Start run$/ }).click(); const { id } = await (await launch).json(); await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
  await page.getByRole('button', { name: 'Evaluate pinned verification profile', exact: true }).click(); const result = page.getByLabel('Verification profile result');
  await expect(result).toHaveAttribute('data-profile-status', 'discrepancy', { timeout: 70000 }); await expect(result).toContainText('Clopper–Pearson');
  const evidence = await (await getWithQuota(page.request, `/api/runs/${id}/evidence`)).json(); const snapshot = evidence.run.progress.measurements[0];
  expect(snapshot.mean).toBeGreaterThan(.9); expect(snapshot.analysis.checks.find((c: { id: string }) => c.id === 'mean-equivalence').observed).toBeLessThan(.6);
});
