import { readFile } from 'node:fs/promises';
import { test, expect } from './fixtures';
const graph = { schemaVersion: '1.0.0', name: 'Authoring metadata proof', nodes: [{ id: 'draw', nodeType: 'draw', drawWeights: [{ outcomeId: 'A', weight: 3, value: 1 }, { outcomeId: 'B', weight: 1, value: 0 }], outputs: { out: { name: 'out', type: 'Wins' } } }, { id: 'sink', nodeType: 'metricsSink', winCap: 10, inputs: { in: { name: 'in', type: 'Wins' } } }], edges: [{ id: 'edge', sourceNodeId: 'draw', sourcePort: 'out', targetNodeId: 'sink', targetPort: 'in' }], opaque: { keep: true } };
const views = { version: 1, studio: { tab: 'sources', selectedSource: '[null,"draw"]', pinnedSources: ['[null,"draw"]'] }, machine: { positions: { entry: { x: 120, y: 85 } }, viewport: { x: 0, y: 0, zoom: 1 }, expandedSources: [] } };
const envelope = { format: 'slotmath-project-v1', graph, views };

test('Graph studios storage: semantic commits persist and reload correctly while ordinary draft timers remain pending', async ({ page }) => {
  // New projects persist their recovery base immediately. Hold the later-edit debounce open;
  // compiler commits and view selections must persist before unload, without a test-side write.
  await page.addInitScript(() => {
    const original = window.setTimeout.bind(window);
    window.setTimeout = ((callback: TimerHandler, delay?: number, ...args: unknown[]) => original(callback, delay === 400 ? 60000 : delay, ...args)) as typeof window.setTimeout;
  });
  await page.goto('/build');
  await page.getByLabel('Import project file').setInputFiles({ name: 'authoring-project.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(envelope)) });
  await expect(page.locator('.project-name')).toHaveText(graph.name);
  const original = await page.evaluate(() => JSON.parse(localStorage.getItem('slotmath-draft-v1') ?? 'null'));
  expect(original.authoringViews).toEqual(views);
  await page.getByRole('tab', { name: 'Studio', exact: true }).click();
  const weight = page.getByLabel('Weight A in draw', { exact: true }); await expect(weight).toBeEnabled(); await weight.fill('1');
  await page.getByRole('button', { name: 'Validate & apply', exact: true }).click(); await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeEnabled();
  await page.getByRole('tab', { name: 'Symbols', exact: true }).click();
  const changedViews = { ...views, studio: { ...views.studio, tab: 'symbols', selectedSource: null } };
  expect(await page.evaluate(() => JSON.parse(localStorage.getItem('slotmath-draft-v1') ?? 'null').authoringViews)).toEqual(changedViews);
  await page.reload(); await expect(page.locator('.project-name')).toHaveText(graph.name);
  const saved = await page.evaluate(() => JSON.parse(localStorage.getItem('slotmath-draft-v1') ?? 'null'));
  expect(saved.authoringViews).toEqual(changedViews); expect(saved.projects[saved.projectId].authoringViews).toEqual(changedViews); expect(saved.tables.authoringViews).toBeUndefined();
  await page.getByRole('tab', { name: 'Build', exact: true }).click();
  const pending = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export project', exact: true }).click();
  const download = await pending, path = await download.path(); expect(path).not.toBeNull();
  const changed = structuredClone(graph); changed.nodes[0].drawWeights![0].weight = 1;
  expect(JSON.parse(await readFile(path!, 'utf8'))).toEqual(changed);
});

test('Graph studios storage: confirmed autosave retains envelope views and unnamed raw imports remain unnamed after reload', async ({ page }) => {
  await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles({ name: 'project.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(envelope)) });
  await expect.poll(async () => page.evaluate(() => JSON.parse(localStorage.getItem('slotmath-draft-v1') ?? 'null')?.authoringViews)).toEqual(views);
  await page.reload(); await expect(page.locator('.project-name')).toHaveText(graph.name);
  const { name: _name, ...unnamed } = graph; void _name;
  await page.getByLabel('Import project file').setInputFiles({ name: 'raw.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(unnamed)) });
  await expect.poll(async () => page.evaluate(() => {
    const draft = JSON.parse(localStorage.getItem('slotmath-draft-v1') ?? 'null'); return draft && { name: draft.configName, views: draft.authoringViews };
  })).toEqual({ name: null, views: null });
  await page.reload(); const pending = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export project', exact: true }).click();
  const download = await pending, path = await download.path(); expect(JSON.parse(await readFile(path!, 'utf8'))).toEqual(unnamed);
});
