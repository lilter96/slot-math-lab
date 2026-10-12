# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: studio-source-lifecycle.spec.ts >> Studio source creation: a new reels board uses the authored dictionary and executes ordinary graph primitives
- Location: e2e/studio-source-lifecycle.spec.ts:19:49

# Error details

```
Error: expect(received).toBe(expected) // Object.is equality

Expected: true
Received: false
```

# Page snapshot

```yaml
- generic [ref=e3]:
  - link "Skip to main content" [ref=e4] [cursor=pointer]:
    - /url: "#main-content"
  - generic [ref=e5]:
    - generic [ref=e8]: Slot Math Lab
    - link "Choose project" [ref=e9] [cursor=pointer]:
      - /url: /projects
      - generic [ref=e11]: Create an independent source
    - generic [ref=e12]:
      - button "AI assist" [ref=e13] [cursor=pointer]
      - link "Games" [ref=e14] [cursor=pointer]:
        - /url: /games.html
        - img [ref=e15]
        - text: Games
      - button "Appearance settings" [ref=e18] [cursor=pointer]: ⚙
  - generic [ref=e19]:
    - complementary "Workspace" [ref=e20]:
      - button "Collapse navigation" [expanded] [ref=e21] [cursor=pointer]
      - generic [ref=e22]: Create an independent source
      - tablist "Main navigation" [ref=e23]:
        - tab "My projects" [ref=e24] [cursor=pointer]:
          - img [ref=e25]
          - text: My projects
        - tab "Overview" [ref=e30] [cursor=pointer]:
          - img [ref=e31]
          - text: Overview
        - tab "Model data" [ref=e36] [cursor=pointer]:
          - img [ref=e37]
          - text: Model data
        - tab "Studio" [selected] [ref=e42] [cursor=pointer]:
          - img [ref=e43]
          - text: Studio
        - tab "Features" [ref=e46] [cursor=pointer]:
          - img [ref=e47]
          - text: Features
        - tab "Build" [ref=e50] [cursor=pointer]:
          - img [ref=e51]
          - text: Build
        - tab "Simulate" [ref=e56] [cursor=pointer]:
          - img [ref=e57]
          - text: Simulate
        - tab "Results" [ref=e59] [cursor=pointer]:
          - img [ref=e60]
          - text: Results
        - tab "Export" [ref=e63] [cursor=pointer]:
          - img [ref=e64]
          - text: Export
        - tab "Debug" [ref=e67] [cursor=pointer]:
          - img [ref=e68]
          - text: Debug
        - tab "Reference demo" [ref=e70] [cursor=pointer]:
          - img [ref=e71]
          - text: Reference demo
    - main [ref=e73]:
      - region "Reel & Paytable Studio" [ref=e74]:
        - generic [ref=e75]:
          - generic [ref=e76]:
            - paragraph [ref=e77]:
              - text: Create an independent source
              - generic [ref=e78]: Private project
            - heading "Reel & Paytable Studio" [level=1] [ref=e79]
          - generic [ref=e80]:
            - button "Create source" [ref=e81] [cursor=pointer]
            - button "Undo" [disabled] [ref=e82]
            - button "Validate & apply" [ref=e83] [cursor=pointer]
            - link "Run analysis ↗" [disabled] [ref=e84]:
              - /url: /simulate
              - text: Run analysis
              - generic [ref=e85]: ↗
        - generic [ref=e86]:
          - tablist "Studio views" [ref=e87]:
            - tab "Sources" [selected] [ref=e88] [cursor=pointer]
            - tab "Paytables" [ref=e89] [cursor=pointer]
            - tab "Paylines" [ref=e90] [cursor=pointer]
            - tab "Symbols" [ref=e91] [cursor=pointer]
          - generic [ref=e92]: 1 pending change
        - generic [ref=e93]:
          - complementary [ref=e94]:
            - generic [ref=e95]:
              - heading "Drawing sources" [level=2] [ref=e96]
              - searchbox "Search Studio sources" [ref=e97]
            - generic [ref=e98]:
              - button "Back up project" [ref=e99] [cursor=pointer]:
                - img [ref=e100]
                - text: Back up project
              - link "Switch private project" [ref=e103] [cursor=pointer]:
                - /url: /projects
          - main [ref=e104]:
            - generic [ref=e105]:
              - generic [ref=e106]: No drawing source
              - button "Source details" [ref=e107] [cursor=pointer]:
                - text: Source details
                - img [ref=e108]
            - generic [ref=e110]:
              - img [ref=e111]
              - heading "No inline drawing source" [level=2] [ref=e116]
              - paragraph [ref=e117]: This model’s source is retained. Open the graph to inspect custom or externally provided inputs.
              - link "Inspect graph" [ref=e118] [cursor=pointer]:
                - /url: /build
            - generic [ref=e119]:
              - generic [ref=e120]:
                - text: Test the applied source
                - paragraph [ref=e121]: A real round through the existing execution API. A single round does not estimate RTP.
              - generic [ref=e122]:
                - text: Seed
                - textbox "Test round seed" [ref=e123]: "42"
              - button "Play test round" [disabled] [ref=e124]:
                - img [ref=e125]
                - text: Play test round
        - generic [ref=e127]:
          - generic [ref=e128]:
            - generic [ref=e129]: 1 pending change
            - generic [ref=e130]: Staged inputs save separately; the applied graph stays unchanged until validation succeeds.
          - generic [ref=e131]:
            - button "Download staged draft" [ref=e132] [cursor=pointer]
            - button "Discard changes" [ref=e133] [cursor=pointer]
          - group [ref=e134]:
            - generic "Review 1 staged changes" [ref=e135] [cursor=pointer]
          - alert [ref=e136]: "Expression 'my-source-entry/window': Unknown field 'emptyArray' on state."
```

