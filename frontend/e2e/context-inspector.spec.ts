import { test, expect } from './fixtures';

test('Pinned inspector survives deselection, reload and project switching without retaining another project node', async ({ page }) => {
  await page.goto('/build?project=nine-lives');
  const inspector = page.getByRole('complementary', { name: 'Context inspector' });
  await expect(inspector).toHaveCount(0);
  await page.getByLabel('Focus graph node').selectOption('round');
  await expect(page.getByLabel('Subgraph name')).toHaveValue('round-entry');
  await inspector.getByRole('button', { name: 'Pin inspector', exact: true }).click();
  await expect(inspector.getByRole('button', { name: 'Unpin inspector' })).toHaveAttribute('aria-pressed', 'true');
  await inspector.getByRole('button', { name: 'Inspector', exact: true }).click();
  await expect(inspector.getByText('Select a node to inspect', { exact: true })).toBeVisible();
  await page.reload();
  await expect(inspector.getByRole('button', { name: 'Unpin inspector' })).toBeVisible();
  await expect(inspector.getByText('Select a node to inspect', { exact: true })).toBeVisible();
  await page.getByRole('tab', { name: 'My projects', exact: true }).click();
  await page.getByRole('button', { name: 'Create Ancient Dragon project', exact: true }).click();
  await page.getByRole('tab', { name: 'Build', exact: true }).click();
  await expect(inspector.getByRole('button', { name: 'Unpin inspector' })).toBeVisible();
  await expect(page.getByLabel('Subgraph name')).toHaveCount(0);
  await expect(inspector.getByText('Select a node to inspect', { exact: true })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await inspector.getByRole('button', { name: 'Unpin inspector' }).click();
  await expect(inspector).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Show inspector' })).toBeFocused();
  await page.getByRole('button', { name: 'Show inspector' }).click();
  await inspector.getByRole('button', { name: 'Pin inspector', exact: true }).click();
  await page.keyboard.press('Escape');
  await expect(inspector).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Show inspector' })).toBeFocused();
  await page.reload();
  await expect(inspector).toHaveCount(0);
});

test('Inspector pin preference belongs only to the verified user and Close clears the returning owner preference', async ({ page, context }) => {
  const alice = `pin-alice-${Date.now()}`, bob = `pin-bob-${Date.now()}`;
  const signIn = async (userId: string) => {
    const response = await context.request.post('/api/auth/token', { data: { userId } });
    expect(response.ok()).toBe(true);
    await page.goto('/build?project=nine-lives');
  };
  const inspector = page.getByRole('complementary', { name: 'Context inspector' });
  await signIn(alice);
  await page.getByRole('button', { name: 'Show inspector' }).click();
  await inspector.getByRole('button', { name: 'Pin inspector', exact: true }).click();
  await signIn(bob);
  await expect(inspector).toHaveCount(0);
  await signIn(alice);
  await expect(inspector.getByRole('button', { name: 'Unpin inspector' })).toBeVisible();
  await inspector.getByRole('button', { name: 'Close inspector' }).click();
  await expect(page.getByRole('button', { name: 'Show inspector' })).toBeFocused();
  await page.reload();
  await expect(inspector).toHaveCount(0);
});

test('Legacy frontend import retains explicit pinning and clears the previously inspected node', async ({ page }) => {
  await page.goto('/build?project=nine-lives');
  await page.getByLabel('Focus graph node').selectOption('round');
  const inspector = page.getByRole('complementary', { name: 'Context inspector' });
  await inspector.getByRole('button', { name: 'Pin inspector', exact: true }).click();
  const legacy = { name: 'Legacy personal draft', nodes: [{ id: 'legacy-sink', type: 'sink', position: { x: 80, y: 100 }, data: { nodeType: 'sink', label: 'Legacy payout', winCap: 10000 } }], edges: [], tables: { id: 'legacy-personal-model', schemaVersion: '1.0.0' }, mechanics: [] };
  const encoded = Buffer.from(encodeURIComponent(JSON.stringify(legacy))).toString('base64');
  await page.goto('/build?load=' + encodeURIComponent(encoded));
  await expect(inspector.getByRole('button', { name: 'Unpin inspector' })).toBeVisible();
  await expect(inspector.getByText('Select a node to inspect', { exact: true })).toBeVisible();
  await expect(page.getByTestId('model-navigation')).toContainText('Legacy personal draft');
  await expect(page.getByLabel('Subgraph name')).toHaveCount(0);
  await expect(page.locator('.react-flow__node')).toHaveCount(1);
});
