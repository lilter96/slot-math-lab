# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: studio-source-lifecycle.spec.ts >> Studio line lifecycle: duplicate/remove lines preserve per-line pricing or explicitly keep a fixed total, and execute the real payout
- Location: e2e/studio-source-lifecycle.spec.ts:39:1

# Error details

```
Test timeout of 30000ms exceeded.
```

```
Error: page.waitForResponse: Test timeout of 30000ms exceeded.
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
      - generic [ref=e11]: Computed bound proof
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
      - generic [ref=e22]: Computed bound proof
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
              - text: Computed bound proof
              - generic [ref=e78]: Private project
            - heading "Reel & Paytable Studio" [level=1] [ref=e79]
          - generic [ref=e80]:
            - button "Undo" [disabled] [ref=e81]
            - button "Validate & apply" [disabled] [ref=e82]
            - link "Run analysis ↗" [disabled] [ref=e83]:
              - /url: /simulate
              - text: Run analysis
              - generic [ref=e84]: ↗
        - generic [ref=e85]:
          - tablist "Studio views" [ref=e86]:
            - tab "Sources" [ref=e87] [cursor=pointer]
            - tab "Paytables" [ref=e88] [cursor=pointer]
            - tab "Paylines" [selected] [ref=e89] [cursor=pointer]
            - tab "Symbols" [ref=e90] [cursor=pointer]
          - generic [ref=e91]: Draft needs attention
        - generic [ref=e92]:
          - complementary [ref=e93]:
            - generic [ref=e94]:
              - heading "Line sets" [level=2] [ref=e95]
              - searchbox "Search Studio sources" [ref=e96]
            - button "Lines 2 paths" [pressed] [ref=e97] [cursor=pointer]:
              - img [ref=e98]
              - generic [ref=e103]:
                - generic [ref=e104]: Lines
                - generic [ref=e105]: 2 paths
            - generic [ref=e106]:
              - button "Back up project" [ref=e107] [cursor=pointer]:
                - img [ref=e108]
                - text: Back up project
              - link "Switch private project" [ref=e111] [cursor=pointer]:
                - /url: /projects
          - main [ref=e112]:
            - generic [ref=e113]:
              - generic [ref=e114]:
                - text: Lines
                - generic [ref=e115]: Executable board paths
              - button "Source details" [ref=e116] [cursor=pointer]:
                - text: Source details
                - img [ref=e117]
            - generic [ref=e119]:
              - generic [ref=e120]:
                - button "Import table" [ref=e121] [cursor=pointer]
                - button "Copy TSV" [ref=e122] [cursor=pointer]
              - generic [ref=e123]:
                - generic [ref=e124]:
                  - text: Export format
                  - combobox "Table export format" [ref=e125]:
                    - option "Excel · XLSX" [selected]
                    - option "CSV"
                    - option "TSV"
                - button "Export applied table" [ref=e126] [cursor=pointer]
            - generic [ref=e127]:
              - generic [ref=e128]:
                - button "Import table" [ref=e129] [cursor=pointer]
                - button "Copy TSV" [ref=e130] [cursor=pointer]
              - generic [ref=e131]:
                - generic [ref=e132]:
                  - text: Export format
                  - combobox "Table export format" [ref=e133]:
                    - option "Excel · XLSX" [selected]
                    - option "CSV"
                    - option "TSV"
                - button "Export applied table" [ref=e134] [cursor=pointer]
            - generic [ref=e135]:
              - generic [ref=e136]:
                - button "Import table" [ref=e137] [cursor=pointer]
                - button "Copy TSV" [ref=e138] [cursor=pointer]
              - generic [ref=e139]:
                - generic [ref=e140]:
                  - text: Export format
                  - combobox "Table export format" [ref=e141]:
                    - option "Excel · XLSX" [selected]
                    - option "CSV"
                    - option "TSV"
                - button "Export applied table" [ref=e142] [cursor=pointer]
            - generic [ref=e143]:
              - generic [ref=e144]:
                - button "Import table" [disabled] [ref=e145]
                - button "Copy TSV" [ref=e146] [cursor=pointer]
              - generic [ref=e147]:
                - generic [ref=e148]:
                  - text: Export format
                  - combobox "Table export format" [ref=e149]:
                    - option "Excel · XLSX" [selected]
                    - option "CSV"
                    - option "TSV"
                - button "Export applied table" [ref=e150] [cursor=pointer]
            - generic [ref=e151]:
              - generic [ref=e152]:
                - button "Import table" [disabled] [ref=e153]
                - button "Copy TSV" [ref=e154] [cursor=pointer]
              - generic [ref=e155]:
                - generic [ref=e156]:
                  - text: Export format
                  - combobox "Table export format" [ref=e157]:
                    - option "Excel · XLSX" [selected]
                    - option "CSV"
                    - option "TSV"
                - button "Export applied table" [ref=e158] [cursor=pointer]
            - generic [ref=e159]:
              - generic [ref=e160]:
                - button "Import table" [ref=e161] [cursor=pointer]
                - button "Copy TSV" [ref=e162] [cursor=pointer]
              - generic [ref=e163]:
                - generic [ref=e164]:
                  - text: Export format
                  - combobox "Table export format" [ref=e165]:
                    - option "Excel · XLSX" [selected]
                    - option "CSV"
                    - option "TSV"
                - button "Export applied table" [ref=e166] [cursor=pointer]
            - generic [ref=e167]:
              - generic [ref=e168]:
                - button "Import table" [disabled] [ref=e169]
                - button "Copy TSV" [ref=e170] [cursor=pointer]
              - generic [ref=e171]:
                - generic [ref=e172]:
                  - text: Export format
                  - combobox "Table export format" [ref=e173]:
                    - option "Excel · XLSX" [selected]
                    - option "CSV"
                    - option "TSV"
                - button "Export applied table" [ref=e174] [cursor=pointer]
            - generic [ref=e175]:
              - generic [ref=e176]:
                - button "Import table" [ref=e177] [cursor=pointer]
                - button "Copy TSV" [ref=e178] [cursor=pointer]
              - generic [ref=e179]:
                - generic [ref=e180]:
                  - text: Export format
                  - combobox "Table export format" [ref=e181]:
                    - option "Excel · XLSX" [selected]
                    - option "CSV"
                    - option "TSV"
                - button "Export applied table" [ref=e182] [cursor=pointer]
            - generic [ref=e183]:
              - generic [ref=e184]:
                - button "Import table" [ref=e185] [cursor=pointer]
                - button "Copy TSV" [ref=e186] [cursor=pointer]
              - generic [ref=e187]:
                - generic [ref=e188]:
                  - text: Export format
                  - combobox "Table export format" [ref=e189]:
                    - option "Excel · XLSX" [selected]
                    - option "CSV"
                    - option "TSV"
                - button "Export applied table" [ref=e190] [cursor=pointer]
            - generic [ref=e191]:
              - generic [ref=e192]:
                - button "Import table" [ref=e193] [cursor=pointer]
                - button "Copy TSV" [ref=e194] [cursor=pointer]
              - generic [ref=e195]:
                - generic [ref=e196]:
                  - text: Export format
                  - combobox "Table export format" [ref=e197]:
                    - option "Excel · XLSX" [selected]
                    - option "CSV"
                    - option "TSV"
                - button "Export applied table" [ref=e198] [cursor=pointer]
            - generic [ref=e199]:
              - generic [ref=e200]:
                - button "Import table" [ref=e201] [cursor=pointer]
                - button "Copy TSV" [ref=e202] [cursor=pointer]
              - generic [ref=e203]:
                - generic [ref=e204]:
                  - text: Export format
                  - combobox "Table export format" [ref=e205]:
                    - option "Excel · XLSX" [selected]
                    - option "CSV"
                    - option "TSV"
                - button "Export applied table" [ref=e206] [cursor=pointer]
            - generic [ref=e207]:
              - generic [ref=e208]:
                - button "Import table" [disabled] [ref=e209]
                - button "Copy TSV" [ref=e210] [cursor=pointer]
              - generic [ref=e211]:
                - generic [ref=e212]:
                  - text: Export format
                  - combobox "Table export format" [ref=e213]:
                    - option "Excel · XLSX" [selected]
                    - option "CSV"
                    - option "TSV"
                - button "Export applied table" [ref=e214] [cursor=pointer]
            - generic [ref=e215]:
              - generic [ref=e216]:
                - button "Import table" [disabled] [ref=e217]
                - button "Copy TSV" [ref=e218] [cursor=pointer]
              - generic [ref=e219]:
                - generic [ref=e220]:
                  - text: Export format
                  - combobox "Table export format" [ref=e221]:
                    - option "Excel · XLSX" [selected]
                    - option "CSV"
                    - option "TSV"
                - button "Export applied table" [ref=e222] [cursor=pointer]
            - generic [ref=e223]:
              - generic [ref=e224]:
                - button "Import table" [ref=e225] [cursor=pointer]
                - button "Copy TSV" [ref=e226] [cursor=pointer]
              - generic [ref=e227]:
                - generic [ref=e228]:
                  - text: Export format
                  - combobox "Table export format" [ref=e229]:
                    - option "Excel · XLSX" [selected]
                    - option "CSV"
                    - option "TSV"
                - button "Export applied table" [ref=e230] [cursor=pointer]
            - generic [ref=e231]:
              - generic [ref=e232]:
                - button "Import table" [ref=e233] [cursor=pointer]
                - button "Copy TSV" [ref=e234] [cursor=pointer]
              - generic [ref=e235]:
                - generic [ref=e236]:
                  - text: Export format
                  - combobox "Table export format" [ref=e237]:
                    - option "Excel · XLSX" [selected]
                    - option "CSV"
                    - option "TSV"
                - button "Export applied table" [ref=e238] [cursor=pointer]
            - generic [ref=e239]:
              - generic [ref=e240]:
                - generic [ref=e241]:
                  - heading "Payline paths" [level=2] [ref=e242]
                  - paragraph [ref=e243]: 1 lines · one row per column · 2 columns × 1 rows
                - generic [ref=e244]: 2 → 1 lines
              - generic [ref=e245]:
                - generic [ref=e246]:
                  - button "Add line" [ref=e247] [cursor=pointer]
                  - button "Duplicate selected line" [ref=e248] [cursor=pointer]
                  - button "Remove selected line" [disabled] [ref=e249]
                - generic [ref=e250]:
                  - generic [ref=e251]:
                    - text: Line stake policy
                    - combobox "Line stake policy" [ref=e252]:
                      - option "Preserve the current credits per line"
                      - option "Keep a fixed total stake · change the pricing contract" [selected]
                  - generic [ref=e253]:
                    - checkbox "Confirm fixed total line stake" [ref=e254]
                    - text: Confirm changing the effective price per line
                  - generic [ref=e255]:
                    - text: Line-count payout cap
                    - combobox "Line-count payout cap" [ref=e256]:
                      - option "Recompute the proven upper bound" [selected]
                      - option "Keep the current game-rule cap"
                  - paragraph [ref=e257]: Confirm changing the stake contract to a fixed total; this changes the effective price per line.
              - generic [ref=e258]:
                - navigation "Payline selection" [ref=e259]:
                  - button "Line 1 1 · 1" [pressed] [ref=e260] [cursor=pointer]:
                    - generic [ref=e261]: Line 1
                    - generic [ref=e262]: 1 · 1
                - generic [ref=e263]:
                  - generic [ref=e264]:
                    - img
                    - button "Line 1 column 1 row 1" [pressed] [ref=e265] [cursor=pointer]:
                      - generic [ref=e266]: 1:1
                    - button "Line 1 column 2 row 1" [pressed] [ref=e267] [cursor=pointer]:
                      - generic [ref=e268]: 2:1
                  - heading "Line 1" [level=3] [ref=e269]
                  - generic [ref=e270]:
                    - generic [ref=e271]:
                      - text: Column 1
                      - combobox "Column 1" [ref=e272]:
                        - option "Row 1" [selected]
                    - generic [ref=e273]:
                      - text: Column 2
                      - combobox "Column 2" [ref=e274]:
                        - option "Row 1" [selected]
                  - paragraph [ref=e275]: Applying synchronizes this path with every proven Library consumer. Board offsets use column-major encoding.
            - generic [ref=e276]:
              - generic [ref=e277]:
                - text: Test the applied source
                - paragraph [ref=e278]: A real round through the existing execution API. A single round does not estimate RTP.
              - generic [ref=e279]:
                - text: Seed
                - textbox "Test round seed" [ref=e280]: "42"
              - button "Play test round" [disabled] [ref=e281]:
                - img [ref=e282]
                - text: Play test round
        - generic [ref=e284]:
          - generic [ref=e285]:
            - generic [ref=e286]: Draft needs attention
            - generic [ref=e287]: Staged inputs save separately; the applied graph stays unchanged until validation succeeds.
          - generic [ref=e288]:
            - button "Download staged draft" [ref=e289] [cursor=pointer]
            - button "Discard changes" [ref=e290] [cursor=pointer]
          - group [ref=e291]:
            - generic "Review 1 staged changes" [ref=e292] [cursor=pointer]
          - alert [ref=e293]: Confirm changing the stake contract to a fixed total; this changes the effective price per line.
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
     |                                                          ^ Error: page.waitForResponse: Test timeout of 30000ms exceeded.
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