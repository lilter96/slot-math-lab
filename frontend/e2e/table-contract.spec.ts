import { test, expect } from './fixtures';
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { createAncientDragonGraph } from '../src/games/ancient-dragon/graph';

const config = { schemaVersion: '1.0.0', name: 'Authored numeric table', reviewerMetadata: { untouched: true }, nodes: [
  { id: 'draw', nodeType: 'draw', drawWeights: Array.from({ length: 60 }, (_, index) => ({ outcomeId: `outcome-${index}`, weight: index + 1, value: index === 0 ? 2147483647 : index })), outputs: { out: { name: 'out', type: 'Wins' } } },
  { id: 'sink', nodeType: 'metricsSink', winCap: 100, inputs: { in: { name: 'in', type: 'Wins' } } },
], edges: [{ id: 'edge', sourceNodeId: 'draw', sourcePort: 'out', targetNodeId: 'sink', targetPort: 'in' }] };
const link = '/model/data?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(config))).toString('base64'));

test('Table density belongs to the verified browser identity', async ({ page }) => {
  let userId = 'table-owner-a';
  await page.route('**/api/auth/status', route => route.fulfill({ json: { required: true, authenticated: true, userId } }));
  await page.goto(link); await page.getByLabel('Table density').selectOption('compact');
  userId = 'table-owner-b'; await page.reload(); await expect(page.getByLabel('Table density')).toHaveValue('regular');
  userId = 'table-owner-a'; await page.reload(); await expect(page.getByLabel('Table density')).toHaveValue('compact');
});

test('Table density persists and sticky headers/primary column remain in the scroll region', async ({ page }) => {
  await page.goto(link);
  const row = page.locator('.math-data-table tbody tr').first();
  const regular = (await row.boundingBox())!.height;
  await page.getByLabel('Table density').selectOption('compact');
  await expect(page.locator('body')).toHaveClass(/compact/);
  const compact = (await row.boundingBox())!.height;
  expect(regular).toBeGreaterThan(compact); expect(compact).toBeGreaterThanOrEqual(36);
  expect((await page.getByLabel('Weight for outcome-0', { exact: true }).boundingBox())!.height).toBeGreaterThanOrEqual(36);
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('slotmath-draft-v1') ?? '{}').configName)).toBe(config.name);
  await page.reload(); await expect(page.getByLabel('Table density')).toHaveValue('compact');
  await page.setViewportSize({ width: 390, height: 844 });
  const scroll = page.locator('.weighted-editor .math-table-scroll');
  await scroll.evaluate(element => { element.scrollTop = 400; element.scrollLeft = 150; });
  const header = page.locator('.math-data-table thead th').first();
  const bounds = (await scroll.boundingBox())!, head = (await header.boundingBox())!;
  expect(Math.abs(head.x - bounds.x)).toBeLessThan(3); expect(Math.abs(head.y - bounds.y)).toBeLessThan(3);
  const primary = page.locator('.math-data-table tbody th').nth(12);
  expect(Math.abs((await primary.boundingBox())!.x - bounds.x)).toBeLessThan(3);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('Keyboard moves through weighted and payout rows without applying the staged edit', async ({ page }) => {
  await page.goto(link);
  const first = page.getByLabel('Weight for outcome-0', { exact: true });
  await first.fill('7'); await first.press('Enter');
  await expect(page.getByLabel('Weight for outcome-1', { exact: true })).toBeFocused();
  await page.keyboard.press('Shift+Enter'); await expect(first).toBeFocused();
  await page.keyboard.press('Alt+ArrowDown'); await expect(page.getByLabel('Weight for outcome-1', { exact: true })).toBeFocused();
  await page.keyboard.press('Alt+ArrowUp'); await expect(first).toBeFocused();
  await expect(first).toHaveAttribute('data-changed', 'true');
  await page.getByRole('tab', { name: 'Build', exact: true }).click();
  const saved = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export project', exact: true }).click();
  expect(JSON.parse(await readFile((await (await saved).path())!, 'utf8'))).toMatchObject(config);
  await page.goto('/model/data?view=payouts&project=nine-lives');
  await page.getByLabel('Coins for 5 cells').fill('2'); await page.getByLabel('Coins for 5 cells').press('Enter');
  await expect(page.getByLabel('Coins for 6 cells')).toBeFocused();
  await page.keyboard.press('Alt+ArrowUp'); await expect(page.getByLabel('Coins for 5 cells')).toBeFocused();
});

