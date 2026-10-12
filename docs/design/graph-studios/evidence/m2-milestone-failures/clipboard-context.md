# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: studio-spreadsheets.spec.ts >> Studio clipboard: keyboard rectangle selection copies exact values and an oversized paste leaves every input intact
- Location: e2e/studio-spreadsheets.spec.ts:83:1

# Error details

```
Error: expect(locator).toContainText(expected) failed

Locator: locator('.studio-matrix-keyboard')
Expected substring: "1 × 2"
Error: strict mode violation: locator('.studio-matrix-keyboard') resolved to 2 elements:
    1) <p class="studio-matrix-keyboard">Use arrows to move; Shift + arrows selects a rect…</p> aka getByText('Use arrows to move; Shift +')
    2) <p class="studio-matrix-keyboard">1 × 2 cells selected. Shift + arrows or Shift + c…</p> aka getByText('1 × 2 cells selected. Shift')

Call log:
  - Expect "toContainText" with timeout 5000ms
  - waiting for locator('.studio-matrix-keyboard')

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
            - button "Undo" [disabled] [ref=e81]
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
          - generic [ref=e91]: Current source
        - generic [ref=e92]:
          - complementary [ref=e93]:
            - generic [ref=e94]:
              - heading "Payout tables" [level=2] [ref=e95]
              - searchbox "Search Studio sources" [ref=e96]
            - button "Private clusters 1 symbol rows" [pressed] [ref=e97] [cursor=pointer]:
              - img [ref=e98]
              - generic [ref=e101]:
                - generic [ref=e102]: Private clusters
                - generic [ref=e103]: 1 symbol rows
            - generic [ref=e104]:
              - button "Back up project" [ref=e105] [cursor=pointer]:
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
                    - option "5 cells" [selected]
                    - option "6 cells"
                - button "Remove size" [ref=e137] [cursor=pointer]
              - group [ref=e138]:
                - generic "Paying symbols & sizes · 1 rows" [ref=e139] [cursor=pointer]
                - option "Choose a symbol" [selected]
                - option "Wild · 0"
                - option "B · 2"
                - option "5" [selected]
                - option "6"
              - table [ref=e141]:
                - rowgroup [ref=e142]:
                  - row "Paying symbol 5 group size 6 group size" [ref=e143]:
                    - columnheader "Paying symbol" [ref=e144]
                    - columnheader "5 group size" [ref=e145]:
                      - text: "5"
                      - generic [ref=e146]: group size
                    - columnheader "6 group size" [ref=e147]:
                      - text: "6"
                      - generic [ref=e148]: group size
                - rowgroup [ref=e149]:
                  - row "A A ID 1 10 20" [ref=e150]:
                    - rowheader "A A ID 1" [ref=e151]:
                      - generic [ref=e152]:
                        - generic [ref=e154]: A
                        - generic [ref=e155]:
                          - generic [ref=e156]: A
                          - generic [ref=e157]: ID 1
                    - cell "10" [ref=e158]:
                      - textbox "cluster payout 1 count 5" [ref=e159]: "10"
                    - cell "20" [ref=e160]:
                      - textbox "cluster payout 1 count 6" [active] [ref=e161]: "20"
              - paragraph [ref=e162]: 1 × 2 cells selected. Shift + arrows or Shift + click extends selection; paste TSV to stage a rectangle.
              - generic [ref=e163]:
                - generic [ref=e164]: "Minimum paying size: 5"
                - generic [ref=e165]: Applying updates the runtime lookup, exact precision, minimum-size pruning and isolated-cell handling together. Existing round caps and collection rules are retained.
              - paragraph [ref=e166]: Values are authored inputs. Evaluation happens through the existing compiler and analysis APIs after applying.
            - generic [ref=e167]:
              - generic [ref=e168]:
                - text: Test the applied source
                - paragraph [ref=e169]: A real round through the existing execution API. A single round does not estimate RTP.
              - generic [ref=e170]:
                - text: Seed
                - textbox "Test round seed" [ref=e171]: "42"
              - button "Play test round" [ref=e172] [cursor=pointer]:
                - img [ref=e173]
                - text: Play test round
        - generic [ref=e176]:
          - generic [ref=e177]: Current source
          - generic [ref=e178]: Applied graph saved in this browser
```

# Test source

