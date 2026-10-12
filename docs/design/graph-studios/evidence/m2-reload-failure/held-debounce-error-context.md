# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: graph-studios-storage.spec.ts >> Graph studios storage: immediate reload flushes pending metadata/source without waiting for the save debounce
- Location: e2e/graph-studios-storage.spec.ts:7:1

# Error details

```
Error: expect(locator).toHaveText(expected) failed

Locator:  locator('.project-name')
Expected: "Authoring metadata proof"
Received: "Choose a project"
Timeout:  5000ms

Call log:
  - Expect "toHaveText" with timeout 5000ms
  - waiting for locator('.project-name')
    12 × locator resolved to <span class="project-name">Choose a project</span>
       - unexpected value "Choose a project"

```

```yaml
- text: Choose a project
```

# Test source

```ts
  1  | import { readFile } from 'node:fs/promises';
  2  | import { test, expect } from './fixtures';
  3  | const graph = { schemaVersion: '1.0.0', name: 'Authoring metadata proof', nodes: [{ id: 'draw', nodeType: 'draw', drawWeights: [{ outcomeId: 'A', weight: 3, value: 1 }, { outcomeId: 'B', weight: 1, value: 0 }], outputs: { out: { name: 'out', type: 'Wins' } } }, { id: 'sink', nodeType: 'metricsSink', winCap: 10, inputs: { in: { name: 'in', type: 'Wins' } } }], edges: [{ id: 'edge', sourceNodeId: 'draw', sourcePort: 'out', targetNodeId: 'sink', targetPort: 'in' }], opaque: { keep: true } };
  4  | const views = { version: 1, studio: { tab: 'sources', selectedSource: '[null,"draw"]', pinnedSources: ['[null,"draw"]'] }, machine: { positions: { entry: { x: 120, y: 85 } }, viewport: { x: 0, y: 0, zoom: 1 }, expandedSources: [] } };
  5  | const envelope = { format: 'slotmath-project-v1', graph, views };
  6  | 
  7  | test('Graph studios storage: immediate reload flushes pending metadata/source without waiting for the save debounce', async ({ page }) => {
  8  |   // Hold the actual debounce open. Reload must use the production beforeunload flush, not a test-side write.
  9  |   await page.addInitScript(() => {
  10 |     const original = window.setTimeout.bind(window);
  11 |     window.setTimeout = ((callback: TimerHandler, delay?: number, ...args: unknown[]) => original(callback, delay === 400 ? 60000 : delay, ...args)) as typeof window.setTimeout;
  12 |   });
  13 |   await page.goto('/build');
  14 |   await page.getByLabel('Import project file').setInputFiles({ name: 'authoring-project.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(envelope)) });
  15 |   await expect(page.locator('.project-name')).toHaveText(graph.name);
  16 |   expect(await page.evaluate(() => localStorage.getItem('slotmath-draft-v1'))).toBeNull();
> 17 |   await page.reload(); await expect(page.locator('.project-name')).toHaveText(graph.name);
     |                                                                    ^ Error: expect(locator).toHaveText(expected) failed
  18 |   const saved = await page.evaluate(() => JSON.parse(localStorage.getItem('slotmath-draft-v1') ?? 'null'));
  19 |   expect(saved.authoringViews).toEqual(views); expect(saved.projects[saved.projectId].authoringViews).toEqual(views); expect(saved.tables.authoringViews).toBeUndefined();
  20 |   const pending = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export project', exact: true }).click();
  21 |   const download = await pending, path = await download.path(); expect(path).not.toBeNull(); expect(JSON.parse(await readFile(path!, 'utf8'))).toEqual(graph);
  22 | });
  23 | 
  24 | test('Graph studios storage: confirmed autosave retains envelope views and unnamed raw imports remain unnamed after reload', async ({ page }) => {
  25 |   await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles({ name: 'project.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(envelope)) });
  26 |   await expect.poll(async () => page.evaluate(() => JSON.parse(localStorage.getItem('slotmath-draft-v1') ?? 'null')?.authoringViews)).toEqual(views);
  27 |   await page.reload(); await expect(page.locator('.project-name')).toHaveText(graph.name);
  28 |   const { name: _name, ...unnamed } = graph; void _name;
  29 |   await page.getByLabel('Import project file').setInputFiles({ name: 'raw.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(unnamed)) });
  30 |   await expect.poll(async () => page.evaluate(() => {
  31 |     const draft = JSON.parse(localStorage.getItem('slotmath-draft-v1') ?? 'null'); return draft && { name: draft.configName, views: draft.authoringViews };
  32 |   })).toEqual({ name: null, views: null });
  33 |   await page.reload(); const pending = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export project', exact: true }).click();
  34 |   const download = await pending, path = await download.path(); expect(JSON.parse(await readFile(path!, 'utf8'))).toEqual(unnamed);
  35 | });
  36 | 
```