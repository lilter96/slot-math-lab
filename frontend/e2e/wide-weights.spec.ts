import { test, expect } from './fixtures';
import { readFile } from 'node:fs/promises';

const model = { schemaVersion: '1.0.0', name: 'Long weighted draw', reviewerMetadata: { preserve: true }, nodes: [
  { id: 'draw', nodeType: 'draw', drawWeights: [{ outcomeId: 'one', weight: 10000000000, value: 1 }, { outcomeId: 'zero', weight: 10000000000, value: 0 }], outputs: { out: { name: 'out', type: 'Wins' } } },
  { id: 'sink', nodeType: 'metricsSink', winCap: 10, inputs: { in: { name: 'in', type: 'Wins' } } },
], edges: [{ id: 'edge', sourceNodeId: 'draw', sourcePort: 'out', targetNodeId: 'sink', targetPort: 'in' }] };
const link = '/model/data?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(model))).toString('base64'));
async function exported(page: import('./fixtures').Page) {
  await page.getByRole('tab', { name: 'Build', exact: true }).click();
  const download = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export project', exact: true }).click();
  return JSON.parse(await readFile((await (await download).path())!, 'utf8'));
}
test('Long weights compile, change an independent exact expectation and Undo restores the source', async ({ page, request }) => {
  test.setTimeout(180000);
  await page.goto(link);
  await expect(page.getByLabel('Weight for one')).toHaveValue('10000000000');
  await expect(page.getByRole('alert')).toHaveCount(0);
  await page.getByLabel('Weight for one').fill('30000000000');
  await page.getByRole('button', { name: 'Validate & apply weights', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Undo weight change' })).toBeVisible({ timeout: 70000 });
  await page.getByRole('button', { name: 'Undo weight change' }).click();
  await expect(page.getByLabel('Weight for one')).toHaveValue('10000000000');
  await page.getByLabel('Weight for one').fill('30000000000');
  await page.getByRole('button', { name: 'Validate & apply weights', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Undo weight change' })).toBeVisible({ timeout: 70000 });
  const changed = await exported(page);
  expect(changed.nodes[0].drawWeights).toEqual([{ outcomeId: 'one', weight: 30000000000, value: 1 }, model.nodes[0].drawWeights![1]]);
  expect(changed.reviewerMetadata).toEqual(model.reviewerMetadata); expect(changed.edges).toEqual(model.edges);
  for (const [config, expected] of [[model, 0.5], [changed, 0.75]] as const) {
    const result = await request.post('/api/evaluate/light', { data: { config, maxBranches: 10000, sampleSize: 5000 } });
    expect(result.ok()).toBe(true); const data = await result.json(); expect(data.strategy).toBe('Exact'); expect(data.rtp).toBe(expected);
  }
});

test('CSV supports long weights while an unsafe exact total remains unapplied', async ({ page }) => {
  await page.goto(link);
  await page.getByLabel('Weights CSV file').setInputFiles({ name: 'long.csv', mimeType: 'text/csv', buffer: Buffer.from('outcomeId,weight\none,30000000000\nzero,10000000000') });
  await expect(page.getByLabel('Weight for one')).toHaveValue('30000000000');
  await page.getByLabel('Weight for one').fill(String(Number.MAX_SAFE_INTEGER));
  await expect(page.getByRole('alert')).toContainText('total weight');
  await expect(page.getByRole('button', { name: 'Validate & apply weights', exact: true })).toBeDisabled();
  expect(await exported(page)).toMatchObject(model);
});
