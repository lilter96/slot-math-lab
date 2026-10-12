# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: reel-paytable-studio.spec.ts >> Studio: the selected X7 prize table and its pending values recover immediately instead of returning to line payouts
- Location: e2e/reel-paytable-studio.spec.ts:105:1

# Error details

```
Error: expect(locator).toBeVisible() failed

Locator: getByRole('heading', { name: 'Coin prizes', exact: true })
Expected: visible
Timeout: 5000ms
Error: element(s) not found

Call log:
  - Expect "toBeVisible" with timeout 5000ms
  - waiting for getByRole('heading', { name: 'Coin prizes', exact: true })

```

```yaml
- link "Skip to main content":
  - /url: "#main-content"
- text: Slot Math Lab
- link "Choose project":
  - /url: /projects
  - text: X7 Club
- button "AI assist"
- link "Games":
  - /url: /games.html
  - img
  - text: Games
- button "Appearance settings": ⚙
- complementary "Workspace":
  - button "Collapse navigation" [expanded]
  - text: X7 Club
  - tablist "Main navigation":
    - tab "My projects":
      - img
      - text: My projects
    - tab "Overview":
      - img
      - text: Overview
    - tab "Model data":
      - img
      - text: Model data
    - tab "Studio" [selected]:
      - img
      - text: Studio
    - tab "Features":
      - img
      - text: Features
    - tab "Build":
      - img
      - text: Build
    - tab "Simulate":
      - img
      - text: Simulate
    - tab "Results":
      - img
      - text: Results
    - tab "Export":
      - img
      - text: Export
    - tab "Debug":
      - img
      - text: Debug
    - tab "Reference demo":
      - img
      - text: Reference demo
- main:
  - region "Reel & Paytable Studio":
    - paragraph: X7 Club Private project
    - heading "Reel & Paytable Studio" [level=1]
    - button "Undo" [disabled]
    - button "Validate & apply"
    - link "Run analysis ↗" [disabled]:
      - /url: /simulate
    - tablist "Studio views":
      - tab "Sources"
      - tab "Paytables" [selected]
      - tab "Paylines"
      - tab "Symbols"
    - text: 1 pending change
    - complementary:
      - heading "Payout tables" [level=2]
      - searchbox "Search Studio sources"
      - button "Lines 5 symbol rows" [pressed]:
        - img
        - text: Lines 5 symbol rows
      - button "Coin prizes 1 symbol rows":
        - img
        - text: Coin prizes 1 symbol rows
      - button "Back up project":
        - img
        - text: Back up project
      - link "Switch private project":
        - /url: /projects
    - main:
      - text: Lines Exact authored payouts
      - button "Source details":
        - text: Source details
        - img
      - heading "Line payouts" [level=2]
      - paragraph: Columns mean matching reels from the left · line credits
      - text: "Round price: 20 credits"
      - table:
        - rowgroup:
          - row "Paying symbol 3 matching reels 4 matching reels 5 matching reels":
            - columnheader "Paying symbol"
            - columnheader "3 matching reels"
            - columnheader "4 matching reels"
            - columnheader "5 matching reels"
        - rowgroup:
          - row "CH CHILL ID 0 4 12 40":
            - rowheader "CH CHILL ID 0"
            - cell "4":
              - textbox "line payout 0 count 3": "4"
            - cell "12":
              - textbox "line payout 0 count 4": "12"
            - cell "40":
              - textbox "line payout 0 count 5": "40"
          - row "HY HYPE ID 1 5 15 50":
            - rowheader "HY HYPE ID 1"
            - cell "5":
              - textbox "line payout 1 count 3": "5"
            - cell "15":
              - textbox "line payout 1 count 4": "15"
            - cell "50":
              - textbox "line payout 1 count 5": "50"
          - row "LO LOL ID 2 6 20 70":
            - rowheader "LO LOL ID 2"
            - cell "6":
              - textbox "line payout 2 count 3": "6"
            - cell "20":
              - textbox "line payout 2 count 4": "20"
            - cell "70":
              - textbox "line payout 2 count 5": "70"
          - row "GG GG ID 3 8 25 90":
            - rowheader "GG GG ID 3"
            - cell "8":
              - textbox "line payout 3 count 3": "8"
            - cell "25":
              - textbox "line payout 3 count 4": "25"
            - cell "90":
              - textbox "line payout 3 count 5": "90"
          - row "SE SEVEN ID 4 12 40 150":
            - rowheader "SE SEVEN ID 4"
            - cell "12":
              - textbox "line payout 4 count 3": "12"
            - cell "40":
              - textbox "line payout 4 count 4": "40"
            - cell "150":
              - textbox "line payout 4 count 5": "150"
      - text: All-wild row is derived Each matching length pays the largest authored symbol payout of that length. Applying updates this runtime row together with the exact precision declaration. Payout cap policy
      - combobox "Payout cap policy":
        - option "Keep the current cap as a game rule" [selected]
      - paragraph: "Current cap: 7777 round-price multiples. Keeping it can limit newly increased payouts."
      - paragraph: Values are authored inputs. Evaluation happens through the existing compiler and analysis APIs after applying.
      - text: Test the applied source
      - paragraph: A real round through the existing execution API. A single round does not estimate RTP.
      - text: Seed
      - textbox "Test round seed": "42"
      - button "Play test round" [disabled]:
        - img
        - text: Play test round
    - text: 1 pending change Staged inputs save separately; the applied graph stays unchanged until validation succeeds.
    - button "Download staged draft"
    - button "Discard changes"
    - group: Review 1 staged changes
```

