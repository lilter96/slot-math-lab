import { test, expect } from './fixtures';
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
  await weight.fill('2'); await page.getByRole('button', { name: 'Validate & apply weights' }).click();
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
  await page.getByLabel('Awarded free spins').fill('1'); await page.getByRole('button', { name: 'Validate & apply free spins' }).click();
  await expect(page.getByRole('button', { name: 'Undo free-spin change' })).toBeVisible();
  await page.getByRole('button', { name: 'Undo free-spin change' }).click(); await expect(page.getByLabel('Awarded free spins')).toHaveValue('9');
  await page.getByLabel('Awarded free spins').fill('1'); await page.getByRole('button', { name: 'Validate & apply free spins' }).click();
  await expect(page.getByRole('button', { name: 'Undo free-spin change' })).toBeVisible();
  await page.getByRole('tab', { name: 'Build', exact: true }).click();
  const downloaded = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export project', exact: true }).click();
  const { readFile } = await import('node:fs/promises'); const config = JSON.parse(await readFile((await (await downloaded).path())!, 'utf8'));
  expect(config.initialState.freeSpins).toBe(1); expect(config.mechanics.bonus.expressions.lives.value).toBe('1');
  expect(config.mechanics.bonus.nodes.find((node: { id: string }) => node.id === 'free-spins').maxIterations).toBe(1);
  expect(config.mechanics['base-spin'].expressions['initial-lives'].thenExpr.value).toBe('1');
  config.initialState.entryBuy = true;
  const played = await request.post('/api/play/round', { data: { config, seed: 42, roundIndex: 0, trace: true } });
  expect(played.ok(), await played.text()).toBe(true); const round = await played.json(); expect(round.state.boards).toHaveLength(1);
  await page.getByRole('tab', { name: 'Features', exact: true }).click(); await expect(page.getByLabel('Awarded free spins')).toHaveValue('1');
});
test('Debug displays actual trace cells and replay uses the pinned model after edits', { tag: '@critical' }, async ({ page }) => {
  await page.goto('/debug?project=nine-lives');
  await expect(page.getByText('No round executed in this session.', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Run current model', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Executed round', exact: true })).toBeVisible();
  await expect(page.locator('.trace-board > div')).toHaveCount(30);
  await page.getByRole('button', { name: 'Replay pinned round' }).click();
  await expect(page.getByRole('status')).toContainText('Replay matches');
  const hits = page.locator('.trace-hit');
  if (await hits.count()) { await hits.first().click(); expect(await page.locator('.trace-cell.hit').count()).toBeGreaterThan(0); await page.getByRole('button', { name: 'Show after refill' }).click(); await expect(page.locator('.trace-cell.hit')).toHaveCount(0); }
  await page.getByRole('tab', { name: 'Features', exact: true }).click();
  await page.getByLabel('Awarded free spins').fill('1'); await page.getByRole('button', { name: 'Validate & apply free spins' }).click();
  await expect(page.getByRole('button', { name: 'Undo free-spin change' })).toBeVisible();
  await page.getByRole('tab', { name: 'Debug', exact: true }).click();
  await expect(page.getByText('The current draft differs from this receipt.', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Replay pinned round' }).click(); await expect(page.getByRole('status')).toContainText('Replay matches');
  for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) { await page.setViewportSize({ width, height }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true); }
});
