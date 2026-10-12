import { test, expect, type Page } from './fixtures';

const nodes = [
  { id: 'draw', nodeType: 'draw', label: 'Draw', drawWeights: [{ outcomeId: 'one', weight: 1, value: 1 }], outputs: { out: { name: 'out', type: 'Wins' } } },
  { id: 'sink', nodeType: 'metricsSink', label: 'Payout', winCap: 10, inputs: { in: { name: 'in', type: 'Wins' } } },
];
const edges = [{ id: 'edge', sourceNodeId: 'draw', sourcePort: 'out', targetNodeId: 'sink', targetPort: 'in' }];
const config = { schemaVersion: '1.0.0', name: 'Focus scope', nodes, edges, mechanics: { inner: { name: 'Inner', nodes, edges } } };
const link = '/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(config))).toString('base64'));
type Harness = { holdFocus(id: string): void; releaseFocus(): void; heldCount: number; focusCancelled: boolean };

async function install(page: Page) {
  await page.evaluate(() => {
    const host = window as unknown as Harness;
    const request = window.requestAnimationFrame.bind(window), cancel = window.cancelAnimationFrame.bind(window);
    let capturing = false;
    const held = new Map<number, FrameRequestCallback>();
    host.heldCount = 0; host.focusCancelled = false;
    window.requestAnimationFrame = callback => {
      if (!capturing) return request(callback);
      const id = request(() => {}); cancel(id); held.set(id, callback); host.heldCount++;
      return id;
    };
    window.cancelAnimationFrame = id => { if (held.has(id)) host.focusCancelled = true; cancel(id); };
    host.holdFocus = id => { capturing = true; try { window.dispatchEvent(new CustomEvent('slotmath:focus-node', { detail: id })); } finally { capturing = false; } };
    // Force even cancelled callbacks to run, exercising the callback's own scope guard as well.
    host.releaseFocus = () => { for (const callback of held.values()) callback(performance.now()); held.clear(); };
  });
}
async function settle(page: Page) {
  await page.evaluate(() => new Promise<void>(resolve => { let count = 0; const step = () => { if (++count === 6) resolve(); else requestAnimationFrame(step); }; requestAnimationFrame(step); }));
}
async function selectWithoutFit(page: Page, id: string) {
  await page.locator(`.react-flow__node[data-id="${id}"]`).click();
  await page.getByRole('button', { name: 'Close inspector', exact: true }).click();
  await page.getByRole('button', { name: 'Reset view', exact: true }).click();
  await page.getByRole('button', { name: '100% zoom', exact: true }).click();
  await settle(page);
}

test('Held focus in its original project remains a working camera command', async ({ page }) => {
  await page.goto(link); await selectWithoutFit(page, 'draw'); await install(page);
  const before = await page.locator('.react-flow__viewport').getAttribute('style');
  await page.evaluate(() => (window as unknown as Harness).holdFocus('draw'));
  expect(await page.evaluate(() => (window as unknown as Harness).heldCount)).toBe(1);
  await page.evaluate(() => (window as unknown as Harness).releaseFocus());
  await expect(page.locator('.react-flow__viewport')).not.toHaveAttribute('style', before!);
});

for (const change of ['project', 'mechanic', 'selection']) {
  test(`Delayed focus cannot move the camera after changing ${change}`, async ({ page }) => {
    await page.goto(link); await selectWithoutFit(page, 'draw'); await install(page);
    await page.evaluate(() => (window as unknown as Harness).holdFocus('draw'));
    expect(await page.evaluate(() => (window as unknown as Harness).heldCount)).toBe(1);
    if (change === 'project') {
      await page.getByLabel('Import project file').setInputFiles({ name: 'copy.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify({ ...config, name: 'Independent same IDs' })) });
      await expect(page.getByRole('link', { name: 'Choose project', exact: true })).toContainText('Independent same IDs');
      await selectWithoutFit(page, 'draw');
    } else if (change === 'mechanic') {
      await page.getByLabel('Find subgraph').fill('inner');
      await page.getByRole('button', { name: 'Open inner graph', exact: true }).click();
      await expect(page.getByTestId('model-navigation')).toContainText('inner');
      await selectWithoutFit(page, 'draw');
    } else {
      await page.getByLabel('Focus graph node').selectOption('sink');
      await settle(page);
    }
    await expect.poll(() => page.evaluate(() => (window as unknown as Harness).focusCancelled)).toBe(true);
    const camera = await page.locator('.react-flow__viewport').getAttribute('style');
    await page.evaluate(() => (window as unknown as Harness).releaseFocus()); await settle(page);
    await expect(page.locator('.react-flow__viewport')).toHaveAttribute('style', camera!);
  });
}
