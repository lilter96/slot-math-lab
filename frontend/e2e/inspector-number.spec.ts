import { test, expect, type Page } from './fixtures';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
const rows = [{ outcomeId: 'one', weight: 10000000000, value: 1 }, { outcomeId: 'zero', weight: 10000000000, value: 0 }];
const native = { schemaVersion: '1.0.0', name: 'Native integer inspector', unknown: { preserved: true }, nodes: [
  { id: 'draw', nodeType: 'draw', label: 'Draw', drawWeights: rows, outputs: { out: { name: 'out', type: 'Wins' } } },
  { id: 'sink', nodeType: 'metricsSink', label: 'Sink', winCap: 10, inputs: { in: { name: 'in', type: 'Wins' } } },
], edges: [{ id: 'edge', sourceNodeId: 'draw', sourcePort: 'out', targetNodeId: 'sink', targetPort: 'in' }] };
const legacy = { name: 'Legacy integer inspector', tables: { schemaVersion: '1.0.0', unknown: { preserved: true } }, nodes: [
  { id: 'draw', type: 'draw', position: { x: 0, y: 0 }, data: { nodeType: 'draw', label: 'Draw', drawWeights: rows } },
  { id: 'sink', type: 'sink', position: { x: 300, y: 0 }, data: { nodeType: 'sink', label: 'Sink', winCap: 10 } },
], edges: [{ id: 'edge', source: 'draw', sourceHandle: 'out', target: 'sink', targetHandle: 'in' }] };
const link = (model: unknown) => '/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(model))).toString('base64'));
async function exported(page: Page) {
  const pending = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export project', exact: true }).click();
  return JSON.parse(await readFile((await (await pending).path())!, 'utf8'));
}
for (const [kind, model] of [['native', native], ['legacy', legacy]] as const) {
  test(`${kind} Inspector preserves integer edits, exact summaries and zero/total guards through a real compiler result`, async ({ page, request }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(link(model)); await page.getByLabel('Focus graph node').selectOption('draw');
    const inspector = page.getByRole('complementary', { name: 'Context inspector' });
    const weight = inspector.getByLabel(kind === 'native' ? 'Outcome one weight' : 'Weight for one', { exact: true });
    await weight.fill('1.5'); await expect(inspector.getByRole('alert')).toContainText('whole number');
    expect((await exported(page)).nodes[0].drawWeights).toEqual(rows);
    await weight.fill('9007199254740991'); await expect(inspector.getByRole('alert')).toContainText('total weight');
    expect((await exported(page)).nodes[0].drawWeights).toEqual(rows);
    await weight.fill('0');
    const zero = inspector.getByLabel(kind === 'native' ? 'Outcome zero weight' : 'Weight for zero', { exact: true });
    await zero.fill('0'); await expect(inspector.getByRole('alert')).toContainText('positive weight');
    expect((await exported(page)).nodes[0].drawWeights).toEqual([{ ...rows[0], weight: 0 }, rows[1]]);
    await weight.fill('30000000000'); await zero.fill('10000000000');
    await expect(inspector.getByText(/Static table mean: 3\/4/)).toBeVisible();
    const changed = await exported(page); expect(changed.unknown).toEqual({ preserved: true });
    const result = await request.post('/api/evaluate/light', { data: { config: changed, maxBranches: 10000, sampleSize: 5000 } });
    expect(result.ok()).toBe(true); expect(await result.json()).toMatchObject({ strategy: 'Exact', rtp: 0.75 });
    const value = inspector.getByLabel(kind === 'native' ? 'Outcome one value' : 'Value for one', { exact: true });
    await value.fill('-1e10'); expect((await exported(page)).nodes[0].drawWeights[0].value).toBe(-10000000000);
    await value.fill('9007199254740993'); await expect(inspector.getByRole('alert')).toContainText('exact browser range');
    for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
      await page.setViewportSize({ width, height });
      await value.scrollIntoViewIfNeeded();
      expect(await value.evaluate(element => { const field = element.getBoundingClientRect(), viewport = element.closest('table')!.parentElement!.getBoundingClientRect(); return field.left >= viewport.left - 1 && field.right <= viewport.right + 1; })).toBe(true);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: path.resolve('../docs/design/qa', `inspector-number-${kind}-${test.info().project.name}-${width}.png`) });
    }
  });
  test(`${kind} Inspector caps reject fractions and range violations without replacing the previous node values`, async ({ page }) => {
    const model = kind === 'native' ? { ...native, nodes: [...native.nodes, { id: 'loop', nodeType: 'loop', label: 'Loop', maxIterations: 5 }] } : { ...legacy, nodes: [...legacy.nodes, { id: 'loop', type: 'loop', position: { x: 0, y: 300 }, data: { nodeType: 'loop', label: 'Loop', iterations: 5 } }] };
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(link(model)); await page.getByLabel('Focus graph node').selectOption('sink');
    const cap = page.getByLabel('Round win cap', { exact: true });
    for (const text of ['', '0', '1.5', '9007199254740993']) { await cap.fill(text); expect((await exported(page)).nodes.find((node: { id: string }) => node.id === 'sink').winCap).toBe(10); }
    await cap.fill('10000000000');
    await page.getByLabel('Focus graph node').selectOption('loop');
    const iterations = page.getByLabel('Iteration cap', { exact: true });
    for (const text of ['0', '1.5', '100001', '2147483648']) { await iterations.fill(text); expect((await exported(page)).nodes.find((node: { id: string }) => node.id === 'loop').maxIterations).toBe(5); }
    await iterations.fill('100000');
    await expect(page.locator('.react-flow__node[data-id="loop"]')).toContainText('100000');
    if (kind === 'legacy') await expect(page.getByRole('button', { name: 'Increase iteration cap' })).toBeDisabled();
    const output = await exported(page); expect(output.nodes.find((node: { id: string }) => node.id === 'loop').maxIterations).toBe(100000);
    expect(output.nodes.find((node: { id: string }) => node.id === 'sink').winCap).toBe(10000000000);
    await page.getByLabel('Focus graph node').selectOption('sink'); await expect(page.getByRole('complementary', { name: 'Context inspector' }).getByRole('alert')).toHaveCount(0);
  });
}
