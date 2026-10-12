# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: studio-symbol-lifecycle.spec.ts >> Studio ordinary cluster eligibility: new B row becomes a real one-cell payout, removal retains drawing/dictionary source
- Location: e2e/studio-symbol-lifecycle.spec.ts:17:39

# Error details

```
Test timeout of 30000ms exceeded.
```

```
Error: locator.click: Test timeout of 30000ms exceeded.
Call log:
  - waiting for getByRole('button', { name: 'Remove paying symbol 1', exact: true })

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
      - generic [ref=e11]: Cluster size proof
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
      - generic [ref=e22]: Cluster size proof
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
              - text: Cluster size proof
              - generic [ref=e78]: Private project
            - heading "Reel & Paytable Studio" [level=1] [ref=e79]
          - generic [ref=e80]:
            - button "Undo" [ref=e81] [cursor=pointer]
            - button "Validate & apply" [disabled] [ref=e82]
            - link "Run analysis ↗" [ref=e83] [cursor=pointer]:
              - /url: /simulate
              - text: Run analysis
              - generic [ref=e84]: ↗
        - generic [ref=e85]:
          - tablist "Studio views" [ref=e86]:
            - tab "Sources" [ref=e87] [cursor=pointer]
            - tab "Paytables" [selected] [ref=e88] [cursor=pointer]
            - tab "Paylines" [ref=e89] [cursor=pointer]
            - tab "Symbols" [ref=e90] [cursor=pointer]
          - generic [ref=e91]: Applied to graph
        - generic [ref=e92]:
          - complementary [ref=e93]:
            - generic [ref=e94]:
              - heading "Payout tables" [level=2] [ref=e95]
              - searchbox "Search Studio sources" [ref=e96]
            - button "Private clusters 2 symbol rows" [pressed] [ref=e97] [cursor=pointer]:
              - img [ref=e98]
              - generic [ref=e101]:
                - generic [ref=e102]: Private clusters
                - generic [ref=e103]: 2 symbol rows
            - generic [ref=e104]:
              - button "Back up project" [active] [ref=e105] [cursor=pointer]:
                - img [ref=e106]
                - text: Back up project
              - link "Switch private project" [ref=e109] [cursor=pointer]:
                - /url: /projects
          - main [ref=e110]:
            - generic [ref=e111]:
              - generic [ref=e112]:
                - text: Private clusters
                - generic [ref=e113]: Exact authored payouts
              - button "Source details" [ref=e114] [cursor=pointer]:
                - text: Source details
                - img [ref=e115]
            - generic [ref=e117]:
              - generic [ref=e118]:
                - button "Import table" [ref=e119] [cursor=pointer]
                - button "Copy TSV" [ref=e120] [cursor=pointer]
              - generic [ref=e121]:
                - generic [ref=e122]:
                  - text: Export format
                  - combobox "Table export format" [ref=e123]:
                    - option "Excel · XLSX" [selected]
                    - option "CSV"
                    - option "TSV"
                - button "Export applied table" [ref=e124] [cursor=pointer]
            - generic [ref=e125]:
              - generic [ref=e126]:
                - generic [ref=e127]:
                  - heading "Cluster payouts" [level=2] [ref=e128]
                  - paragraph [ref=e129]: Columns mean connected group size · cluster credits
                - generic [ref=e130]: "Round price: 1 credits"
              - generic [ref=e131]:
                - generic [ref=e132]:
                  - text: New group size
                  - textbox "New group size" [ref=e133]: "4"
                - button "Add size" [ref=e134] [cursor=pointer]
                - generic [ref=e135]:
                  - text: Remove paying size
                  - combobox "Remove paying size" [ref=e136]:
                    - option "1 cells"
                    - option "5 cells" [selected]
                    - option "6 cells"
                - button "Remove size" [ref=e137] [cursor=pointer]
              - group [ref=e138]:
                - generic "Paying symbols & sizes · 2 rows" [ref=e139] [cursor=pointer]
                - option "Choose a symbol" [selected]
                - option "Wild · 0"
              - table [ref=e141]:
                - rowgroup [ref=e142]:
                  - row "Paying symbol 1 group size 5 group size 6 group size" [ref=e143]:
                    - columnheader "Paying symbol" [ref=e144]
                    - columnheader "1 group size" [ref=e145]:
                      - text: "1"
                      - generic [ref=e146]: group size
                    - columnheader "5 group size" [ref=e147]:
                      - text: "5"
                      - generic [ref=e148]: group size
                    - columnheader "6 group size" [ref=e149]:
                      - text: "6"
                      - generic [ref=e150]: group size
                - rowgroup [ref=e151]:
                  - row "A A ID 1 0 10 20" [ref=e152]:
                    - rowheader "A A ID 1" [ref=e153]:
                      - generic [ref=e154]:
                        - generic [ref=e156]: A
                        - generic [ref=e157]:
                          - generic [ref=e158]: A
                          - generic [ref=e159]: ID 1
                    - cell "0" [ref=e160]:
                      - textbox "cluster payout 1 count 1" [ref=e161]: "0"
                    - cell "10" [ref=e162]:
                      - textbox "cluster payout 1 count 5" [ref=e163]: "10"
                    - cell "20" [ref=e164]:
                      - textbox "cluster payout 1 count 6" [ref=e165]: "20"
                  - row "B B ID 2 4 0 0" [ref=e166]:
                    - rowheader "B B ID 2" [ref=e167]:
                      - generic [ref=e168]:
                        - generic [ref=e170]: B
                        - generic [ref=e171]:
                          - generic [ref=e172]: B
                          - generic [ref=e173]: ID 2
                    - cell "4" [ref=e174]:
                      - textbox "cluster payout 2 count 1" [ref=e175]: "4"
                    - cell "0" [ref=e176]:
                      - textbox "cluster payout 2 count 5" [ref=e177]: "0"
                    - cell "0" [ref=e178]:
                      - textbox "cluster payout 2 count 6" [ref=e179]: "0"
              - paragraph [ref=e180]: Use arrows to move; Shift + arrows selects a rectangle. Copy or paste TSV values.
              - generic [ref=e181]:
                - generic [ref=e182]: "Minimum paying size: 1"
                - generic [ref=e183]: Applying updates the runtime lookup, exact precision, minimum-size pruning and isolated-cell handling together. Existing round caps and collection rules are retained.
              - paragraph [ref=e184]: Values are authored inputs. Evaluation happens through the existing compiler and analysis APIs after applying.
            - generic [ref=e185]:
              - generic [ref=e186]:
                - text: Test the applied source
                - paragraph [ref=e187]: A real round through the existing execution API. A single round does not estimate RTP.
              - generic [ref=e188]:
                - text: Seed
                - textbox "Test round seed" [ref=e189]: "42"
              - button "Play test round" [ref=e190] [cursor=pointer]:
                - img [ref=e191]
                - text: Play test round
        - generic [ref=e194]:
          - generic [ref=e195]: Applied to graph
          - generic [ref=e196]: Applied graph saved in this browser
```

