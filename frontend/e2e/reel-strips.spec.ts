import { test, expect, type APIRequestContext, type Page } from './fixtures';
import { Flow, Mechanics, gameGraph } from '../src/games/shared/graph/flow';
import { reelState, spinReels, showingState, spinReelsShowing } from '../src/games/shared/graph/mechanics/reels';
import { at, num, toNumber } from '../src/games/shared/graph/expr';
import { createWoodlandWhisperGraph } from '../src/games/woodland-whisper/graph';
import { createAncientDragonGraph } from '../src/games/ancient-dragon/graph';
import { createLeMilitareGraph } from '../src/games/le-militare/graph';
import { readFile } from 'node:fs/promises';

const reels = { name: 'ordinary-reels', stripKey: 'strip', strips: [['0', '1'], ['0', '1']], rows: 2 };
const mechanics = new Mechanics();
const fixture = gameGraph({ id: 'strip-fixture', name: 'Strip arithmetic fixture', mechanics,
  root: new Flow().use('spin', 'Draw two ordinary strips', mechanics.add(spinReels(reels))).write('win', 'Top of first reel', 'win', toNumber(at('board', num(0)))).sink('sink', 'Payout', 'win', 2),
  initialState: { ...reelState(reels), win: 0 }, tables: { boardConfig: { columns: 2, rows: 2 }, symbols: [{ id: '0', name: 'Zero' }, { id: '1', name: 'One' }], reviewerMetadata: { retained: true } } });
const link = (graph: unknown) => '/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(graph))).toString('base64'));
async function openReels(page: Page) {
  await page.getByRole('tab', { name: 'Model data', exact: true }).click();
  await page.getByRole('tab', { name: 'Reel strips', exact: true }).click();
}
async function exported(page: Page) {
  await page.getByRole('tab', { name: 'Export', exact: true }).click();
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download JSON', exact: true }).click();
  return JSON.parse(await readFile((await (await download).path())!, 'utf8'));
}
async function post(request: APIRequestContext, path: string, data: unknown) {
  for (let attempt = 0; attempt < 3; attempt++) {
    const response = await request.post(path, { data });
    if (response.status() !== 429) { expect(response.ok(), path).toBe(true); return response.json(); }
    const delay = Math.max(1000, Number(response.headers()['retry-after'] ?? 1) * 1000);
    if (!Number.isFinite(delay) || delay > 65000) throw new Error('Quota deadline exceeds this test budget.');
    await new Promise(resolve => setTimeout(resolve, delay));
  }
  throw new Error('Explicit calculation quota did not recover.');
}

