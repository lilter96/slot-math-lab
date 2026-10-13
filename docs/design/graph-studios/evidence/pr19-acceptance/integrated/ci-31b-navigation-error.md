# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: workbench.spec.ts >> Compiler validation reports errors and focuses actual nodes; keyboard navigation and appearance are accessible
- Location: e2e/workbench.spec.ts:155:1

# Error details

```
Error: expect(page).toHaveURL(expected) failed

Expected pattern: /\/model\/data$/
Received string:  "http://localhost:4173/build"
Timeout: 5000ms

Call log:
  - Expect "toHaveURL" with timeout 5000ms
    14 × unexpected value "http://localhost:4173/build"

```

```yaml
- link "Skip to main content":
  - /url: "#main-content"
- text: Slot Math Lab
- link "Choose project":
  - /url: /projects
  - text: Invalid reference
- link "Run analysis":
  - /url: /simulate
- button "AI assist"
- link "Games":
  - /url: /games.html
  - img
  - text: Games
- button "Appearance settings": ⚙
- complementary "Workspace":
  - button "Collapse navigation" [expanded]
  - text: Invalid reference
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
    - tab "Studio":
      - img
      - text: Studio
    - tab "State machine":
      - img
      - text: State machine
    - tab "Features":
      - img
      - text: Features
    - tab "Build" [selected]:
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
  - complementary "Mechanic explorer":
    - text: Invalid reference Mechanics
    - button "Open full game graph": Full game
    - button "Open model data tables": Data tables
    - text: Find mechanic
    - searchbox "Find subgraph"
    - list:
      - listitem: No named mechanics. Edit the full graph or extract selected nodes.
      - listitem:
        - group: All definitions (0)
    - group: Ready subgraphs
    - group: Extract selected atoms
  - navigation "Graph breadcrumbs": Logic graph
  - button "Add nodes"
  - button "Open mechanic manager": Mechanics
  - button "Open AI tuning panel": AI
  - button "Show inspector"
  - button "Validate model"
  - group:
    - text: 1 compiler errors
    - list:
      - listitem:
        - text: "INVALID_GRAPH: Library node 'draw' references unknown mechanic 'not-defined'."
        - button "Focus draw"
  - application:
    - group:
      - img
      - text: draw L0 mechanic not-defined
    - group:
      - img
      - text: sink L0 RTP —
    - img
    - link "React Flow attribution":
      - /url: https://reactflow.dev
      - text: React Flow
  - button "Choose a project or template": Projects
  - button "Edit project data and expressions": Data / AST
  - button "Connect graph ports": Connect ports
  - combobox "Focus graph node":
    - option "Find node…" [selected]
    - option "draw"
    - option "sink"
  - combobox "Load catalog example":
    - option "Catalog examples" [selected]
    - option "cascade"
    - option "cluster"
    - option "hold-and-win"
    - option "lines"
    - option "scatter"
    - option "sticky-wild"
    - option "ways"
  - button "Load coin example": REF-A
  - button "Import project": Import
  - button "Export project": Save
  - button "AI Generate graph":
    - img
  - button "Zoom out":
    - img
  - text: 100%
  - button "Zoom in":
    - img
  - button "Fit selection":
    - img
  - button "100% zoom": 1:1
  - button "Reset view":
    - img
  - text: drag nodes · scroll to zoom · hold space to pan Return to player — Unavailable Hit frequency — Unavailable Volatility index — Unavailable Maximum win — No proven bound in light evaluation Evaluation status
  - status: Unavailable
  - text: Library node 'draw' references unknown mechanic 'not-defined'.
```

# Test source

