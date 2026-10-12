import { test, expect, type Page } from './fixtures';
import { readFile } from 'node:fs/promises';
const generated = { schemaVersion: '1.0.0', id: 'generated-model', name: 'Generated private model', initialState: { weights: [3, 1], retained: 10000000000 }, reviewerMetadata: { keep: true }, expressions: { unused: { exprType: 'constant', kind: 'Integer', value: '42' } }, nodes: [
  { id: 'round', nodeType: 'library', label: 'Round', mechanicName: 'coin-round', parameters: {}, outputs: { out: { name: 'out', type: 'Wins' } } },
  { id: 'sink', nodeType: 'metricsSink', label: 'Sink', winCap: 10, inputs: { in: { name: 'in', type: 'Wins' } } },
], edges: [{ id: 'edge', sourceNodeId: 'round', sourcePort: 'out', targetNodeId: 'sink', targetPort: 'in' }], mechanics: {
  'coin-round': { name: 'coin-round', nodes: [{ id: 'draw', nodeType: 'draw', drawWeights: [{ outcomeId: 'one', weight: 30000000000, value: 1 }, { outcomeId: 'zero', weight: 10000000000, value: 0 }], outputs: { out: { name: 'out', type: 'Wins' } } }, { id: 'copy', nodeType: 'modifyState', expressionId: 'retain-value', outputKey: 'copied', inputs: { in: { name: 'in', type: 'Wins' } }, outputs: { out: { name: 'out', type: 'Wins' } } }], edges: [{ id: 'inside', sourceNodeId: 'draw', sourcePort: 'out', targetNodeId: 'copy', targetPort: 'in' }], expressions: { 'retain-value': { exprType: 'fieldAccess', target: 'state', path: ['retained'] } } },
} };
async function start(page: Page) {
  await page.route('**/api/features', route => route.fulfill({ json: { ai: true, autoTune: true, play: true, plugins: false } }));
  await page.goto('/build'); await page.getByRole('button', { name: 'Load coin example' }).click();
}
async function generate(page: Page) {
  await page.getByRole('button', { name: 'AI assist', exact: true }).click();
  await page.getByLabel('Game description', { exact: true }).fill('Create a reusable weighted round.');
  await page.getByRole('button', { name: 'Generate', exact: true }).click();
}
async function exported(page: Page) {
  await page.getByRole('tab', { name: 'Export', exact: true }).click();
  const pending = page.waitForEvent('download'); await page.getByRole('button', { name: 'Download JSON', exact: true }).click();
  return JSON.parse(await readFile((await (await pending).path())!, 'utf8'));
}
test('Generated graph becomes an independent private project with nested source, AST and metadata intact', async ({ page, request }) => {
  await start(page); await page.route('**/api/ai/generate-graph', route => route.fulfill({ json: generated }));
  await generate(page); await expect(page.getByRole('dialog', { name: 'AI Generate Graph' })).toHaveCount(0);
  const model = await exported(page); expect(model).toMatchObject(generated);
  const result = await request.post('/api/evaluate/light', { data: { config: model, maxBranches: 10000, sampleSize: 5000 } });
  expect(result.ok()).toBe(true); expect(await result.json()).toMatchObject({ strategy: 'Exact', rtp: 0.75 });
  await page.getByRole('tab', { name: 'My projects', exact: true }).click();
  await expect(page.locator('[aria-label="Personal projects"] .project-row')).toHaveCount(2);
  await page.locator('.project-row').filter({ hasText: 'REF-A Coin' }).getByRole('button', { name: 'Open project', exact: true }).click();
  expect((await exported(page)).name).toBe('REF-A Coin');
});

test('Unsafe or malformed generated response leaves the current model unchanged and reports the error', async ({ page }) => {
  await start(page);
  let body = JSON.stringify(generated).replace('10000000000', '9007199254740993');
  await page.route('**/api/ai/generate-graph', route => route.fulfill({ contentType: 'application/json', body }));
  await generate(page); await expect(page.getByRole('dialog').getByRole('alert')).toContainText('exact browser range');
  body = '{"schemaVersion":"1.0.0","nodes":[],"edges":[]}';
  await page.getByRole('button', { name: 'Generate', exact: true }).click(); await expect(page.getByRole('dialog').getByRole('alert')).toContainText('needs schemaVersion');
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  expect((await exported(page)).name).toBe('REF-A Coin');
});

test('Closing generation discards an obsolete reply without creating a new project', async ({ page }) => {
  await start(page); let release!: () => void, requested = false;
  const gate = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/api/ai/generate-graph', async route => { requested = true; await gate; await route.fulfill({ json: generated }).catch(() => {}); });
  try {
    await generate(page); await expect.poll(() => requested).toBe(true);
    await page.getByRole('button', { name: 'Cancel', exact: true }).click(); release();
    expect((await exported(page)).name).toBe('REF-A Coin');
    await page.getByRole('tab', { name: 'My projects', exact: true }).click();
    await expect(page.locator('[aria-label="Personal projects"] .project-row')).toHaveCount(1);
  } finally { release(); }
});

