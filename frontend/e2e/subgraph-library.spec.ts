import { test, expect } from './fixtures';

test('Complete migrated game models open directly in Build and preserve the previous draft', async ({ page }) => {
  await page.goto('/build');
  await page.getByRole('button', { name: 'Load coin example', exact: true }).click();
  const library = page.getByTestId('starter-library');
  await library.locator('summary').click();
  for (const name of ['Nine Lives', 'Ancient Dragon', 'Woodland Whisper', 'X7 Club', 'Le Militare']) {
    await library.getByRole('button', { name: `Open ${name} model`, exact: true }).click();
    await expect(page.getByTestId('model-navigation')).toContainText(name);
    await expect(page.getByRole('button', { name: 'Open free-spin graph', exact: true }).or(page.getByRole('button', { name: 'Open hold-and-spin graph', exact: true }))).toBeVisible();
  }
  await library.getByRole('button', { name: 'Restore my model', exact: true }).click();
  await expect(page.locator('.react-flow__node[data-id="coin-draw"]')).toBeVisible();
});

test('Ready subgraphs are editable, reusable and preserve the previous model', async ({ page }) => {
  await page.goto('/build');
  await page.getByRole('button', { name: 'Load coin example', exact: true }).click();
  const library = page.getByTestId('starter-library');
  await library.locator('summary').click();
  await library.getByRole('button', { name: 'Inspect Cluster pays subgraph', exact: true }).click();
  await expect(page.getByTestId('model-navigation')).toContainText('cluster-pays');
  await page.getByLabel('Focus graph node').selectOption('candidate-mode');
  await expect(page.locator('.react-flow__node[data-id="candidate-mode"]')).toBeVisible();
  await page.getByRole('button', { name: 'Reuse this subgraph', exact: true }).click();
  await expect(page.getByLabel('Subgraph name')).toHaveValue('cluster-pays');
  await expect(page.getByRole('button', { name: 'Open full game graph', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Open cluster-candidates graph', exact: true }).click();
  await page.getByLabel('Focus graph node').selectOption('candidates');
  await expect(page.locator('.react-flow__node[data-id="candidates"]')).toBeVisible();
  await library.getByRole('button', { name: 'Restore my model', exact: true }).click();
  await expect(page.locator('.react-flow__node[data-id="coin-draw"]')).toBeVisible();
  await expect(page.locator('.react-flow__node[data-id="draw"]')).toHaveCount(0);
  await expect(library.getByRole('button', { name: 'Restore my model', exact: true })).toHaveCount(0);
});

test('Cascade starter exposes shared evaluation, refill and award subgraphs', async ({ page }) => {
  await page.goto('/build');
  const library = page.getByTestId('starter-library');
  await library.locator('summary').click();
  await library.getByRole('button', { name: 'Open Cascading clusters example', exact: true }).click();
  for (const name of ['cluster-pays', 'cluster-candidates', 'tumble', 'award-step', 'cascade']) {
    await expect(page.getByRole('button', { name: `Open ${name} graph`, exact: true })).toBeVisible();
  }
  await page.getByRole('button', { name: 'Open cascade graph', exact: true }).click();
  await expect(page.getByLabel('Focus graph node')).toContainText('step');
  await page.getByRole('button', { name: 'Open full game graph', exact: true }).click();
  await page.getByRole('button', { name: 'Open model data tables', exact: true }).click();
  await expect(page.getByLabel('Data table', { exact: true })).toBeVisible();
});

test('An emptied subgraph remains editable and can return to its parent', async ({ page }) => {
  await page.goto('/build');
  const library = page.getByTestId('starter-library');
  await library.locator('summary').click();
  await library.getByRole('button', { name: 'Inspect Cluster pays subgraph', exact: true }).click();
  const nodes = page.locator('.react-flow__node');
  while (await nodes.count()) {
    const id = await nodes.first().getAttribute('data-id');
    await page.getByLabel('Focus graph node').selectOption(id!);
    await page.getByRole('button', { name: 'Delete node', exact: true }).click();
  }
  await page.getByRole('button', { name: 'Open full game graph', exact: true }).click();
  await expect(page.locator('.react-flow__node[data-id="draw"]')).toBeVisible();
  await page.getByRole('button', { name: 'Open cluster-pays graph', exact: true }).click();
  await expect(nodes).toHaveCount(0);
  await page.getByRole('button', { name: 'Open full game graph', exact: true }).click();
  await expect(page.locator('.react-flow__node[data-id="draw"]')).toBeVisible();
});

test('Own atoms can be extracted into a definition and reused', async ({ page }) => {
  await page.goto('/build');
  const library = page.getByTestId('starter-library');
  await library.locator('summary').click();
  await library.getByRole('button', { name: 'Open Cluster pays example', exact: true }).click();
  await page.getByLabel('Focus graph node').selectOption('draw');
  await page.getByLabel('Name for extraction').fill('my-board');
  await page.getByRole('button', { name: 'Extract selection', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Open my-board graph', exact: true })).toBeVisible();
  await expect(page.locator('.react-flow__node[data-id="draw"]')).toHaveCount(0);
  await page.getByRole('button', { name: 'Open my-board graph', exact: true }).click();
  await expect(page.locator('.react-flow__node[data-id="draw"]')).toBeVisible();
  await page.getByRole('button', { name: 'Reuse this subgraph', exact: true }).click();
  await expect(page.getByLabel('Subgraph name')).toHaveValue('my-board');
});

test('Mechanics panel uses the same editable definitions as the canvas', async ({ page }) => {
  await page.goto('/build');
  const library = page.getByTestId('starter-library');
  await library.locator('summary').click();
  await library.getByRole('button', { name: 'Open Cluster pays example', exact: true }).click();
  await page.getByRole('button', { name: 'Mechanics', exact: true }).click();
  const manager = page.getByTestId('mechanic-manager');
  await manager.getByLabel('Find a definition').fill('cluster-pays');
  await manager.getByRole('button', { name: 'Edit definition cluster-pays', exact: true }).click();
  await page.getByLabel('Focus graph node').selectOption('candidate-mode');
  await expect(page.locator('.react-flow__node[data-id="candidate-mode"]')).toBeVisible();
  await page.getByRole('button', { name: 'Mechanics', exact: true }).click();
  await manager.getByRole('button', { name: 'Reuse definition cluster-pays', exact: true }).click();
  await expect(page.getByLabel('Subgraph name')).toHaveValue('cluster-pays');
});
