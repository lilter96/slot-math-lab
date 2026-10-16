import { test, expect } from './fixtures';

const config = { schemaVersion: '1.0.0', name: 'Bad edge source', nodes: [
  { id: 'draw', nodeType: 'draw', drawWeights: [{ outcomeId: 'one', weight: 1, value: 1 }], outputs: { out: { name: 'out', type: 'Wins' } } },
  { id: 'sink', nodeType: 'metricsSink', winCap: 10, inputs: { in: { name: 'in', type: 'State' } } },
], edges: [{ id: 'bad-port', sourceNodeId: 'draw', sourcePort: 'out', targetNodeId: 'sink', targetPort: 'in' }] };
const link = '/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(config))).toString('base64'));

test('A compiler edge diagnostic selects the authored connection and frames its endpoints', async ({ page }) => {
  await page.goto(link);
  await page.getByRole('button', { name: 'Validate model', exact: true }).click();
  await page.getByRole('button', { name: 'Focus edge bad-port', exact: true }).first().click();
  await expect(page.locator('.react-flow__edge[data-id="bad-port"]')).toHaveClass(/selected/);
  for (const id of ['draw', 'sink']) await expect(page.locator(`.react-flow__node[data-id="${id}"]`)).toBeVisible();
  await expect(page.getByRole('complementary', { name: 'Context inspector' })).toHaveCount(0);
});

test('Late compiler feedback is cancelled when an identical model opens as another project', async ({ page }) => {
  await page.goto(link);
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  let requested = false, released = false;
  await page.route('**/api/validate', async route => {
    requested = true;
    const response = await route.fetch();
    await gate;
    await route.fulfill({ response });
    released = true;
  });
  await page.getByRole('button', { name: 'Validate model', exact: true }).click();
  await expect.poll(() => requested).toBe(true);
  const file = Buffer.from(JSON.stringify(config));
  await page.getByLabel('Import project file', { exact: true }).setInputFiles({ name: 'same-model.json', mimeType: 'application/json', buffer: file });
  await expect(page.getByRole('button', { name: 'Validate model', exact: true })).toBeEnabled();
  release();
  await expect.poll(() => released).toBe(true);
  await expect(page.locator('.graph-validation')).not.toContainText('compiler errors');
  await expect(page.locator('.graph-validation').getByRole('alert')).toHaveCount(0);
  await page.unroute('**/api/validate');
  await page.getByRole('button', { name: 'Validate model', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Focus edge bad-port', exact: true }).first()).toBeVisible();
});

test('A real namespaced compiler diagnostic opens the matching authored mechanic', async ({ page }) => {
  const nested = { schemaVersion: '1.0.0', name: 'Nested edge diagnostic', nodes: [{ id: 'outer', nodeType: 'library', mechanicName: 'broken' }], edges: [],
    mechanics: { broken: { name: 'Broken inner flow', nodes: config.nodes, edges: config.edges } } };
  await page.goto('/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(nested))).toString('base64')));
  await page.getByRole('button', { name: 'Validate model', exact: true }).click();
  await page.getByRole('button', { name: 'Focus edge outer/bad-port', exact: true }).first().click();
  await expect(page.getByTestId('model-navigation')).toContainText('broken');
  await expect(page.locator('.react-flow__edge[data-id="bad-port"]')).toHaveClass(/selected/);
  for (const id of ['draw', 'sink']) await expect(page.locator(`.react-flow__node[data-id="${id}"]`)).toBeVisible();
});