test('Raw copy passes full payload and exact fractions; clipboard rejection gives selectable values', async ({ page }) => {
  await page.addInitScript(() => {
    const host = window as unknown as { copiedRaw: string; rejectCopy: boolean };
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: async (value: string) => { if (host.rejectCopy) throw new Error('Denied'); host.copiedRaw = value; } } });
  });
  await page.goto(link);
  await page.getByRole('button', { name: 'Copy raw payload for outcome-0', exact: true }).click();
  expect(await page.evaluate(() => (window as unknown as { copiedRaw: string }).copiedRaw)).toBe('2147483647');
  await page.getByRole('button', { name: 'Copy raw probability for outcome-0', exact: true }).click();
  expect(await page.evaluate(() => (window as unknown as { copiedRaw: string }).copiedRaw)).toBe('1/1830');
  await page.evaluate(() => { (window as unknown as { rejectCopy: boolean }).rejectCopy = true; });
  await page.getByRole('button', { name: 'Copy raw payload for outcome-0', exact: true }).click();
  await expect(page.getByLabel('Raw payload for outcome-0', { exact: true })).toHaveValue('2147483647');
  await expect(page.getByLabel('Raw payload for outcome-0', { exact: true })).toHaveAttribute('readonly', '');
});

test('Actual numeric-table captures retain full values and usable targets at four sizes', async ({ page }) => {
  const observations = [];
  for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
    await page.setViewportSize({ width, height }); await page.goto(link);
    await page.getByLabel('Table density').selectOption('compact');
    await expect(page.getByLabel('Weight for outcome-0', { exact: true })).toBeVisible();
    const row = (await page.locator('.math-data-table tbody tr').first().boundingBox())!.height;
    const input = (await page.getByLabel('Weight for outcome-0', { exact: true }).boundingBox())!.height;
    expect(input).toBeGreaterThanOrEqual(width === 390 ? 44 : 36);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    observations.push({ width, height, row, input });
    await page.screenshot({ path: path.resolve('../docs/design/qa', `table-contract-${width}.png`), fullPage: true });
  }
  await writeFile(path.resolve('../docs/design/qa/table-contract-captures.json'), JSON.stringify(observations, null, 2));
});

test('Payout tables keep raw scaled values and usable fields at four sizes', async ({ page }) => {
  await page.addInitScript(() => {
    const host = window as unknown as { copiedPayout: string };
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: async (value: string) => { host.copiedPayout = value; } } });
  });
  for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
    await page.setViewportSize({ width, height });
    await page.goto('/model/data?view=payouts&project=nine-lives');
    await page.getByLabel('Table density').selectOption('compact');
    await expect(page.getByLabel('Coins for 5 cells')).toHaveValue('1');
    expect((await page.getByLabel('Coins for 5 cells').boundingBox())!.height).toBeGreaterThanOrEqual(width === 390 ? 44 : 36);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: path.resolve('../docs/design/qa', `table-contract-payout-${width}.png`), fullPage: true });
    if (width === 1440) {
      await page.getByRole('button', { name: 'Copy raw payout for 5 cells', exact: true }).click();
      expect(await page.evaluate(() => (window as unknown as { copiedPayout: string }).copiedPayout)).toBe('1/20');
    }
  }
});

test('Reel tables honor regular and compact density without shrinking symbol targets', async ({ page }) => {
  const graph = createAncientDragonGraph();
  await page.goto('/build');
  await page.getByLabel('Import project file', { exact: true }).setInputFiles({ name: 'ancient-dragon.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(graph)) });
  await page.getByRole('tab', { name: 'Model data', exact: true }).click();
  await page.getByRole('tab', { name: 'Reel strips', exact: true }).click();
  const row = page.locator('.reel-data-table tbody tr').first();
  const regular = (await row.boundingBox())!.height;
  await page.getByLabel('Table density').selectOption('compact');
  await expect(page.locator('body')).toHaveClass(/compact/);
  expect((await row.boundingBox())!.height).toBeLessThan(regular);
  expect((await page.getByLabel('Reel 1 symbol at 0', { exact: true }).boundingBox())!.height).toBeGreaterThanOrEqual(36);
  for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
    await page.setViewportSize({ width, height });
    expect((await page.getByLabel('Reel 1 symbol at 0', { exact: true }).boundingBox())!.height).toBeGreaterThanOrEqual(width === 390 ? 44 : 36);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: path.resolve('../docs/design/qa', `table-contract-reel-${width}.png`), fullPage: true });
  }
});