test('Strip transaction stages edits, preserves canonical source, updates stops/wrap and independently changes expectation with Undo', async ({ page, request }) => {
  test.setTimeout(180000);
  await page.goto(link(fixture));
  const original = await exported(page);
  expect((await post(request, '/api/evaluate/light', { config: original })).rtp).toBe(.5);
  const before = await post(request, '/api/play/round', { config: original, seed: 7, trace: true });
  await openReels(page);
  await page.getByLabel('Reel 1 symbol at 0', { exact: true }).selectOption('1');
  const add = page.getByRole('button', { name: 'Add symbol to reel 1', exact: true });
  await add.focus(); await page.keyboard.press('Enter');
  await page.getByLabel('Reel 1 symbol at 2', { exact: true }).selectOption('1');
  await expect(page.getByText(/Unapplied strip changes/)).toContainText('2 → 3 stops');
  expect(await exported(page)).toEqual(original);
  await openReels(page);
  await expect(page.getByLabel('Reel 1 symbol at 2', { exact: true })).toHaveValue('1');
  await page.getByRole('button', { name: 'Validate & apply strips', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Undo strip change', exact: true })).toBeVisible();
  const changed = await exported(page);
  expect(changed.initialState.strip0).toEqual(['1', '1', '1', '1']);
  expect(changed.initialState.strip1).toEqual(original.initialState.strip1);
  expect(changed.mechanics['ordinary-reels'].nodes[0].drawWeights).toEqual([0, 1, 2].map(index => ({ outcomeId: String(index), weight: 1, value: 0 })));
  expect(changed.reviewerMetadata).toEqual({ retained: true });
  expect((await post(request, '/api/evaluate/light', { config: changed })).rtp).toBe(1);
  expect((await post(request, '/api/play/round', { config: changed, seed: 7, trace: true })).state.board[0]).toBe('1');
  await openReels(page);
  await page.getByRole('button', { name: 'Undo strip change', exact: true }).click();
  const restored = await exported(page);
  expect(restored).toEqual(original);
  expect(await post(request, '/api/play/round', { config: restored, seed: 7, trace: true })).toEqual(before);
});

test('Strip CSV preview validates all indices/symbols and keyboard reorder/remove stays unapplied', async ({ page }) => {
  await page.goto(link(fixture)); await openReels(page);
  const file = page.getByLabel('Strip CSV file', { exact: true });
  await file.setInputFiles({ name: 'bad.csv', mimeType: 'text/csv', buffer: Buffer.from('reel,index,symbolId\n1,0,unknown\n2,0,0') });
  await expect(page.getByRole('alert')).toContainText('unknown symbol');
  await expect(page.getByLabel('Reel 1 symbol at 0', { exact: true })).toHaveValue('0');
  await file.setInputFiles({ name: 'good.csv', mimeType: 'text/csv', buffer: Buffer.from('reel,index,symbolId\n2,0,0\n1,1,1\n1,0,0') });
  await expect(page.getByText(/CSV preview staged/)).toBeVisible();
  await page.getByLabel('Reel 1 symbol at 1', { exact: true }).focus();
  const up = page.getByRole('button', { name: 'Move reel 1 index 1 up', exact: true });
  await up.focus(); await page.keyboard.press('Enter');
  await expect(page.getByLabel('Reel 1 symbol at 0', { exact: true })).toHaveValue('1');
  await page.getByLabel('Reel 2 symbol at 0', { exact: true }).focus();
  const remove = page.getByRole('button', { name: 'Remove reel 2 index 0', exact: true });
  await remove.focus(); await page.keyboard.press('Enter');
  await expect(page.getByRole('alert')).toContainText('at least one symbol');
  await expect(page.getByRole('button', { name: 'Validate & apply strips', exact: true })).toBeDisabled();
});

test('Late strip validation aborts when another project is opened', async ({ page }) => {
  await page.goto(link(fixture)); await openReels(page);
  let release!: () => void, requested = false, aborted = false, finished = false;
  const gate = new Promise<void>(resolve => { release = resolve; });
  page.on('requestfailed', request => { if (new URL(request.url()).pathname === '/api/validate') aborted = true; });
  await page.route('**/api/validate', async route => { requested = true; const response = await route.fetch(); await gate; await route.fulfill({ response }).catch(() => {}); finished = true; });
  await page.getByLabel('Reel 1 symbol at 0', { exact: true }).selectOption('1');
  await page.getByRole('button', { name: 'Validate & apply strips', exact: true }).click();
  await expect.poll(() => requested).toBe(true);
  await page.getByRole('tab', { name: 'Build', exact: true }).click();
  await expect.poll(() => aborted).toBe(true);
  await page.getByLabel('Import project file', { exact: true }).setInputFiles({ name: 'copy.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify({ ...fixture, name: 'Independent strip copy' })) });
  release(); await expect.poll(() => finished).toBe(true); await page.unroute('**/api/validate');
  await openReels(page);
  await expect(page.getByLabel('Reel 1 symbol at 0', { exact: true })).toHaveValue('0');
  await expect(page.getByRole('button', { name: 'Validate & apply strips', exact: true })).toBeDisabled();
});

test('Real game strip sets, pagination and responsive tables preserve all source data', async ({ page }) => {
  test.setTimeout(180000);
  for (const [create, count] of [[createAncientDragonGraph, 1], [createWoodlandWhisperGraph, 1], [createLeMilitareGraph, 2]] as const) {
    await page.goto('/build');
    await page.getByLabel('Import project file', { exact: true }).setInputFiles({ name: 'game.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(create())) });
    await page.getByRole('tab', { name: 'Overview', exact: true }).click();
    await page.getByRole('link', { name: 'Edit reel strips', exact: true }).click();
    await expect(page.getByLabel('Reel set', { exact: true }).locator('option')).toHaveCount(count);
    await page.getByLabel('Strip index range', { exact: true }).selectOption('1');
    await expect(page.getByLabel('Reel 1 symbol at 25', { exact: true })).toBeVisible();
    for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
      await page.setViewportSize({ width, height });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    }
    await page.setViewportSize({ width: 1440, height: 900 });
    const keys = await page.getByLabel('Reel set', { exact: true }).locator('option').evaluateAll(options => options.map(option => (option as HTMLOptionElement).value));
    for (const key of keys) {
      await page.getByLabel('Reel set', { exact: true }).selectOption(key);
      await page.getByRole('button', { name: 'Add symbol to reel 1', exact: true }).click();
      await page.getByRole('button', { name: 'Validate & apply strips', exact: true }).click();
      await expect(page.getByRole('button', { name: 'Undo strip change', exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'Undo strip change', exact: true }).click();
      await expect(page.getByRole('button', { name: 'Validate & apply strips', exact: true })).toBeDisabled();
    }
  }
});

test('Conditioned bought-feature strip edits preserve all rules, compile, Undo and remain editable after import', async ({ page }) => {
  test.setTimeout(180000);
  const graph = createWoodlandWhisperGraph(undefined, { buy: true });
  await page.goto('/build');
  await page.getByLabel('Import project file', { exact: true }).setInputFiles({ name: 'bought.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(graph)) });
  const original = await exported(page);
  await openReels(page);
  await expect(page.getByText(/at least 3 COIN symbols/)).toBeVisible();
  await page.getByRole('button', { name: 'Add symbol to reel 1', exact: true }).click();
  const index = original.initialState.reel0.length - 2;
  await page.getByLabel(`Reel 1 symbol at ${index}`, { exact: true }).selectOption('11');
  await page.getByRole('button', { name: 'Validate & apply strips', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Undo strip change', exact: true })).toBeVisible();
  const changed = await exported(page);
  expect(changed.initialState.reel0).toHaveLength(original.initialState.reel0.length + 1);
  expect(changed.mechanics['spin-reels-triggering'].expressions.__reelEditorAtLeast.value).toBe('3');
  await openReels(page);
  await page.getByRole('button', { name: 'Undo strip change', exact: true }).click();
  expect(await exported(page)).toEqual(original);
  await page.getByRole('tab', { name: 'Build', exact: true }).click();
  await page.getByLabel('Import project file', { exact: true }).setInputFiles({ name: 'changed-buy.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(changed)) });
  await openReels(page);
  await expect(page.getByText(/at least 3 COIN symbols/)).toBeVisible();
  await expect(page.getByLabel('Reel set', { exact: true })).toBeVisible();
});

test('Conditional strip edits follow independently enumerated conditioning and seeded Undo without extra RNG draws', async ({ page, request }) => {
  test.setTimeout(180000);
  const spec = { name: 'raw', stripKey: 'strip', strips: [['0', '1'], ['0', '1']], rows: 1 };
  const condition = { name: 'conditioned', symbol: '1', symbolName: 'One', atLeast: 1 }, graphMechanics = new Mechanics();
  const graph = gameGraph({ id: 'conditional-oracle', name: 'Conditional arithmetic oracle', mechanics: graphMechanics,
    root: new Flow().use('spin', 'At least one One', graphMechanics.add(spinReelsShowing(spec, condition))).write('win', 'First reel', 'win', toNumber(at('board', num(0)))).sink('sink', 'Payout', 'win', 2),
    initialState: { ...reelState(spec), ...showingState(spec, condition), win: 0 }, tables: { boardConfig: { columns: 2, rows: 1 }, symbols: [{ id: '0', name: 'Zero' }, { id: '1', name: 'One' }] } });
  await page.goto(link(graph)); const original = await exported(page);
  // Independent Cartesian enumeration: 01/10/11 qualify; two of three have first=1.
  expect((await post(request, '/api/evaluate/light', { config: original })).rtp).toBeCloseTo(2 / 3, 12);
  const before = await post(request, '/api/play/round', { config: original, seed: 7, trace: true });
  await openReels(page);
  await page.getByRole('button', { name: 'Add symbol to reel 1', exact: true }).click();
  await page.getByRole('button', { name: 'Validate & apply strips', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Undo strip change', exact: true })).toBeVisible();
  const changed = await exported(page);
  // 00/01/10/11/00/01: four qualify, two have first=1. Raw first frequency is 1/3, not 1/2.
  expect((await post(request, '/api/evaluate/light', { config: changed })).rtp).toBe(.5);
  expect(changed.mechanics.conditioned.expressions.__reelEditorSymbol.value).toBe('1');
  await openReels(page);
  await page.getByRole('button', { name: 'Undo strip change', exact: true }).click();
  const restored = await exported(page);
  expect(restored).toEqual(original);
  expect(await post(request, '/api/play/round', { config: restored, seed: 7, trace: true })).toEqual(before);
  await openReels(page);
  for (const reel of [1, 2]) await page.getByLabel(`Reel ${reel} symbol at 0`, { exact: true }).selectOption('1');
  await page.getByRole('button', { name: 'Validate & apply strips', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Undo strip change', exact: true })).toBeVisible();
  const allOnes = await exported(page);
  expect((await post(request, '/api/evaluate/light', { config: allOnes })).rtp).toBe(1);
  await page.getByRole('tab', { name: 'Build', exact: true }).click();
  await page.getByLabel('Import project file', { exact: true }).setInputFiles({ name: 'all-ones.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(allOnes)) });
  await openReels(page);
  await expect(page.getByText(/at least 1 One symbols/)).toBeVisible();
  await expect(page.getByLabel('Reel 1 symbol at 0', { exact: true })).toHaveValue('1');
});

test('A late strip CSV read cannot overwrite newer manual edits', async ({ page }) => {
  await page.addInitScript(() => {
    const original = File.prototype.text;
    const host = window as unknown as { csvStarted: boolean; csvFinished: boolean; releaseCsv(): void };
    const gate = new Promise<void>(resolve => { host.releaseCsv = resolve; });
    File.prototype.text = async function () {
      const text = await original.call(this);
      if (this.name === 'late.csv') { host.csvStarted = true; await gate; host.csvFinished = true; }
      return text;
    };
  });
  await page.goto(link(fixture)); await openReels(page);
  await page.getByLabel('Strip CSV file', { exact: true }).setInputFiles({ name: 'late.csv', mimeType: 'text/csv', buffer: Buffer.from('reel,index,symbolId\n1,0,0\n1,1,0\n2,0,1\n2,1,1') });
  await expect.poll(() => page.evaluate(() => (window as unknown as { csvStarted: boolean }).csvStarted)).toBe(true);
  await page.getByLabel('Reel 1 symbol at 0', { exact: true }).selectOption('1');
  await page.evaluate(() => (window as unknown as { releaseCsv(): void }).releaseCsv());
  await expect.poll(() => page.evaluate(() => (window as unknown as { csvFinished: boolean }).csvFinished)).toBe(true);
  await expect(page.getByLabel('Reel 1 symbol at 0', { exact: true })).toHaveValue('1');
  await expect(page.getByLabel('Reel 1 symbol at 1', { exact: true })).toHaveValue('1');
  await expect(page.getByText(/CSV preview staged/)).toHaveCount(0);
});