```ts
  62  |   const downloaded = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export project', exact: true }).click();
  63  |   const download = await downloaded;
  64  |   const { readFile } = await import('node:fs/promises'); const config = JSON.parse(await readFile((await download.path())!, 'utf8'));
  65  |   expect(config.reviewerMetadata).toEqual(original.reviewerMetadata); expect(config.edges).toEqual(original.edges);
  66  |   expect(config.nodes[0].drawWeights).toEqual([{ outcomeId: 'three', weight: 2, value: 3 }, ...original.nodes[0].drawWeights!.slice(1)]);
  67  |   const actual = await request.post('/api/evaluate/light', { data: { config, maxBranches: 10000, sampleSize: 5000 } });
  68  |   expect(actual.ok()).toBe(true); const result = await actual.json(); expect(result.strategy).toBe('Exact'); expect(result.rtp).toBe(1); // (2×3 + 3×1 + 4×0) / 9
  69  |   await page.getByLabel('Import project file').setInputFiles({ name: 'round-trip.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(config)) });
  70  |   await expect.poll(async () => JSON.parse(await page.evaluate(() => localStorage.getItem('slotmath-draft-v1') ?? '{}')).nodes?.[0]?.data?.drawWeights?.[0]?.weight).toBe(2);
  71  |   await page.reload(); await page.getByRole('tab', { name: 'Model data', exact: true }).click(); await expect(weight).toHaveValue('2');
  72  | });
  73  | test('Free-spin form keeps source counts synchronized and one purchased graph life executes', { tag: '@critical' }, async ({ page, request }) => {
  74  |   await page.goto('/model/features?project=nine-lives');
  75  |   await expect(page.getByLabel('Awarded free spins')).toHaveValue('9');
  76  |   await page.getByLabel('Awarded free spins').fill('2'); await page.getByRole('tab', { name: 'Build', exact: true }).click(); await page.getByRole('tab', { name: 'Features', exact: true }).click(); await expect(page.getByLabel('Awarded free spins')).toHaveValue('2'); await page.getByRole('button', { name: 'Reset free spins' }).click();
  77  |   await page.getByLabel('Awarded free spins').fill('1'); await page.getByRole('button', { name: 'Validate & apply free spins' }).click();
  78  |   await expect(page.getByRole('button', { name: 'Undo free-spin change' })).toBeVisible({ timeout: 70000 });
  79  |   await page.getByRole('button', { name: 'Undo free-spin change' }).click(); await expect(page.getByLabel('Awarded free spins')).toHaveValue('9');
  80  |   await page.getByLabel('Awarded free spins').fill('1'); await page.getByRole('button', { name: 'Validate & apply free spins' }).click();
  81  |   await expect(page.getByRole('button', { name: 'Undo free-spin change' })).toBeVisible({ timeout: 70000 });
  82  |   await page.getByRole('tab', { name: 'Build', exact: true }).click();
  83  |   const downloaded = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export project', exact: true }).click();
  84  |   const { readFile } = await import('node:fs/promises'); const config = JSON.parse(await readFile((await (await downloaded).path())!, 'utf8'));
  85  |   expect(config.initialState.freeSpins).toBe(1); expect(config.mechanics.bonus.expressions.lives.value).toBe('1');
  86  |   expect(config.mechanics.bonus.nodes.find((node: { id: string }) => node.id === 'free-spins').maxIterations).toBe(1);
  87  |   expect(config.mechanics['base-spin'].expressions['initial-lives'].thenExpr.value).toBe('1');
  88  |   config.initialState.entryBuy = true;
  89  |   const played = await postWithQuota(request, '/api/play/round', { config, seed: 42, roundIndex: 0, trace: true });
  90  |   expect(played.ok(), await played.text()).toBe(true); const round = await played.json(); expect(round.state.boards).toHaveLength(1);
  91  |   await page.getByRole('tab', { name: 'Reference demo', exact: true }).click();
  92  |   await expect(page.getByRole('button', { name: 'SPIN', exact: true })).toBeEnabled({ timeout: 30000 });
  93  |   await page.getByRole('button', { name: 'TURBO OFF', exact: true }).click();
  94  |   await page.getByRole('button', { name: /BUY NINE LIVES/ }).click(); await page.getByRole('button', { name: 'DEAL.', exact: true }).click();
  95  |   await expect(page.locator('.nl-lives small')).toContainText('1 free spins remaining', { timeout: 70000 });
  96  |   await page.getByRole('tab', { name: 'Features', exact: true }).click(); await expect(page.getByLabel('Awarded free spins')).toHaveValue('1');
  97  | });
  98  | test('Debug displays actual trace cells and replay uses the pinned model after edits', { tag: '@critical' }, async ({ page }) => {
  99  |   await page.goto('/debug?project=nine-lives');
  100 |   await expect(page.getByText('No round executed in this session.', { exact: false })).toBeVisible({ timeout: 70000 });
  101 |   await page.getByLabel('Debug seed').fill('1');
  102 |   await page.getByRole('button', { name: 'Run current model', exact: true }).click();
  103 |   await expect(page.getByRole('heading', { name: 'Executed round', exact: true })).toBeVisible({ timeout: 70000 });
  104 |   await expect(page.locator('.trace-board > div')).toHaveCount(30);
  105 |   await page.getByRole('button', { name: 'Replay pinned round' }).click();
  106 |   await expect(page.getByRole('status')).toContainText('Replay matches', { timeout: 70000 });
  107 |   const hits = page.locator('.trace-hit');
  108 |   expect(await hits.count()).toBeGreaterThan(0);
  109 |   { await hits.first().click(); expect(await page.locator('.trace-cell.hit').count()).toBeGreaterThan(0); await page.getByRole('button', { name: 'Show after refill' }).click(); await expect(page.locator('.trace-cell.hit')).toHaveCount(0); }
  110 |   await page.getByRole('tab', { name: 'Features', exact: true }).click();
  111 |   await page.getByLabel('Awarded free spins').fill('1'); await page.getByRole('button', { name: 'Validate & apply free spins' }).click();
  112 |   await expect(page.getByRole('button', { name: 'Undo free-spin change' })).toBeVisible({ timeout: 70000 });
  113 |   await page.getByRole('tab', { name: 'Debug', exact: true }).click();
  114 |   await expect(page.getByText('The current draft differs from this receipt.', { exact: false })).toBeVisible();
  115 |   await page.getByRole('button', { name: 'Replay pinned round' }).click(); await expect(page.getByRole('status')).toContainText('Replay matches', { timeout: 70000 });
  116 |   for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) { await page.setViewportSize({ width, height }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true); }
  117 | });
  118 | test('Cluster payout editor changes executable coins and matching metadata with undo and actual trace evidence', { tag: '@critical' }, async ({ page, request }) => {
  119 |   await page.goto('/model/data?project=nine-lives');
  120 |   await page.getByRole('tab', { name: 'Cluster payouts', exact: true }).click();
  121 |   const input = page.getByLabel('Coins for 5 cells', { exact: true });
  122 |   await expect(input).toHaveValue('1'); await input.fill('1.5');
  123 |   await expect(page.getByRole('button', { name: 'Validate & apply payouts' })).toBeDisabled();
  124 |   await input.fill('2'); await page.getByRole('button', { name: 'Validate & apply payouts' }).click();
  125 |   await expect(page.getByRole('button', { name: 'Undo payout change' })).toBeVisible({ timeout: 70000 });
  126 |   await page.getByRole('button', { name: 'Undo payout change' }).click(); await expect(input).toHaveValue('1');
  127 |   await input.fill('2'); await page.getByRole('button', { name: 'Validate & apply payouts' }).click();
  128 |   await expect(page.getByRole('button', { name: 'Undo payout change' })).toBeVisible({ timeout: 70000 });
  129 |   await page.getByRole('tab', { name: 'Build', exact: true }).click();
  130 |   const downloaded = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export project', exact: true }).click();
  131 |   const { readFile } = await import('node:fs/promises'); const config = JSON.parse(await readFile((await (await downloaded).path())!, 'utf8'));
  132 |   expect(config.initialState.clusterPaytable[0]).toBe('2'); expect(config.paytables[0].entries[0].payouts[0]).toBe('0.1');
  133 |   const played = await postWithQuota(request, '/api/play/round', { config, seed: 1, roundIndex: 0, trace: true });
  134 |   expect(played.ok(), await played.text()).toBe(true); const round = await played.json();
  135 |   expect(round.state.stepHitSymbols[0][0]).toBe('1'); expect(round.state.stepHitSizes[0][0]).toBe('5'); expect(round.state.stepHitPayouts[0][0]).toBe('2');
  136 |   const original = structuredClone(config); original.initialState.clusterPaytable[0] = '1'; original.paytables[0].entries[0].payouts[0] = '0.05';
  137 |   const baseline = await postWithQuota(request, '/api/play/round', { config: original, seed: 1, roundIndex: 0, trace: true });
  138 |   expect(baseline.ok()).toBe(true); const previous = await baseline.json(); expect(previous.state.stepHitPayouts[0][0]).toBe('1'); expect(round.state.boards[0]).toEqual(previous.state.boards[0]);
  139 |   await page.getByRole('tab', { name: 'Model data', exact: true }).click(); await page.getByRole('tab', { name: 'Cluster payouts', exact: true }).click(); await expect(input).toHaveValue('2');
  140 | });
  141 | test('Export from an open mechanic saves the entire canonical model and shares it over an existing draft', { tag: '@critical' }, async ({ page, request }) => {
  142 |   await page.goto('/overview?project=nine-lives'); await page.getByRole('button', { name: /Free spins.*Open logic/ }).click();
  143 |   await page.getByRole('tab', { name: 'Export', exact: true }).click();
  144 |   const downloaded = page.waitForEvent('download'); await page.getByRole('button', { name: 'Download JSON', exact: true }).click();
  145 |   const { readFile } = await import('node:fs/promises'); const config = JSON.parse(await readFile((await (await downloaded).path())!, 'utf8'));
  146 |   expect(config.id).toBe('nine-lives-base'); expect(config.nodes.some((node: { id: string }) => node.id === 'round')).toBe(true); expect(Object.keys(config.mechanics)).toHaveLength(40);
  147 |   const saved = page.waitForResponse(response => new URL(response.url()).pathname === '/api/configs' && response.request().method() === 'POST');
  148 |   await page.getByRole('button', { name: 'Save to server', exact: true }).click(); const response = await saved; expect(response.ok()).toBe(true);
  149 |   const { id } = await response.json(); const stored = await request.get('/api/configs/' + id); expect(stored.ok()).toBe(true); const body = await stored.json();
  150 |   expect(Object.keys(body.config.mechanics)).toHaveLength(40); expect(body.config.mechanics['free-spin'].expressions).toBeTruthy(); expect(body.config.initialState.clusterPaytable).toEqual(config.initialState.clusterPaytable);
  151 |   const shared = { schemaVersion: '1.0.0', name: 'Shared over draft', nodes: [{ id: 'sink', nodeType: 'metricsSink', winCap: 20 }], edges: [] };
  152 |   await page.goto('/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(shared))).toString('base64')));
  153 |   await expect(page.locator('.project-pill')).toContainText('Shared over draft'); await expect(page.locator('.react-flow__node')).toHaveCount(1);
  154 | });
  155 | test('Compiler validation reports errors and focuses actual nodes; keyboard navigation and appearance are accessible', { tag: '@critical' }, async ({ page }) => {
  156 |   const invalid = { schemaVersion: '1.0.0', name: 'Invalid reference', nodes: [{ id: 'draw', nodeType: 'library', mechanicName: 'not-defined' }, { id: 'sink', nodeType: 'metricsSink', winCap: 20 }], edges: [] };
  157 |   await page.goto('/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(invalid))).toString('base64')));
  158 |   await page.getByRole('button', { name: 'Validate model', exact: true }).click();
  159 |   await expect(page.locator('.graph-validation')).toContainText('compiler errors');
  160 |   await page.getByRole('button', { name: 'Focus draw', exact: true }).first().click(); await expect(page.getByRole('complementary', { name: 'Context inspector' })).toBeVisible();
  161 |   await page.getByRole('button', { name: 'Close inspector' }).click();
> 162 |   await page.getByRole('tab', { name: 'Overview', exact: true }).focus(); await page.keyboard.press('ArrowDown'); await expect(page).toHaveURL(/\/model\/data$/);
      |                                                                                                                                      ^ Error: expect(page).toHaveURL(expected) failed
  163 |   await page.getByRole('button', { name: 'Appearance settings' }).click(); await expect(page.getByRole('button', { name: 'Appearance settings' })).toHaveAttribute('aria-expanded', 'true');
  164 |   await page.getByRole('button', { name: 'Appearance settings' }).click();
  165 | });
  166 | test('Legacy frontend share links retain edges and named definitions in canonical export', { tag: '@critical' }, async ({ page, request }) => {
  167 |   const nodes = [{ id: 'draw', type: 'draw', position: { x: 100, y: 100 }, data: { nodeType: 'draw', label: 'Legacy draw', drawWeights: [{ outcomeId: 'one', weight: 3, value: 2 }, { outcomeId: 'zero', weight: 1, value: 0 }] } }, { id: 'sink', type: 'sink', position: { x: 400, y: 100 }, data: { nodeType: 'sink', label: 'Legacy sink', winCap: 10 } }];
  168 |   const edges = [{ id: 'edge', source: 'draw', target: 'sink', sourceHandle: 'out', targetHandle: 'in' }];
  169 |   const legacy = { name: 'Legacy share', reviewerMetadata: { retained: true }, nodes, edges, mechanics: [{ id: 'legacy-definition', name: 'Legacy definition', nodes, edges, createdAt: '2026-01-01T00:00:00Z' }] };
  170 |   await page.goto('/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(legacy))).toString('base64')));
  171 |   await expect(page.locator('.react-flow__node')).toHaveCount(2); await page.getByRole('tab', { name: 'Export', exact: true }).click();
  172 |   const downloaded = page.waitForEvent('download'); await page.getByRole('button', { name: 'Download JSON', exact: true }).click(); const { readFile } = await import('node:fs/promises'); const config = JSON.parse(await readFile((await (await downloaded).path())!, 'utf8'));
  173 |   expect(config.reviewerMetadata).toEqual({ retained: true }); expect(config.edges).toEqual([{ id: 'edge', sourceNodeId: 'draw', sourcePort: 'out', targetNodeId: 'sink', targetPort: 'in' }]); expect(config.mechanics['legacy-definition'].nodes).toHaveLength(2); expect(config.mechanics['legacy-definition'].edges).toEqual(config.edges);
  174 |   const evaluated = await postWithQuota(request, '/api/evaluate/light', { config }); expect(evaluated.ok()).toBe(true); expect((await evaluated.json()).rtp).toBe(1.5); // (3×2 + 1×0)/4
  175 | });
  176 | 
  177 | test('State inspector preserves boolean, null and nested values and appends numeric rows as numbers', { tag: '@critical' }, async ({ page }) => {
  178 |   const initialState = { flags: [true, false], empty: null, mixed: ['a', null], nested: { enabled: true }, counts: [1, 2] };
  179 |   const config = { schemaVersion: '1.0.0', name: 'Typed state preservation', initialState, nodes: [{ id: 'sink', nodeType: 'metricsSink', winCap: 10 }], edges: [] };
  180 |   await page.goto('/model/data?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(config))).toString('base64')));
  181 |   await page.getByRole('button', { name: 'State & expressions', exact: true }).click();
  182 |   const dialog = page.getByRole('dialog');
  183 |   for (const key of ['flags', 'empty', 'mixed', 'nested']) {
  184 |     await page.getByLabel('Data table', { exact: true }).selectOption(key);
  185 |     await expect(dialog).toContainText('read only here to preserve their types');
  186 |     await expect(dialog.getByLabel('Data row 1', { exact: true })).toHaveCount(0);
  187 |     await expect(dialog.locator('pre')).toHaveText(JSON.stringify(initialState[key as keyof typeof initialState], null, 2));
  188 |   }
  189 |   await page.getByLabel('Data table', { exact: true }).selectOption('counts');
  190 |   await dialog.getByRole('button', { name: 'Add data row', exact: true }).click();
  191 |   await expect(dialog.getByLabel('Data row 3', { exact: true })).toHaveValue('0');
  192 |   await page.keyboard.press('Escape'); await page.getByRole('tab', { name: 'Export', exact: true }).click();
  193 |   const pending = page.waitForEvent('download'); await page.getByRole('button', { name: 'Download JSON', exact: true }).click();
  194 |   const { readFile } = await import('node:fs/promises'); const exported = JSON.parse(await readFile((await (await pending).path())!, 'utf8'));
  195 |   expect(exported.initialState).toEqual({ ...initialState, counts: [1, 2, 0] });
  196 | });
  197 | 
```