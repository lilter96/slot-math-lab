import { test, expect } from './fixtures';

const config = { schemaVersion: '1.0.0', name: 'Confirm destructive edits', nodes: [
  { id: 'draw', nodeType: 'draw', drawWeights: [{ outcomeId: 'one', weight: 1, value: 1 }], outputs: { out: { name: 'out', type: 'Wins' } } },
  { id: 'sink', nodeType: 'metricsSink', winCap: 10, inputs: { in: { name: 'in', type: 'Wins' } } },
], edges: [{ id: 'connection', sourceNodeId: 'draw', sourcePort: 'out', targetNodeId: 'sink', targetPort: 'in' }] };
const link = '/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(config))).toString('base64'));

test('Inspector deletion preserves source on cancel and removes the node and its connections on confirmation', async ({ page }) => {
  await page.goto(link);
  await page.getByLabel('Focus graph node').selectOption('draw');
  page.once('dialog', async dialog => { expect(dialog.message()).toContain('1 node'); await dialog.dismiss(); });
  await page.getByRole('button', { name: 'Delete node', exact: true }).click();
  await expect(page.locator('.react-flow__node[data-id="draw"]')).toBeVisible();
  await expect(page.locator('.react-flow__edge[data-id="connection"]')).toHaveCount(1);
  page.once('dialog', dialog => dialog.accept());
  await page.getByRole('button', { name: 'Delete node', exact: true }).click();
  await expect(page.getByLabel('Focus graph node').locator('option[value="draw"]')).toHaveCount(0);
  await expect(page.locator('.react-flow__edge[data-id="connection"]')).toHaveCount(0);
  await expect(page.getByLabel('Focus graph node').locator('option[value="sink"]')).toHaveCount(1);
});

test('Keyboard connection deletion requires confirmation and leaves both endpoint nodes intact', async ({ page }) => {
  await page.goto(link);
  const edge = page.locator('.react-flow__edge[data-id="connection"]');
  await edge.focus();
  await page.keyboard.press('Enter');
  page.once('dialog', async dialog => { expect(dialog.message()).toContain('1 connection'); await dialog.dismiss(); });
  await page.keyboard.press('Delete');
  await expect(edge).toHaveCount(1);
  page.once('dialog', dialog => dialog.accept());
  await page.keyboard.press('Delete');
  await expect(edge).toHaveCount(0);
  for (const id of ['draw', 'sink']) await expect(page.getByLabel('Focus graph node').locator(`option[value="${id}"]`)).toHaveCount(1);
});

test('Primitive outcome removal and primitive node deletion can be cancelled', async ({ page }) => {
  const legacy = { name: 'Primitive delete confirmation', nodes: [
    { id: 'draw', type: 'draw', position: { x: 80, y: 100 }, data: { nodeType: 'draw', label: 'Weighted draw', drawWeights: [{ outcomeId: 'zero', weight: 1, value: 0 }, { outcomeId: 'one', weight: 1, value: 1 }] } },
    { id: 'sink', type: 'sink', position: { x: 400, y: 100 }, data: { nodeType: 'sink', label: 'Payout', winCap: 10000 } },
  ], edges: [{ id: 'edge', source: 'draw', target: 'sink', sourceHandle: 'out', targetHandle: 'in' }], tables: { schemaVersion: '1.0.0' }, mechanics: [] };
  await page.goto('/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(legacy))).toString('base64')));
  await page.getByLabel('Focus graph node').selectOption('draw');
  const remove = page.getByRole('button', { name: 'Remove outcome zero', exact: true });
  page.once('dialog', async dialog => { expect(dialog.message()).toContain('draw distribution'); await dialog.dismiss(); });
  await remove.click();
  await expect(page.getByLabel('Outcome ID 1', { exact: true })).toHaveValue('zero');
  page.once('dialog', dialog => dialog.accept());
  await remove.click();
  await expect(page.getByLabel('Outcome ID 1', { exact: true })).toHaveValue('one');
  await expect(page.getByLabel('Outcome ID 2', { exact: true })).toHaveCount(0);
  page.once('dialog', dialog => dialog.dismiss());
  await page.getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(page.getByLabel('Focus graph node').locator('option[value="draw"]')).toHaveCount(1);
  page.once('dialog', dialog => dialog.accept());
  await page.getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(page.getByLabel('Focus graph node').locator('option[value="draw"]')).toHaveCount(0);
});

test('Keyboard node deletion preserves connected source on cancel and removes incident edges on confirmation', async ({ page }) => {
  await page.goto(link);
  await expect(page.locator('.react-flow__node[data-id="sink"]')).toBeInViewport();
  const node = page.locator('.react-flow__node[data-id="draw"]');
  await node.focus();
  await expect(node).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(node).toHaveClass(/selected/);
  page.once('dialog', dialog => dialog.dismiss());
  await page.keyboard.press('Backspace');
  await expect(node).toHaveCount(1);
  await expect(page.locator('.react-flow__edge[data-id="connection"]')).toHaveCount(1);
  page.once('dialog', dialog => dialog.accept());
  await page.keyboard.press('Backspace');
  await expect(page.getByLabel('Focus graph node').locator('option[value="draw"]')).toHaveCount(0);
  await expect(page.locator('.react-flow__edge[data-id="connection"]')).toHaveCount(0);
  await expect(page.getByLabel('Focus graph node').locator('option[value="sink"]')).toHaveCount(1);
});