# Test source

```ts
  1  | import { readFile } from 'node:fs/promises';
  2  | import { test, expect, type Page, type APIRequestContext } from './fixtures';
  3  | import { sharedDrawingProof, emptySourceProof } from '../unit/fixtures/sourceLifecycleProof';
  4  | import { prizeStudioProof } from '../unit/fixtures/prizeStudioProof';
  5  | import { computedLineBoundProof } from '../unit/fixtures/computedLineBoundProof';
  6  | 
  7  | async function open(page: Page, graph: Record<string, unknown>) {
  8  |   await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles({ name: 'source-proof.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(graph)) }); await expect(page.locator('.project-name')).toHaveText(String(graph.name)); await page.getByRole('tab', { name: 'Studio', exact: true }).click();
  9  | }
  10 | async function backup(page: Page) { const pending = page.waitForEvent('download'); await page.getByRole('button', { name: 'Back up project', exact: true }).click(); const download = await pending; return JSON.parse(await readFile((await download.path())!, 'utf8')).graph as Record<string, unknown>; }
> 11 | async function apply(page: Page) { const response = page.waitForResponse(response => new URL(response.url()).pathname === '/api/validate' && response.status() !== 429); await page.getByRole('button', { name: 'Validate & apply', exact: true }).click(); expect((await (await response).json()).isValid).toBe(true); await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeEnabled(); }
     |                                                                                                                                                                                                                                                                                                             ^ Error: expect(received).toBe(expected) // Object.is equality
  12 | async function exact(request: APIRequestContext, graph: Record<string, unknown>, expected: number) { const response = await request.post('/api/evaluate/light', { data: { config: graph, maxBranches: 10000, sampleSize: 1000 } }); expect(response.ok()).toBe(true); const result = await response.json(); expect(result.strategy).toBe('Exact'); expect(result.rtp).toBeCloseTo(expected, 12); }
  13 | 
  14 | test('Studio source creation: a blank model receives an ordinary numeric source, persists before reload and executes without Studio metadata', async ({ page, request }) => {
  15 |   const graph = emptySourceProof(); await open(page, graph); await page.getByRole('button', { name: 'Create source', exact: true }).click(); await page.getByLabel('New source outcomes').fill('2,3,0\n10,1,0');
  16 |   await page.getByRole('button', { name: 'Stage new source', exact: true }).click(); await page.reload(); await apply(page); const changed = await backup(page); expect(changed.opaque).toEqual(graph.opaque); await exact(request, changed, 4);
  17 |   await page.getByRole('button', { name: 'Undo', exact: true }).click(); expect(await backup(page)).toEqual(graph);
  18 | });
  19 | for (const kind of ['cells', 'reels'] as const) test(`Studio source creation: a new ${kind} board uses the authored dictionary and executes ordinary graph primitives`, async ({ page, request }) => {
  20 |   const graph = emptySourceProof(); await open(page, graph); await page.getByRole('button', { name: 'Create source', exact: true }).click(); await page.getByLabel('New source kind').selectOption(kind); await page.getByRole('button', { name: 'Stage new source', exact: true }).click(); await apply(page);
  21 |   const changed = await backup(page); expect(changed.boardConfig).toEqual({ columns: 3, rows: 3 }); expect(changed.symbols).toEqual([{ id: 'A', name: 'Symbol A', kind: 'Standard' }, { id: 'B', name: 'Symbol B', kind: 'Standard' }]);
  22 |   const response = await request.post('/api/play/round', { data: { config: changed, seed: 42, roundIndex: 0 } }); expect(response.ok()).toBe(true); const result = await response.json(); expect(result.state.board).toHaveLength(9); expect(result.state.board.every((id: string) => ['A', 'B'].includes(id))).toBe(true); expect(result.win).toBe(0); expect(changed.opaque).toEqual(graph.opaque);
  23 | });
  24 | for (const kind of ['cells', 'reels'] as const) test(`Studio private ${kind}: copying the second use and editing only its source changes real probability while the first source stays intact`, async ({ page, request }) => {
  25 |   const graph = sharedDrawingProof(kind); await open(page, graph); await page.getByRole('button', { name: 'Make one use independent', exact: true }).click(); await page.getByLabel('Independent source use').selectOption({ label: 'Root / Second use of the source' }); await page.getByRole('button', { name: 'Stage independent copy', exact: true }).click(); await apply(page);
  26 |   const copied = await backup(page); await exact(request, copied, 1);
  27 |   const picker = page.locator('.studio-source-explorer').getByRole('button', { name: /Private source|Private-source|Private_source/ }); await picker.first().click();
  28 |   if (kind === 'reels') { await page.getByLabel('Selected stop symbol').selectOption('B'); await page.getByRole('button', { name: 'Move selected stop down', exact: true }).click(); await page.getByLabel('Selected stop symbol').selectOption('B'); }
  29 |   else { await page.locator('.studio-outcome-domain summary').click(); await page.getByRole('button', { name: 'Remove outcome A', exact: true }).click(); }
  30 |   await apply(page); const changed = await backup(page); await exact(request, changed, .5);
  31 |   expect((changed.mechanics as Record<string, unknown>)['shared-draw']).toEqual((graph.mechanics as Record<string, unknown>)['shared-draw']); expect((changed.nodes as { id: string }[]).find(node => node.id === 'first')).toEqual((graph.nodes as { id: string }[]).find(node => node.id === 'first')); expect(changed.opaque).toEqual(graph.opaque);
  32 | });
  33 | test('Studio prize lifecycle: adding/removing a prize updates real expected payout and requires explicit removal policy', async ({ page, request }) => {
  34 |   const graph = prizeStudioProof(); await open(page, graph); await page.getByRole('tab', { name: 'Paytables', exact: true }).click(); await page.locator('.studio-outcome-domain summary').click(); await page.getByLabel('New prize value').fill('10'); await page.getByLabel('New prize weight').fill('2'); await page.getByRole('button', { name: 'Add prize', exact: true }).click(); await apply(page);
  35 |   const added = await backup(page); await exact(request, added, 28 / 3); // (3×2 + 1×10 + 2×20) / 6
  36 |   await page.getByRole('button', { name: 'Remove prize 1', exact: true }).click(); await expect(page.getByRole('button', { name: 'Validate & apply', exact: true })).toBeDisabled(); await expect(page.getByRole('alert')).toContainText('Confirm discarding'); await page.getByLabel('Discard weights of removed prize outcomes', { exact: false }).check(); await apply(page);
  37 |   await exact(request, await backup(page), 50 / 3); // (1×10 + 2×20) / 3
  38 | });
  39 | test('Studio line lifecycle: duplicate/remove lines preserve per-line pricing or explicitly keep a fixed total, and execute the real payout', async ({ page, request }) => {
  40 |   const graph = computedLineBoundProof(); await open(page, graph); await page.getByRole('tab', { name: 'Paylines', exact: true }).click(); await page.getByRole('button', { name: 'Duplicate selected line', exact: true }).click();
  41 |   await expect(page.getByLabel('Line stake policy')).toHaveValue('per-line'); await apply(page); const priced = await backup(page); expect((priced.initialState as Record<string, unknown>).totalBet).toBe(2); expect((priced.initialState as Record<string, unknown>).roundCost).toBe(4); await exact(request, priced, 7);
  42 |   await page.getByRole('button', { name: 'Undo', exact: true }).click(); expect(await backup(page)).toEqual(graph); await page.getByRole('button', { name: 'Duplicate selected line', exact: true }).click(); await page.getByLabel('Line stake policy').selectOption('fixed-total'); await expect(page.getByRole('button', { name: 'Validate & apply', exact: true })).toBeDisabled(); await page.getByLabel('Confirm fixed total line stake').check(); await apply(page);
  43 |   const fixed = await backup(page); expect((fixed.initialState as Record<string, unknown>).totalBet).toBe(1); expect((fixed.initialState as Record<string, unknown>).roundCost).toBe(2); await exact(request, fixed, 14);
  44 |   await page.getByRole('button', { name: 'Remove selected line', exact: true }).click(); await apply(page); await exact(request, await backup(page), 7);
  45 | });
  46 | 
```