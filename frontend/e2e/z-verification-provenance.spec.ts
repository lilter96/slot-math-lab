import { saveValidatedDialog, waitForRunLaunch, test, expect, getWithQuota } from './fixtures';

test('Retained profile and accounting verdicts require the original sample producer, even when every input and count matches', async ({ page }) => {
  test.setTimeout(240000);
  await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles('e2e/fixtures/component-accounting-model.json');
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByRole('button', { name: 'Create component accounting plan', exact: true }).click();
  await page.getByLabel('Reconciliation name').fill('Producer identity'); await page.getByLabel('Accounting observation point').selectOption('sink');
  await page.getByLabel('Accounting total value').selectOption('state:total');
  await page.getByLabel('Component 1 name').fill('X'); await page.getByLabel('Component 1 value').selectOption('state:x');
  await page.getByLabel('Component 2 name').fill('Y'); await page.getByLabel('Component 2 value').selectOption('state:y');
  await page.getByRole('button', { name: 'Add component', exact: true }).click(); await page.getByLabel('Component 3 name').fill('Z'); await page.getByLabel('Component 3 value').selectOption('state:z');
  await saveValidatedDialog(page, 'Save accounting plan');
  await page.getByRole('button', { name: 'Configure verification profile', exact: true }).click();
  await saveValidatedDialog(page, 'Save verification profile');
  await page.getByLabel('Simulation spins').fill('100');
  const created = waitForRunLaunch(page);
  await page.getByRole('button', { name: /^▶ Start run$/ }).click(); const { id } = await (await created).json();
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
  await page.getByRole('button', { name: 'Evaluate pinned verification profile', exact: true }).click();
  await expect(page.getByLabel('Verification profile result')).toHaveAttribute('data-profile-status', 'criteriaMet', { timeout: 70000 });
  const accounting = page.locator('#component-accounting'); await accounting.locator('summary').first().click();
  await accounting.getByRole('button', { name: 'Reconcile pinned components', exact: true }).click();
  await expect(accounting.getByLabel('Component reconciliation result')).toHaveAttribute('data-accounting-status', 'noObservedViolations', { timeout: 70000 });
  const stored = await (await getWithQuota(page.request, `/api/runs/${id}/evidence`)).json();
  const altered = structuredClone(stored);
  for (const artifact of altered.diagnostics) if (['verification-profile', 'component-accounting'].includes(artifact.kind))
    artifact.output.source.producer.coreBinarySha256 = '0'.repeat(64);
  await page.route(`**/api/runs/${id}/evidence`, route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(altered) }));
  await page.goto(`/results?run=${id}`);
  // Reload restores a compact live frame without terminal JSON. A rejected
  // snapshot read must not discard the complete authoritative archive result.
  let rejectedAt = 0;
  await page.route(`**/api/runs/${id}`, route => {
    if (route.request().method() !== 'GET') return route.continue();
    // Startup can already have more than one reader in flight. Reject the
    // endpoint for the entire interval; per-owner retry deadlines are covered
    // by the supervisor tests rather than inferred from concurrent requests.
    rejectedAt ||= Date.now();
    const remaining = 6000 - (Date.now() - rejectedAt);
    if (remaining > 0) return route.fulfill({ status: 429, headers: { 'Retry-After': String(Math.ceil(remaining / 1000)) }, body: '' });
    return route.continue();
  });
  const rejected = page.waitForResponse(r => new URL(r.url()).pathname === `/api/runs/${id}` && r.status() === 429);
  await page.reload(); await rejected;
  await expect(page.locator('#verification-profile').getByRole('alert')).toContainText('identity differs');
  const saved = page.locator('#component-accounting'); await saved.locator('summary').first().click();
  await expect(saved.getByRole('alert')).toContainText('identity does not match');
  await expect(page.getByLabel('Verification profile result')).toHaveCount(0); await expect(saved.getByLabel('Component reconciliation result')).toHaveCount(0);
  await page.unroute(`**/api/runs/${id}`); await page.unroute(`**/api/runs/${id}/evidence`); await page.reload();
  await expect(page.getByLabel('Verification profile result')).toHaveAttribute('data-profile-status', 'criteriaMet');
  await page.locator('#component-accounting > summary').click();
  await expect(page.getByLabel('Component reconciliation result')).toHaveAttribute('data-accounting-status', 'noObservedViolations');
});
