import { test, expect, type APIRequestContext } from './fixtures';
test.setTimeout(180000);
async function postWithQuota(request: APIRequestContext, path: string, data: unknown) {
  for (let attempt = 0; ; attempt++) { const response = await request.post(path, { data }); if (response.status() !== 429 || attempt >= 2) return response; const delay = Math.max(1000, Number(response.headers()['retry-after'] ?? 1) * 1000); if (!Number.isFinite(delay) || delay > 60000) return response; await new Promise(resolve => setTimeout(resolve, delay)); }
}

test('Overview derives Nine Lives geometry and opens source logic at every supported viewport', { tag: '@critical' }, async ({ page }) => {
  for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
    await page.setViewportSize({ width, height });
    await page.goto('/overview?project=nine-lives');
    await expect(page.getByRole('heading', { name: 'Nine Lives', exact: true })).toBeVisible();
    await expect(page.getByRole('img', { name: /6 reels by 5 rows/ })).toBeVisible();
    await expect(page.locator('.structure-board > span')).toHaveCount(30);
    await expect(page.locator('.overview-page .prov')).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  }
  await page.getByRole('button', { name: /Free spins.*Open logic/ }).click();
  await expect(page.getByTestId('model-navigation')).toContainText('free-spin');
  await page.getByRole('button', { name: 'Open full game graph' }).click();
  await expect(page.locator('.react-flow__node[data-id="round"]')).toBeVisible();
});
test('Graph selection opens a contextual inspector and closing preserves the selected model', { tag: '@critical' }, async ({ page }) => {
  await page.goto('/build?project=nine-lives');
  await expect(page.getByRole('complementary', { name: 'Context inspector' })).toHaveCount(0);
  await page.getByLabel('Focus graph node').selectOption('round');
  await expect(page.getByRole('complementary', { name: 'Context inspector' })).toBeVisible();
  await expect.poll(async () => { const node = await page.locator('.react-flow__node[data-id="round"]').boundingBox(); const canvas = await page.locator('.react-flow').boundingBox(); return !!node && !!canvas && node.x >= canvas.x && node.x + node.width <= canvas.x + canvas.width; }).toBe(true);
  await page.getByRole('button', { name: 'Close inspector' }).click();
  await expect(page.getByRole('complementary', { name: 'Context inspector' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Show inspector' }).click();
  await expect(page.getByLabel('Subgraph name')).toHaveValue('round-entry');
  await page.keyboard.press('Escape');
  await expect(page.getByRole('complementary', { name: 'Context inspector' })).toHaveCount(0);
  const canvas = await page.locator('.canvas-wrap').boundingBox();
  expect(canvas!.width).toBeGreaterThan(800);
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByText('Graph movement and connections are read only on this screen.', { exact: false })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});
test('Weighted edits validate, undo, survive mode switches and export/import with independently checked expectation', { tag: '@critical' }, async ({ page, request }) => {
  const original = { schemaVersion: '1.0.0', name: 'Table round trip', reviewerMetadata: { retained: true }, nodes: [
    { nodeType: 'draw', id: 'draw', drawWeights: [{ outcomeId: 'three', weight: 1, value: 3 }, { outcomeId: 'one', weight: 3, value: 1 }, { outcomeId: 'zero', weight: 4, value: 0 }], outputs: { out: { name: 'out', type: 'Wins' } } },
    { nodeType: 'metricsSink', id: 'sink', winCap: 10000, inputs: { in: { name: 'in', type: 'Wins' } } },
  ], edges: [{ id: 'edge', sourceNodeId: 'draw', sourcePort: 'out', targetNodeId: 'sink', targetPort: 'in' }] };
  await page.goto('/model/data?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(original))).toString('base64')));
  const weight = page.getByLabel('Weight for three', { exact: true });
  await expect(weight).toHaveValue('1');
  await weight.fill('-1'); await expect(page.getByRole('button', { name: 'Validate & apply weights' })).toBeDisabled();
  await page.getByLabel('Weights CSV file').setInputFiles({ name: 'weights.csv', mimeType: 'text/csv', buffer: Buffer.from('outcomeId,weight\nzero,4\none,3\nthree,2') });
  await expect(page.getByText('CSV preview staged.', { exact: false })).toBeVisible();
  await expect(weight).toHaveValue('2');
  await page.getByRole('button', { name: 'Validate & apply weights' }).click();
  await expect(page.getByRole('button', { name: 'Undo weight change' })).toBeVisible();
  await page.getByRole('button', { name: 'Undo weight change' }).click(); await expect(weight).toHaveValue('1');
  await weight.fill('2'); await page.getByRole('button', { name: 'Validate & apply weights' }).click();
  await expect(page.getByRole('button', { name: 'Undo weight change' })).toBeVisible();
  await page.getByRole('tab', { name: 'Overview', exact: true }).click(); await page.getByRole('tab', { name: 'Model data', exact: true }).click();
  await expect(weight).toHaveValue('2');
  await weight.fill('3'); await page.getByRole('tab', { name: 'Build', exact: true }).click(); await page.getByRole('tab', { name: 'Model data', exact: true }).click();
  await expect(weight).toHaveValue('3'); await page.getByRole('button', { name: 'Reset weights' }).click(); await expect(weight).toHaveValue('2');
  await page.getByRole('tab', { name: 'Build', exact: true }).click();
  const downloaded = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export project', exact: true }).click();
  const download = await downloaded;
  const { readFile } = await import('node:fs/promises'); const config = JSON.parse(await readFile((await download.path())!, 'utf8'));
  expect(config.reviewerMetadata).toEqual(original.reviewerMetadata); expect(config.edges).toEqual(original.edges);
  expect(config.nodes[0].drawWeights).toEqual([{ outcomeId: 'three', weight: 2, value: 3 }, ...original.nodes[0].drawWeights!.slice(1)]);
  const actual = await request.post('/api/evaluate/light', { data: { config, maxBranches: 10000, sampleSize: 5000 } });
  expect(actual.ok()).toBe(true); const result = await actual.json(); expect(result.strategy).toBe('Exact'); expect(result.rtp).toBe(1); // (2×3 + 3×1 + 4×0) / 9
  await page.getByLabel('Import project file').setInputFiles({ name: 'round-trip.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(config)) });
  await expect.poll(async () => JSON.parse(await page.evaluate(() => localStorage.getItem('slotmath-draft-v1') ?? '{}')).nodes?.[0]?.data?.drawWeights?.[0]?.weight).toBe(2);
  await page.reload(); await page.getByRole('tab', { name: 'Model data', exact: true }).click(); await expect(weight).toHaveValue('2');
});
test('Free-spin form keeps source counts synchronized and one purchased graph life executes', { tag: '@critical' }, async ({ page, request }) => {
  await page.goto('/model/features?project=nine-lives');
  await expect(page.getByLabel('Awarded free spins')).toHaveValue('9');
  await page.getByLabel('Awarded free spins').fill('2'); await page.getByRole('tab', { name: 'Build', exact: true }).click(); await page.getByRole('tab', { name: 'Features', exact: true }).click(); await expect(page.getByLabel('Awarded free spins')).toHaveValue('2'); await page.getByRole('button', { name: 'Reset free spins' }).click();
  await page.getByLabel('Awarded free spins').fill('1'); await page.getByRole('button', { name: 'Validate & apply free spins' }).click();
  await expect(page.getByRole('button', { name: 'Undo free-spin change' })).toBeVisible({ timeout: 70000 });
  await page.getByRole('button', { name: 'Undo free-spin change' }).click(); await expect(page.getByLabel('Awarded free spins')).toHaveValue('9');
  await page.getByLabel('Awarded free spins').fill('1'); await page.getByRole('button', { name: 'Validate & apply free spins' }).click();
  await expect(page.getByRole('button', { name: 'Undo free-spin change' })).toBeVisible({ timeout: 70000 });
  await page.getByRole('tab', { name: 'Build', exact: true }).click();
  const downloaded = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export project', exact: true }).click();
  const { readFile } = await import('node:fs/promises'); const config = JSON.parse(await readFile((await (await downloaded).path())!, 'utf8'));
  expect(config.initialState.freeSpins).toBe(1); expect(config.mechanics.bonus.expressions.lives.value).toBe('1');
  expect(config.mechanics.bonus.nodes.find((node: { id: string }) => node.id === 'free-spins').maxIterations).toBe(1);
  expect(config.mechanics['base-spin'].expressions['initial-lives'].thenExpr.value).toBe('1');
  config.initialState.entryBuy = true;
  const played = await postWithQuota(request, '/api/play/round', { config, seed: 42, roundIndex: 0, trace: true });
  expect(played.ok(), await played.text()).toBe(true); const round = await played.json(); expect(round.state.boards).toHaveLength(1);
  await page.getByRole('tab', { name: 'Reference demo', exact: true }).click();
  await expect(page.getByRole('button', { name: 'SPIN', exact: true })).toBeEnabled({ timeout: 30000 });
  await page.getByRole('button', { name: 'TURBO OFF', exact: true }).click();
  await page.getByRole('button', { name: /BUY NINE LIVES/ }).click(); await page.getByRole('button', { name: 'DEAL.', exact: true }).click();
  await expect(page.locator('.nl-lives small')).toContainText('1 free spins remaining', { timeout: 70000 });
  await page.getByRole('tab', { name: 'Features', exact: true }).click(); await expect(page.getByLabel('Awarded free spins')).toHaveValue('1');
});
test('Debug displays actual trace cells and replay uses the pinned model after edits', { tag: '@critical' }, async ({ page }) => {
  await page.goto('/debug?project=nine-lives');
  await expect(page.getByText('No round executed in this session.', { exact: false })).toBeVisible({ timeout: 70000 });
  await page.getByLabel('Debug seed').fill('1');
  await page.getByRole('button', { name: 'Run current model', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Executed round', exact: true })).toBeVisible({ timeout: 70000 });
  await expect(page.locator('.trace-board > div')).toHaveCount(30);
  await page.getByRole('button', { name: 'Replay pinned round' }).click();
  await expect(page.getByRole('status')).toContainText('Replay matches', { timeout: 70000 });
  const hits = page.locator('.trace-hit');
  expect(await hits.count()).toBeGreaterThan(0);
  { await hits.first().click(); expect(await page.locator('.trace-cell.hit').count()).toBeGreaterThan(0); await page.getByRole('button', { name: 'Show after refill' }).click(); await expect(page.locator('.trace-cell.hit')).toHaveCount(0); }
  await page.getByRole('tab', { name: 'Features', exact: true }).click();
  await page.getByLabel('Awarded free spins').fill('1'); await page.getByRole('button', { name: 'Validate & apply free spins' }).click();
  await expect(page.getByRole('button', { name: 'Undo free-spin change' })).toBeVisible({ timeout: 70000 });
  await page.getByRole('tab', { name: 'Debug', exact: true }).click();
  await expect(page.getByText('The current draft differs from this receipt.', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Replay pinned round' }).click(); await expect(page.getByRole('status')).toContainText('Replay matches', { timeout: 70000 });
  for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) { await page.setViewportSize({ width, height }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true); }
});
test('Cluster payout editor changes executable coins and matching metadata with undo and actual trace evidence', { tag: '@critical' }, async ({ page, request }) => {
  await page.goto('/model/data?project=nine-lives');
  await page.getByRole('tab', { name: 'Cluster payouts', exact: true }).click();
  const input = page.getByLabel('Coins for 5 cells', { exact: true });
  await expect(input).toHaveValue('1'); await input.fill('1.5');
  await expect(page.getByRole('button', { name: 'Validate & apply payouts' })).toBeDisabled();
  await input.fill('2'); await page.getByRole('button', { name: 'Validate & apply payouts' }).click();
  await expect(page.getByRole('button', { name: 'Undo payout change' })).toBeVisible({ timeout: 70000 });
  await page.getByRole('button', { name: 'Undo payout change' }).click(); await expect(input).toHaveValue('1');
  await input.fill('2'); await page.getByRole('button', { name: 'Validate & apply payouts' }).click();
  await expect(page.getByRole('button', { name: 'Undo payout change' })).toBeVisible({ timeout: 70000 });
  await page.getByRole('tab', { name: 'Build', exact: true }).click();
  const downloaded = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export project', exact: true }).click();
  const { readFile } = await import('node:fs/promises'); const config = JSON.parse(await readFile((await (await downloaded).path())!, 'utf8'));
  expect(config.initialState.clusterPaytable[0]).toBe('2'); expect(config.paytables[0].entries[0].payouts[0]).toBe('0.1');
  const played = await postWithQuota(request, '/api/play/round', { config, seed: 1, roundIndex: 0, trace: true });
  expect(played.ok(), await played.text()).toBe(true); const round = await played.json();
  expect(round.state.stepHitSymbols[0][0]).toBe('1'); expect(round.state.stepHitSizes[0][0]).toBe('5'); expect(round.state.stepHitPayouts[0][0]).toBe('2');
  const original = structuredClone(config); original.initialState.clusterPaytable[0] = '1'; original.paytables[0].entries[0].payouts[0] = '0.05';
  const baseline = await postWithQuota(request, '/api/play/round', { config: original, seed: 1, roundIndex: 0, trace: true });
  expect(baseline.ok()).toBe(true); const previous = await baseline.json(); expect(previous.state.stepHitPayouts[0][0]).toBe('1'); expect(round.state.boards[0]).toEqual(previous.state.boards[0]);
  await page.getByRole('tab', { name: 'Model data', exact: true }).click(); await page.getByRole('tab', { name: 'Cluster payouts', exact: true }).click(); await expect(input).toHaveValue('2');
});
test('Export from an open mechanic saves the entire canonical model and shares it over an existing draft', { tag: '@critical' }, async ({ page, request }) => {
  await page.goto('/overview?project=nine-lives'); await page.getByRole('button', { name: /Free spins.*Open logic/ }).click();
  await page.getByRole('tab', { name: 'Export', exact: true }).click();
  const downloaded = page.waitForEvent('download'); await page.getByRole('button', { name: 'Download JSON', exact: true }).click();
  const { readFile } = await import('node:fs/promises'); const config = JSON.parse(await readFile((await (await downloaded).path())!, 'utf8'));
  expect(config.id).toBe('nine-lives-base'); expect(config.nodes.some((node: { id: string }) => node.id === 'round')).toBe(true); expect(Object.keys(config.mechanics)).toHaveLength(40);
  const saved = page.waitForResponse(response => new URL(response.url()).pathname === '/api/configs' && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Save to server', exact: true }).click(); const response = await saved; expect(response.ok()).toBe(true);
  const { id } = await response.json(); const stored = await request.get('/api/configs/' + id); expect(stored.ok()).toBe(true); const body = await stored.json();
  expect(Object.keys(body.config.mechanics)).toHaveLength(40); expect(body.config.mechanics['free-spin'].expressions).toBeTruthy(); expect(body.config.initialState.clusterPaytable).toEqual(config.initialState.clusterPaytable);
  const shared = { schemaVersion: '1.0.0', name: 'Shared over draft', nodes: [{ id: 'sink', nodeType: 'metricsSink', winCap: 20 }], edges: [] };
  await page.goto('/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(shared))).toString('base64')));
  await expect(page.locator('.project-pill')).toContainText('Shared over draft'); await expect(page.locator('.react-flow__node')).toHaveCount(1);
});
test('Compiler validation reports errors and focuses actual nodes; keyboard navigation and appearance are accessible', { tag: '@critical' }, async ({ page }) => {
  const invalid = { schemaVersion: '1.0.0', name: 'Invalid reference', nodes: [{ id: 'draw', nodeType: 'library', mechanicName: 'not-defined' }, { id: 'sink', nodeType: 'metricsSink', winCap: 20 }], edges: [] };
  await page.goto('/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(invalid))).toString('base64')));
  await page.getByRole('button', { name: 'Validate model', exact: true }).click();
  await expect(page.locator('.graph-validation')).toContainText('compiler errors');
  await page.getByRole('button', { name: 'Focus draw', exact: true }).first().click(); await expect(page.getByRole('complementary', { name: 'Context inspector' })).toBeVisible();
  await page.getByRole('button', { name: 'Close inspector' }).click();
  await page.getByRole('tab', { name: 'Overview', exact: true }).focus(); await page.keyboard.press('ArrowDown'); await expect(page).toHaveURL(/\/model\/data$/);
  await page.getByRole('button', { name: 'Appearance settings' }).click(); await expect(page.getByRole('button', { name: 'Appearance settings' })).toHaveAttribute('aria-expanded', 'true');
  await page.getByRole('button', { name: 'Appearance settings' }).click();
});
test('Legacy frontend share links retain edges and named definitions in canonical export', { tag: '@critical' }, async ({ page, request }) => {
  const nodes = [{ id: 'draw', type: 'draw', position: { x: 100, y: 100 }, data: { nodeType: 'draw', label: 'Legacy draw', drawWeights: [{ outcomeId: 'one', weight: 3, value: 2 }, { outcomeId: 'zero', weight: 1, value: 0 }] } }, { id: 'sink', type: 'sink', position: { x: 400, y: 100 }, data: { nodeType: 'sink', label: 'Legacy sink', winCap: 10 } }];
  const edges = [{ id: 'edge', source: 'draw', target: 'sink', sourceHandle: 'out', targetHandle: 'in' }];
  const legacy = { name: 'Legacy share', reviewerMetadata: { retained: true }, nodes, edges, mechanics: [{ id: 'legacy-definition', name: 'Legacy definition', nodes, edges, createdAt: '2026-01-01T00:00:00Z' }] };
  await page.goto('/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(legacy))).toString('base64')));
  await expect(page.locator('.react-flow__node')).toHaveCount(2); await page.getByRole('tab', { name: 'Export', exact: true }).click();
  const downloaded = page.waitForEvent('download'); await page.getByRole('button', { name: 'Download JSON', exact: true }).click(); const { readFile } = await import('node:fs/promises'); const config = JSON.parse(await readFile((await (await downloaded).path())!, 'utf8'));
  expect(config.reviewerMetadata).toEqual({ retained: true }); expect(config.edges).toEqual([{ id: 'edge', sourceNodeId: 'draw', sourcePort: 'out', targetNodeId: 'sink', targetPort: 'in' }]); expect(config.mechanics['legacy-definition'].nodes).toHaveLength(2); expect(config.mechanics['legacy-definition'].edges).toEqual(config.edges);
  const evaluated = await postWithQuota(request, '/api/evaluate/light', { config }); expect(evaluated.ok()).toBe(true); expect((await evaluated.json()).rtp).toBe(1.5); // (3×2 + 1×0)/4
});

test('State inspector preserves boolean, null and nested values and appends numeric rows as numbers', { tag: '@critical' }, async ({ page }) => {
  const initialState = { flags: [true, false], empty: null, mixed: ['a', null], nested: { enabled: true }, counts: [1, 2] };
  const config = { schemaVersion: '1.0.0', name: 'Typed state preservation', initialState, nodes: [{ id: 'sink', nodeType: 'metricsSink', winCap: 10 }], edges: [] };
  await page.goto('/model/data?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(config))).toString('base64')));
  await page.getByRole('button', { name: 'State & expressions', exact: true }).click();
  const dialog = page.getByRole('dialog');
  for (const key of ['flags', 'empty', 'mixed', 'nested']) {
    await page.getByLabel('Data table', { exact: true }).selectOption(key);
    await expect(dialog).toContainText('read only here to preserve their types');
    await expect(dialog.getByLabel('Data row 1', { exact: true })).toHaveCount(0);
    await expect(dialog.locator('pre')).toHaveText(JSON.stringify(initialState[key as keyof typeof initialState], null, 2));
  }
  await page.getByLabel('Data table', { exact: true }).selectOption('counts');
  await dialog.getByRole('button', { name: 'Add data row', exact: true }).click();
  await expect(dialog.getByLabel('Data row 3', { exact: true })).toHaveValue('0');
  await page.keyboard.press('Escape'); await page.getByRole('tab', { name: 'Export', exact: true }).click();
  const pending = page.waitForEvent('download'); await page.getByRole('button', { name: 'Download JSON', exact: true }).click();
  const { readFile } = await import('node:fs/promises'); const exported = JSON.parse(await readFile((await (await pending).path())!, 'utf8'));
  expect(exported.initialState).toEqual({ ...initialState, counts: [1, 2, 0] });
});