test('Auto-tune sends the canonical model and reports a candidate without fabricated sampling precision', async ({ page }) => {
  await start(page); const original = await exported(page); await page.getByRole('tab', { name: 'Build', exact: true }).click();
  let submitted: unknown;
  await page.route('**/api/ai/auto-tune', async route => { submitted = route.request().postDataJSON().config; await route.fulfill({ json: { bestConfig: generated, achievedRtp: 0.75, iterations: 20, elapsedMs: 2 } }); });
  await page.getByRole('button', { name: 'Open AI tuning panel', exact: true }).click();
  await page.getByRole('button', { name: 'Tune', exact: true }).click();
  await expect(page.getByText('Reported candidate RTP', { exact: true })).toBeVisible();
  expect(submitted).toEqual(original);
  const panel = page.getByRole('complementary', { name: 'Context inspector' }); await expect(panel.locator('.prov')).toHaveCount(0);
  await expect(panel).toContainText('Sampling details are not included');
  await page.getByRole('button', { name: 'Close inspector', exact: true }).click(); expect(await exported(page)).toEqual(original);
});

test('Tuned candidate opens independently and its report retains the requested target', async ({ page }) => {
  await start(page);
  await page.route('**/api/ai/auto-tune', route => route.fulfill({ json: { bestConfig: generated, achievedRtp: 0.75, iterations: 20, elapsedMs: 2 } }));
  await page.getByRole('button', { name: 'Open AI tuning panel', exact: true }).click();
  await page.getByRole('button', { name: 'Tune', exact: true }).click();
  await expect(page.getByText('Reported candidate RTP', { exact: true })).toBeVisible();
  await page.getByLabel('Target RTP', { exact: true }).fill('0.5');
  const report = page.getByText('Reported candidate RTP', { exact: true }).locator('..').locator('..');
  await expect(report).toContainText('96.00%');
  await expect(report).toContainText('21.000pp');
  await page.getByRole('button', { name: 'Open tuned candidate as a new project', exact: true }).click();
  expect(await exported(page)).toMatchObject(generated);
  await page.getByRole('tab', { name: 'My projects', exact: true }).click();
  await expect(page.locator('[aria-label="Personal projects"] .project-row')).toHaveCount(2);
  await page.locator('.project-row').filter({ hasText: 'REF-A Coin' }).getByRole('button', { name: 'Open project', exact: true }).click();
  expect((await exported(page)).name).toBe('REF-A Coin');
});

test('Generation dialog traps keyboard focus, fits four sizes and restores the trigger on Escape', async ({ page }) => {
  await start(page); const trigger = page.getByRole('button', { name: 'AI assist', exact: true }); await trigger.click();
  const dialog = page.getByRole('dialog', { name: 'AI Generate Graph' });
  await expect(page.getByLabel('Game description')).toBeFocused();
  for (let i = 0; i < 8; i++) {
    await page.keyboard.press('Tab');
    expect(await dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
  }
  for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
    await page.setViewportSize({ width, height });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    expect((await dialog.getByRole('button', { name: 'Close', exact: true }).boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.screenshot({ path: `../docs/design/qa/ai-dialog-${test.info().project.name}-${width}.png` });
  }
  await page.keyboard.press('Escape'); await expect(dialog).toHaveCount(0); await expect(trigger).toBeFocused();
});

test('Closing generation during compiler validation cannot apply the validated late graph', async ({ page }) => {
  await start(page); await page.route('**/api/ai/generate-graph', route => route.fulfill({ json: generated }));
  let requested = false, release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/api/validate', async route => { requested = true; await gate; await route.fulfill({ json: { isValid: true, errors: [] } }).catch(() => {}); });
  try {
    await generate(page); await expect.poll(() => requested).toBe(true);
    await page.getByRole('button', { name: 'Cancel', exact: true }).click(); release();
    expect((await exported(page)).name).toBe('REF-A Coin');
  } finally { release(); }
});

test('Unsafe tuning reply is rejected and closing the tuner discards a later candidate', async ({ page }) => {
  await start(page); let mode = 'unsafe', requested = false, release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/api/ai/auto-tune', async route => {
    requested = true;
    if (mode === 'unsafe') await route.fulfill({ contentType: 'application/json', body: JSON.stringify({ bestConfig: generated, achievedRtp: 0.75, iterations: 20, elapsedMs: 2 }).replace('10000000000', '9007199254740993') });
    else { await gate; await route.fulfill({ json: { bestConfig: generated, achievedRtp: 0.75, iterations: 20, elapsedMs: 2 } }).catch(() => {}); }
  });
  try {
    await page.getByRole('button', { name: 'Open AI tuning panel', exact: true }).click(); await page.getByRole('button', { name: 'Tune', exact: true }).click();
    await expect(page.getByRole('alert')).toContainText('exact browser range');
    mode = 'late'; requested = false; await page.getByRole('button', { name: 'Tune', exact: true }).click(); await expect.poll(() => requested).toBe(true);
    await page.getByRole('button', { name: 'Close inspector', exact: true }).click(); release();
    expect((await exported(page)).name).toBe('REF-A Coin');
  } finally { release(); }
});

test('Generation parse errors remain visible when the compiler-error list is empty', async ({ page }) => {
  await start(page); await page.route('**/api/ai/generate-graph', route => route.fulfill({ status: 422, json: { error: 'The provider returned malformed model JSON.', compilerErrors: [] } }));
  await generate(page); await expect(page.getByRole('dialog').getByRole('alert')).toContainText('malformed model JSON');
  await page.getByRole('button', { name: 'Cancel', exact: true }).click(); expect((await exported(page)).name).toBe('REF-A Coin');
});