# Test source

```ts
  7   | import { linePayTable, paylines, paylineState } from '../src/games/shared/graph/mechanics/paylines';
  8   | import { computedLineBoundProof } from '../unit/fixtures/computedLineBoundProof';
  9   | import { graphStudiosCorpus } from '../unit/fixtures/graphStudiosCorpus';
  10  | import { clusterStudioProof } from '../unit/fixtures/clusterStudioProof';
  11  | import { prizeStudioProof } from '../unit/fixtures/prizeStudioProof';
  12  | import { createX7ClubGraph } from '../src/games/x7-club/graph';
  13  | function reels() {
  14  |   const spec = { name: 'private-reels', stripKey: 'privateStrip', rows: 2, strips: [['A', 'B', 'C']] }, mechanics = new Mechanics();
  15  |   const root = new Flow().use('spin', 'Spin the private reel', mechanics.add(spinReels(spec))).write('pay', 'AB pays one', 'coins', when(and(eq(at('board', num(0)), str('A')), eq(at('board', num(1)), str('B'))), num(1), num(0))).sink('sink', 'Payout', 'coins', 1);
  16  |   return gameGraph({ id: 'studio-reel-ui-proof', name: 'Private reel UI proof', description: 'Three physical windows: AB, BC and CA. Only AB pays.', root, mechanics, initialState: { coins: 0, ...reelState(spec) }, tables: { boardConfig: { rows: 2, columns: 1 }, symbols: ['A', 'B', 'C'].map(id => ({ id, name: id, kind: 'Standard' })), opaque: { retained: true } } });
  17  | }
  18  | function cells() {
  19  |   const mechanics = new Mechanics(), root = new Flow().use('draw', 'Independent board cells', mechanics.add(spinCells({ name: 'private-cells', cells: 2, outcomes: [['1', 3], ['2', 1]] })))
  20  |     .use('lines', 'AA pays one', mechanics.add(paylines(mechanics, [[0, 0]], 1))).sink('sink', 'Payout', 'lineCoins', 1);
  21  |   return gameGraph({ id: 'studio-cell-ui-proof', name: 'Private cell UI proof', description: 'Two independent symbols. AA pays one.', root, mechanics, initialState: { ...cellState({ cells: 2 }), ...paylineState(0, linePayTable({ symbolCount: 3, wild: 0, pays: { 1: { 2: 1 } }, reels: 2 })) }, tables: { boardConfig: { rows: 1, columns: 2 }, symbols: [{ id: '0', name: 'Wild', kind: 'Wild' }, { id: '1', name: 'A', kind: 'Standard' }, { id: '2', name: 'B', kind: 'Standard' }] } });
  22  | }
  23  | async function open(page: Page, graph: Record<string, unknown>) {
  24  |   await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles({ name: 'private-graph.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(graph)) });
  25  |   await expect(page.locator('.project-name')).toHaveText(String(graph.name)); await page.getByRole('tab', { name: 'Studio', exact: true }).click();
  26  |   await expect(page.getByRole('heading', { name: 'Reel & Paytable Studio', exact: true })).toBeVisible();
  27  | }
  28  | async function backup(page: Page) {
  29  |   const pending = page.waitForEvent('download'); await page.getByRole('button', { name: 'Back up project', exact: true }).click(); const download = await pending, path = await download.path();
  30  |   return JSON.parse(await readFile(path!, 'utf8')) as { graph: Record<string, unknown>; views: unknown };
  31  | }
  32  | async function apply(page: Page) {
  33  |   const response = page.waitForResponse(response => new URL(response.url()).pathname === '/api/validate' && response.status() !== 429);
  34  |   await page.getByRole('button', { name: 'Validate & apply', exact: true }).click(); const result = await response; expect(result.ok()).toBe(true); expect((await result.json()).isValid).toBe(true);
  35  |   await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeEnabled();
  36  | }
  37  | async function exact(api: APIRequestContext, graph: Record<string, unknown>, value: number) {
  38  |   const response = await api.post('/api/evaluate/light', { data: { config: graph, maxBranches: 10000, sampleSize: 1000 } }); expect(response.ok()).toBe(true); const result = await response.json(); expect(result.strategy).toBe('Exact'); expect(result.rtp).toBeCloseTo(value, 12);
  39  | }
  40  | test('Studio: ordered reel changes stay staged, survive immediate reload, validate against the real compiler and undo completely', async ({ page, request }) => {
  41  |   const graph = reels(); await open(page, graph); await expect(page.getByRole('button', { name: 'Move selected stop down' })).toBeEnabled();
  42  |   await page.getByRole('button', { name: 'Move selected stop down' }).click(); expect((await backup(page)).graph).toEqual(graph);
  43  |   await page.reload(); await expect(page.getByLabel('Selected stop symbol')).toHaveValue('B');
  44  |   await apply(page); const changed = (await backup(page)).graph; await exact(request, graph, 1 / 3); await exact(request, changed, 0);
  45  |   expect((changed.initialState as Record<string, unknown>).privateStrip0).toEqual(['B', 'A', 'C', 'B']); expect(changed.opaque).toEqual(graph.opaque);
  46  |   await page.getByRole('button', { name: 'Undo', exact: true }).click(); expect((await backup(page)).graph).toEqual(graph);
  47  | });
  48  | test('Studio: one board cell changes real probability while independent peers and zero payloads stay intact', async ({ page, request }) => {
  49  |   const graph = cells(); await open(page, graph);
  50  |   const first = page.getByLabel('Weight 1 in cell-1', { exact: true }); await expect(first).toBeEnabled(); await first.fill('1'); await page.getByLabel('Weight 2 in cell-1', { exact: true }).fill('3');
  51  |   await page.reload(); await expect(first).toHaveValue('1'); await expect(page.getByLabel('Weight 2 in cell-1', { exact: true })).toHaveValue('3');
  52  |   await apply(page); const changed = (await backup(page)).graph; await exact(request, graph, 9 / 16); await exact(request, changed, 3 / 16);
  53  |   const oldNodes = ((graph.mechanics as Record<string, { nodes: Record<string, unknown>[] }>)['private-cells']).nodes, newNodes = ((changed.mechanics as Record<string, { nodes: Record<string, unknown>[] }>)['private-cells']).nodes;
  54  |   expect(newNodes[1]).toEqual(oldNodes[1]); expect(newNodes[0].drawWeights).toEqual([{ outcomeId: '1', weight: 1, value: 0 }, { outcomeId: '2', weight: 3, value: 0 }]);
  55  | });
  56  | test('Studio: invalid text/imports never change source and closing the inspector retains staged inputs', async ({ page }) => {
  57  |   const graph = cells(); await open(page, graph); const weight = page.getByLabel('Weight 1 in cell-1', { exact: true }); await expect(weight).toBeEnabled(); await weight.fill('');
  58  |   await expect(page.getByRole('button', { name: 'Validate & apply', exact: true })).toBeDisabled(); await expect(page.getByRole('alert')).toContainText('whole weights');
  59  |   await page.getByRole('button', { name: 'Source details', exact: true }).click(); await expect(page.getByLabel('Source details', { exact: true })).toBeVisible(); await page.keyboard.press('Escape'); await expect(page.getByLabel('Source details', { exact: true })).toHaveCount(0); await expect(weight).toHaveValue('');
  60  |   await page.getByRole('button', { name: 'Paste / import', exact: true }).click(); await page.getByLabel('CSV import input').fill('outcomeId,weight\n1,9007199254740993\n2,1'); await page.getByRole('button', { name: 'Preview exact values' }).click();
  61  |   await expect(page.getByRole('dialog').getByRole('alert')).toContainText('exact integer'); await expect(page.getByRole('button', { name: 'Stage import' })).toBeDisabled(); await page.getByRole('button', { name: 'Close import' }).click(); expect((await backup(page)).graph).toEqual(graph);
  62  | });
  63  | test('Studio: a payout increase updates its computed bound and the real executed result without clipping the new win', async ({ page, request }) => {
  64  |   const graph = computedLineBoundProof(); await open(page, graph); await page.getByRole('tab', { name: 'Paytables', exact: true }).click();
  65  |   const field = page.getByLabel('line payout 1 count 2', { exact: true }); await expect(field).toBeEnabled(); await field.fill('6');
  66  |   await apply(page); const changed = (await backup(page)).graph;
  67  |   expect((changed.nodes as { id: string; winCap?: number }[]).find(node => node.id === 'sink')!.winCap).toBe(35);
  68  |   await exact(request, graph, 7); await exact(request, changed, 21);
  69  | });
  70  | test('Studio: explicitly keeping the existing cap preserves its actual clipping rule', async ({ page, request }) => {
  71  |   const graph = computedLineBoundProof(); await open(page, graph); await page.getByRole('tab', { name: 'Paytables', exact: true }).click();
  72  |   const field = page.getByLabel('line payout 1 count 2', { exact: true }); await expect(field).toBeEnabled(); await page.getByLabel('Payout cap policy').selectOption('keep'); await field.fill('6');
  73  |   await apply(page); const changed = (await backup(page)).graph; expect((changed.nodes as { id: string; winCap?: number }[]).find(node => node.id === 'sink')!.winCap).toBe(15); await exact(request, changed, 15);
  74  | });
  75  | test('Studio: dynamic weights retain their expression and do not display static ratios from fallback rows', async ({ page }) => {
  76  |   const graph = cells(), scope = (graph.mechanics as Record<string, { nodes: Record<string, unknown>[]; expressions: Record<string, unknown> }>)['private-cells'];
  77  |   scope.nodes[0].weightExpressionId = 'dynamicWeights'; scope.expressions.dynamicWeights = { exprType: 'fieldAccess', target: 'state', path: ['runtimeWeights'] };
  78  |   await open(page, graph); await expect(page.locator('.weight-probability').filter({ hasText: 'State-dependent' })).toHaveCount(2); await expect(page.getByLabel('Weight 1 in cell-1', { exact: true })).toBeDisabled();
  79  |   expect((await page.locator('.weight-probability').allTextContents()).join(' ')).not.toContain('%'); expect((await backup(page)).graph).toEqual(graph);
  80  | });
  81  | test('Studio: all 65 game/variant/reference/custom corpus graphs open and export without changing executable source', async ({ page }) => {
  82  |   test.setTimeout(240000); const { graphs } = graphStudiosCorpus(); expect(graphs).toHaveLength(65);
  83  |   for (const { caseId, graph } of graphs) {
  84  |     await open(page, graph as Record<string, unknown>); expect((await backup(page)).graph, caseId).toEqual(graph);
  85  |   }
  86  | });
  87  | for (const weighted of [false, true]) test(`Studio: ${weighted ? 'weighted' : 'ordinary'} cluster size edits change executed payouts across minimum-five and isolated-cell boundaries`, async ({ page, request }) => {
  88  |   const graph = clusterStudioProof(weighted); await open(page, graph); await page.getByRole('tab', { name: 'Paytables', exact: true }).click();
  89  |   const size = weighted ? 3 : 4; await expect(page.getByLabel('New group size')).toBeEnabled(); await page.getByLabel('New group size').fill(String(size)); await page.getByRole('button', { name: 'Add size', exact: true }).click();
  90  |   await page.getByLabel(`cluster payout 1 count ${size}`, { exact: true }).fill('6'); await apply(page); const grouped = (await backup(page)).graph;
  91  |   await exact(request, graph, 0); await exact(request, grouped, 6); expect((grouped.initialState as Record<string, unknown>).clusterMinSize).toBe(size); expect(grouped.nodes).toEqual(graph.nodes); expect(grouped.opaque).toEqual(graph.opaque);
  92  |   await page.getByLabel('New group size').fill('1'); await page.getByRole('button', { name: 'Add size', exact: true }).click(); await page.getByLabel('cluster payout 1 count 1', { exact: true }).fill('3'); await apply(page);
  93  |   const singles = (await backup(page)).graph; await exact(request, singles, 9); expect((singles.initialState as Record<string, unknown>).clusterMinSize).toBe(1);
  94  |   await page.getByLabel('Remove paying size').selectOption('1'); await page.getByRole('button', { name: 'Remove size', exact: true }).click(); await apply(page); await exact(request, (await backup(page)).graph, 6);
  95  |   await page.getByLabel('Remove paying size').selectOption(String(size)); await page.getByRole('button', { name: 'Remove size', exact: true }).click(); await apply(page); const restored = (await backup(page)).graph; await exact(request, restored, 0); expect((restored.initialState as Record<string, unknown>).clusterMinSize).toBe(5);
  96  |   await page.getByRole('button', { name: 'Undo', exact: true }).click(); expect((await backup(page)).graph).toEqual(grouped);
  97  | });
  98  | test('Studio: prize values and independent weights survive immediate reload and change the real expected payout', async ({ page, request }) => {
  99  |   const graph = prizeStudioProof(); await open(page, graph); await page.getByRole('tab', { name: 'Paytables', exact: true }).click();
  100 |   await expect(page.getByLabel('Prize 1 value')).toBeEnabled(); await page.getByLabel('Prize 1 value').fill('3'); await page.reload(); await expect(page.getByLabel('Prize 1 value')).toHaveValue('3');
  101 |   await apply(page); const values = (await backup(page)).graph; await exact(request, graph, 4); await exact(request, values, 7);
  102 |   await page.getByLabel('Prize 1 weight').fill('1'); await apply(page); const weighted = (await backup(page)).graph; await exact(request, weighted, 8);
  103 |   await page.getByLabel('Prize 1 value').fill('5'); await expect(page.getByRole('button', { name: 'Validate & apply', exact: true })).toBeDisabled(); await expect(page.getByRole('alert')).toContainText('same identity'); expect((await backup(page)).graph).toEqual(weighted);
  104 | });
  105 | test('Studio: the selected X7 prize table and its pending values recover immediately instead of returning to line payouts', async ({ page }) => {
  106 |   const graph = createX7ClubGraph(); await open(page, graph); await page.getByRole('tab', { name: 'Paytables', exact: true }).click(); await page.locator('.studio-source-explorer').getByRole('button', { name: /Coin prizes/ }).click();
> 107 |   await expect(page.getByLabel('Prize 1 value')).toBeEnabled(); await page.getByLabel('Prize 1 value').fill('10001'); await page.reload(); await expect(page.getByRole('heading', { name: 'Coin prizes', exact: true })).toBeVisible(); await expect(page.getByLabel('Prize 1 value')).toHaveValue('10001');
      |                                                                                                                                                                                                                          ^ Error: expect(locator).toBeVisible() failed
  108 |   await apply(page); const changed = (await backup(page)).graph; expect(changed.initialState).toEqual(graph.initialState); expect(changed.nodes).toEqual(graph.nodes);
  109 |   const old = graph.mechanics as Record<string, { nodes: Record<string, unknown>[] }>, current = changed.mechanics as typeof old;
  110 |   for (const [scope, definition] of Object.entries(old)) for (const node of definition.nodes.filter(node => node.nodeType === 'draw' && /^prize\d+$/.test(String(node.stateWriteKey)))) {
  111 |     const edited = current[scope].nodes.find(value => value.id === node.id)!; expect((edited.drawWeights as Record<string, unknown>[])[0]).toEqual({ ...(node.drawWeights as Record<string, unknown>[])[0], outcomeId: '10001' });
  112 |   }
  113 | });
  114 | 
```