```ts
  1  | import { readFile } from 'node:fs/promises';
  2  | import * as XLSX from 'xlsx';
  3  | import { test, expect, type Page, type APIRequestContext } from './fixtures';
  4  | import { clusterStudioProof } from '../unit/fixtures/clusterStudioProof';
  5  | import { prizeStudioProof } from '../unit/fixtures/prizeStudioProof';
  6  | import { computedLineBoundProof } from '../unit/fixtures/computedLineBoundProof';
  7  | 
  8  | async function open(page: Page, graph: Record<string, unknown>) {
  9  |   await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles({ name: 'private.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(graph)) });
  10 |   await expect(page.locator('.project-name')).toHaveText(String(graph.name)); await page.getByRole('tab', { name: 'Studio', exact: true }).click(); await page.getByRole('tab', { name: 'Paytables', exact: true }).click();
  11 | }
  12 | async function backup(page: Page) {
  13 |   const pending = page.waitForEvent('download'); await page.getByRole('button', { name: 'Back up project', exact: true }).click(); const download = await pending;
  14 |   return JSON.parse(await readFile((await download.path())!, 'utf8')).graph as Record<string, unknown>;
  15 | }
  16 | async function apply(page: Page) {
  17 |   const response = page.waitForResponse(response => new URL(response.url()).pathname === '/api/validate' && response.status() !== 429);
  18 |   await page.getByRole('button', { name: 'Validate & apply', exact: true }).click(); expect((await (await response).json()).isValid).toBe(true); await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeEnabled();
  19 | }
  20 | async function exact(request: APIRequestContext, graph: Record<string, unknown>, expected: number) {
  21 |   const response = await request.post('/api/evaluate/light', { data: { config: graph, maxBranches: 10000, sampleSize: 1000 } }); expect(response.ok()).toBe(true); const result = await response.json(); expect(result.strategy).toBe('Exact'); expect(result.rtp).toBeCloseTo(expected, 12);
  22 | }
  23 | function workbook(sheets: { name: string; rows: (string | number)[][]; formula?: { cell: string; expression: string; cached: number } }[]) {
  24 |   const book = XLSX.utils.book_new();
  25 |   for (const data of sheets) { const sheet = XLSX.utils.aoa_to_sheet(data.rows); if (data.formula) sheet[data.formula.cell] = { t: 'n', f: data.formula.expression, v: data.formula.cached }; XLSX.utils.book_append_sheet(book, sheet, data.name); }
  26 |   return XLSX.write(book, { bookType: 'xlsx', type: 'buffer', compression: true }) as Buffer;
  27 | }
  28 | async function clipboardText(page: Page, text: string) {
  29 |   await page.evaluate(text => { const input = document.createElement('textarea'); input.id = 'clipboard-test-buffer'; input.value = text; input.style.cssText = 'position:fixed;left:0;top:0;width:1px;height:1px;opacity:0'; document.body.appendChild(input); input.focus(); input.select(); }, text);
  30 |   await page.keyboard.press('Control+C'); await page.locator('#clipboard-test-buffer').evaluate(input => input.remove());
  31 | }
  32 | async function readCopiedText(page: Page) {
  33 |   await page.evaluate(() => { const input = document.createElement('textarea'); input.id = 'clipboard-test-buffer'; input.style.cssText = 'position:fixed;left:0;top:0;width:1px;height:1px;opacity:0'; document.body.appendChild(input); input.focus(); });
  34 |   await page.keyboard.press('Control+V'); const value = await page.locator('#clipboard-test-buffer').inputValue(); await page.locator('#clipboard-test-buffer').evaluate(input => input.remove()); return value;
  35 | }
  36 | test('Studio XLSX: select a worksheet, map arbitrary columns/names and convert exact units before executing a cluster edit', async ({ page, request }) => {
  37 |   const graph = clusterStudioProof(); await open(page, graph); await page.getByRole('button', { name: 'Import table', exact: true }).click();
  38 |   const bytes = workbook([{ name: 'Notes', rows: [['Notes'], ['Select Payouts']] }, { name: 'Payouts', rows: [['Authoring import'], ['Reward', 'Size', 'Name'], [6, 4, 'A']] }]);
  39 |   await page.getByLabel('Choose table file').setInputFiles({ name: 'cluster.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: bytes });
  40 |   await expect(page.getByLabel('Import worksheet').locator('option')).toHaveCount(2); await page.getByLabel('Import worksheet').selectOption({ label: 'Payouts' }); await page.getByLabel('Import header row').fill('2');
  41 |   await page.getByLabel('Map symbolId column').selectOption('2'); await page.getByLabel('Map count column').selectOption('1'); await page.getByLabel('Map value column').selectOption('0'); await page.getByLabel('Import symbol matching').selectOption('name'); await page.getByLabel('Import payout unit').selectOption('credits');
  42 |   await page.getByRole('button', { name: 'Preview exact values', exact: true }).click(); await expect(page.getByRole('button', { name: 'Stage import', exact: true })).toBeEnabled(); await expect(page.locator('.studio-import-preview')).toContainText('6');
  43 |   await page.getByRole('button', { name: 'Stage import', exact: true }).click(); expect(await backup(page)).toEqual(graph); await page.reload(); await apply(page); const changed = await backup(page);
  44 |   await exact(request, graph, 0); await exact(request, changed, 6); expect((changed.initialState as Record<string, unknown>).clusterMinSize).toBe(4);
  45 | });
  46 | test('Studio XLSX: real export stores exact cells as text and imports numeric prizes/independent weights without changing no-op source', async ({ page, request }) => {
  47 |   const graph = prizeStudioProof(); await open(page, graph); const pending = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export applied table', exact: true }).click(); const download = await pending, bytes = await readFile((await download.path())!);
  48 |   expect(bytes.subarray(0, 2).toString()).toBe('PK'); const book = XLSX.read(bytes, { type: 'buffer' }); expect(book.SheetNames).toEqual(['Inputs', 'Read me']); expect(book.Sheets.Inputs.A2.t).toBe('s'); expect(book.Sheets.Inputs.C2.t).toBe('s'); expect(book.Sheets.Inputs.F2.v).toBe('1');
  49 |   await page.getByRole('button', { name: 'Import table', exact: true }).click(); await page.getByLabel('Choose table file').setInputFiles({ name: 'no-op.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: bytes });
  50 |   await expect(page.getByLabel('Import worksheet').locator('option')).toHaveCount(2); await page.getByRole('button', { name: 'Preview exact values', exact: true }).click(); await expect(page.getByRole('button', { name: 'Stage import', exact: true })).toBeEnabled(); await page.getByRole('button', { name: 'Stage import', exact: true }).click();
  51 |   await expect(page.getByRole('button', { name: 'Validate & apply', exact: true })).toBeDisabled(); expect(await backup(page)).toEqual(graph); await page.getByRole('button', { name: 'Discard changes', exact: true }).click();
  52 |   book.Sheets.Inputs.C2.v = '3'; book.Sheets.Inputs.G2.v = '1'; const changedFile = XLSX.write(book, { type: 'buffer', bookType: 'xlsx' }) as Buffer;
  53 |   await page.getByRole('button', { name: 'Import table', exact: true }).click(); await page.getByLabel('Choose table file').setInputFiles({ name: 'prizes.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: changedFile });
  54 |   await expect(page.getByLabel('Import worksheet').locator('option')).toHaveCount(2); await page.getByRole('button', { name: 'Preview exact values', exact: true }).click(); await expect(page.getByRole('button', { name: 'Stage import', exact: true })).toBeEnabled(); await page.getByRole('button', { name: 'Stage import', exact: true }).click(); await apply(page); await exact(request, await backup(page), 8);
  55 | });
  56 | test('Studio XLSX: formulas and unsafe/invalid cells expose their row/address and never apply a partial import', async ({ page }) => {
  57 |   const graph = clusterStudioProof(); await open(page, graph); await page.getByRole('button', { name: 'Import table', exact: true }).click();
  58 |   const bytes = workbook([{ name: 'Inputs', rows: [['symbolId', 'count', 'value'], ['1', '4', 6]], formula: { cell: 'C2', expression: '3+3', cached: 6 } }]);
  59 |   await page.getByLabel('Choose table file').setInputFiles({ name: 'formula.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: bytes }); await expect(page.locator('.studio-import-file')).toContainText('Loaded');
  60 |   await page.getByRole('button', { name: 'Preview exact values', exact: true }).click(); await expect(page.getByRole('dialog').getByRole('alert')).toContainText('C2: Formula cell rejected'); await expect(page.getByRole('button', { name: 'Stage import', exact: true })).toBeDisabled(); await page.getByRole('button', { name: 'Close import', exact: true }).click(); expect(await backup(page)).toEqual(graph);
  61 | });
  62 | test('Studio XLSX: original numeric XML precision is checked before a rounded parser value could become a valid credit', async ({ page }) => {
  63 |   const graph = clusterStudioProof(); await open(page, graph); await page.getByRole('button', { name: 'Import table', exact: true }).click();
  64 |   const book = XLSX.utils.book_new(), sheet = XLSX.utils.aoa_to_sheet([['symbolId', 'count', 'value'], ['1', '4', 1]]); sheet.C2 = { t: 'n', v: '1.000000000000000001' }; XLSX.utils.book_append_sheet(book, sheet, 'Inputs');
  65 |   const bytes = XLSX.write(book, { type: 'buffer', bookType: 'xlsx' }) as Buffer;
  66 |   expect(XLSX.read(bytes, { type: 'buffer' }).Sheets.Inputs.C2.v).toBe(1);
  67 |   await page.getByLabel('Choose table file').setInputFiles({ name: 'precision.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: bytes }); await expect(page.locator('.studio-import-file')).toContainText('Loaded');
  68 |   await expect(page.getByLabel('CSV import input')).toHaveValue(/1\.000000000000000001/); await page.getByRole('button', { name: 'Preview exact values', exact: true }).click(); await expect(page.getByRole('dialog').getByRole('alert')).toContainText('whole credits'); await expect(page.getByRole('button', { name: 'Stage import', exact: true })).toBeDisabled(); await page.getByRole('button', { name: 'Close import', exact: true }).click(); expect(await backup(page)).toEqual(graph);
  69 | });
  70 | test('Studio XLSX: round-price amounts convert to credits and reconcile the real computed payout bound', async ({ page, request }) => {
  71 |   const graph = computedLineBoundProof(); await open(page, graph); await page.getByRole('button', { name: 'Import table', exact: true }).click();
  72 |   await page.getByLabel('Choose table file').setInputFiles({ name: 'round-units.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: workbook([{ name: 'Inputs', rows: [['symbolId', 'count', 'value'], ['1', 2, 3]] }]) }); await expect(page.locator('.studio-import-file')).toContainText('Loaded');
  73 |   await page.getByLabel('Import payout unit').selectOption('round'); await page.getByRole('button', { name: 'Preview exact values', exact: true }).click(); await expect(page.getByRole('button', { name: 'Stage import', exact: true })).toBeEnabled(); await page.getByRole('button', { name: 'Stage import', exact: true }).click(); await apply(page);
  74 |   const changed = await backup(page); expect((changed.nodes as { id: string; winCap?: number }[]).find(node => node.id === 'sink')!.winCap).toBe(35); await exact(request, changed, 21);
  75 | });
  76 | test('Studio clipboard: TSV paste stages a payout batch and the real result changes', async ({ page, request }) => {
  77 |   const graph = computedLineBoundProof(); await open(page, graph); const cell = page.getByLabel('line payout 1 count 2', { exact: true }); await expect(cell).toBeEnabled(); await cell.focus();
  78 |   await clipboardText(page, '6\n'); await cell.focus(); await page.keyboard.press('Control+V');
  79 |   await expect(cell).toHaveValue('6'); await apply(page); await exact(request, await backup(page), 21);
  80 |   await page.getByRole('button', { name: 'Import table', exact: true }).click(); await page.getByLabel('Paste table format').selectOption('tsv'); await page.getByLabel('CSV import input').fill('symbolId\tcount\tvalue\n1\t2\t2');
  81 |   await page.getByRole('button', { name: 'Preview exact values', exact: true }).click(); await expect(page.getByRole('button', { name: 'Stage import', exact: true })).toBeEnabled(); await page.getByRole('button', { name: 'Stage import', exact: true }).click(); await apply(page); await exact(request, await backup(page), 7);
  82 | });
  83 | test('Studio clipboard: keyboard rectangle selection copies exact values and an oversized paste leaves every input intact', async ({ page }) => {
  84 |   const graph = clusterStudioProof(); await open(page, graph); const first = page.getByLabel('cluster payout 1 count 5', { exact: true }), second = page.getByLabel('cluster payout 1 count 6', { exact: true });
> 85 |   await expect(first).toBeEnabled(); await first.focus(); await first.press('Shift+ArrowRight'); await expect(second).toBeFocused(); await expect(page.locator('.studio-matrix-keyboard')).toContainText('1 × 2');
     |                                                                                                                                                                                            ^ Error: expect(locator).toContainText(expected) failed
  86 |   await page.keyboard.press('Control+C'); expect(await readCopiedText(page)).toBe('"10"\t"20"');
  87 |   await clipboardText(page, '1\t2\t3'); await first.focus(); await page.keyboard.press('Control+V');
  88 |   await expect(page.getByRole('alert')).toContainText('No input has changed'); await expect(first).toHaveValue('10'); await expect(second).toHaveValue('20'); expect(await backup(page)).toEqual(graph);
  89 | });
  90 | 
```