# Test source

```ts
  1  | import { readFile } from 'node:fs/promises';
  2  | import { test, expect, type Page, type APIRequestContext } from './fixtures';
  3  | import { clusterStudioProof } from '../unit/fixtures/clusterStudioProof';
  4  | import { computedLineBoundProof } from '../unit/fixtures/computedLineBoundProof';
  5  | async function open(page: Page, graph: Record<string, unknown>) {
  6  |   await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles({ name: 'symbol-proof.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(graph)) }); await expect(page.locator('.project-name')).toHaveText(String(graph.name)); await page.getByRole('tab', { name: 'Studio', exact: true }).click();
  7  | }
  8  | async function backup(page: Page) { const pending = page.waitForEvent('download'); await page.getByRole('button', { name: 'Back up project', exact: true }).click(); const download = await pending; return JSON.parse(await readFile((await download.path())!, 'utf8')).graph as Record<string, unknown>; }
  9  | async function apply(page: Page) { const response = page.waitForResponse(response => new URL(response.url()).pathname === '/api/validate' && response.status() !== 429); await page.getByRole('button', { name: 'Validate & apply', exact: true }).click(); const result = await (await response).json(); expect(result, JSON.stringify(result)).toMatchObject({ isValid: true }); await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeEnabled(); }
  10 | async function exact(request: APIRequestContext, graph: Record<string, unknown>, payout: number) { const response = await request.post('/api/evaluate/light', { data: { config: graph, maxBranches: 10000, sampleSize: 1000 } }); expect(response.ok()).toBe(true); const result = await response.json(); expect(result.strategy).toBe('Exact'); expect(result.rtp).toBeCloseTo(payout, 12); }
  11 | test('Studio dictionary: stable extension preserves real payout, pending reload and referenced deletion shows its blockers', async ({ page, request }) => {
  12 |   const graph = clusterStudioProof(); await open(page, graph); await page.getByRole('tab', { name: 'Symbols', exact: true }).click(); await page.getByRole('button', { name: 'Add symbol', exact: true }).click(); await expect(page.getByLabel('New symbol ID')).toHaveValue('3'); await page.getByLabel('New symbol name').fill('Private C'); await page.getByRole('button', { name: 'Stage symbol', exact: true }).click(); await page.reload(); await apply(page);
  13 |   const changed = await backup(page); expect((changed.symbols as { id: string }[]).map(symbol => symbol.id)).toEqual(['0', '1', '2', '3']); expect(changed.paytables).toEqual(graph.paytables); await exact(request, changed, 0);
  14 |   const row = page.locator('.studio-symbol-dictionary article').filter({ has: page.getByLabel('Symbol 1 name', { exact: true }) }); await row.locator('summary').click(); await expect(row).toContainText('Paytable private-clusters'); await expect(page.getByRole('button', { name: 'Remove dictionary symbol 1', exact: true })).toBeDisabled();
  15 |   await page.getByRole('button', { name: 'Undo', exact: true }).click(); expect(await backup(page)).toEqual(graph);
  16 | });
  17 | for (const weighted of [false, true]) test(`Studio ${weighted ? 'weighted' : 'ordinary'} cluster eligibility: new B row becomes a real one-cell payout, removal retains drawing/dictionary source`, async ({ page, request }) => {
  18 |   const graph = clusterStudioProof(weighted); await open(page, graph); await page.getByRole('tab', { name: 'Paytables', exact: true }).click(); await page.locator('.studio-paying-domain summary').click(); await page.getByLabel('Domain new paying size').fill('1'); await page.getByRole('button', { name: 'Add paying size', exact: true }).click(); await page.getByLabel('New paying symbol').selectOption('2'); await page.getByRole('button', { name: 'Add paying symbol', exact: true }).click(); await page.getByLabel('Domain payout 2 count 1').fill('4'); await page.reload(); await apply(page);
  19 |   const added = await backup(page); await exact(request, added, 4); expect(added.symbols).toEqual(graph.symbols); expect((added.initialState as Record<string, unknown>).board).toEqual((graph.initialState as Record<string, unknown>).board);
> 20 |   await page.getByRole('button', { name: 'Remove paying symbol 1', exact: true }).click(); await apply(page); const removed = await backup(page); await exact(request, removed, 4); expect((removed.paytables as { entries: { symbolId: string }[] }[])[0].entries.map(row => row.symbolId)).toEqual(['2']); expect(removed.opaque).toEqual(graph.opaque);
     |                                                                                   ^ Error: locator.click: Test timeout of 30000ms exceeded.
  21 | });
  22 | test('Studio line eligibility: authored wild row reconciles computed bound and can be removed to restore its derived rule', async ({ page, request }) => {
  23 |   const graph = computedLineBoundProof(); await open(page, graph); await page.getByRole('tab', { name: 'Paytables', exact: true }).click(); await page.locator('.studio-paying-domain summary').click(); await page.getByLabel('New paying symbol').selectOption('0'); await page.getByRole('button', { name: 'Add paying symbol', exact: true }).click(); await page.getByLabel('Domain payout 0 count 2').fill('6'); await apply(page);
  24 |   const changed = await backup(page); expect((changed.nodes as { id: string; winCap?: number }[]).find(node => node.id === 'sink')!.winCap).toBe(35); await exact(request, changed, 7);
  25 |   await page.getByRole('button', { name: 'Remove paying symbol 0', exact: true }).click(); await apply(page); const removed = await backup(page); expect((removed.nodes as { id: string; winCap?: number }[]).find(node => node.id === 'sink')!.winCap).toBe(15); await exact(request, removed, 7);
  26 | });
  27 | 
```