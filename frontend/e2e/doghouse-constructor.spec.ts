import { test, expect, waitForRunLaunch } from './fixtures';
import fs from 'node:fs/promises';
async function build(page: import('@playwright/test').Page) { await page.goto('/build'); await page.getByRole('button', { name: 'Build Dog House graph', exact: true }).click(); }
async function focus(page: import('@playwright/test').Page, id: string) { await page.getByLabel('Focus graph node').selectOption(id); }

test('A fresh Build opens the complete model and all math graphs without loading a preset manually', async ({ page }) => {
  await page.goto('/build');
  await expect(page.locator('.react-flow__node')).toHaveCount(12);
  await expect(page.getByTestId('model-navigation')).toContainText('target 98%');
  await expect(page.locator('.react-flow__node[data-id="base-spin"]')).toBeVisible();
  await page.locator('.react-flow__node[data-id="base-spin"]').getByRole('button', { name: /Open .* subgraph/ }).click();
  await expect(page.getByLabel('Focus graph node')).toContainText('base reel 1 stop');
  await page.getByRole('button', { name: 'Open payline graph', exact: true }).click();
  await expect(page.locator('.react-flow__node')).toHaveCount(8);
  await focus(page, 'sum'); await expect(page.getByLabel('Expression operator', { exact: true })).toHaveValue('Add');
  await page.getByRole('button', { name: 'Open free spins graph', exact: true }).click();
  await expect(page.getByLabel('Focus graph node')).toContainText('Restore Sticky Wild positions');
  await page.getByRole('button', { name: 'Open reel and payout tables', exact: true }).click();
  await expect(page.getByLabel('Data table')).toHaveValue('linePaytable');
  await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
  await page.getByRole('button', { name: 'Open exact RTP and calibration graph', exact: true }).click();
  await expect(page.getByLabel('Focus graph node')).toContainText('Solve P(×3)');
  await expect(page.getByLabel('Expression operator', { exact: true })).toHaveValue('Add');
  await page.getByRole('button', { name: 'Open full game graph', exact: true }).click();
  await expect(page.locator('.react-flow__node')).toHaveCount(12);
  await page.getByRole('button', { name: 'Play this model', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Spin', exact: true })).toBeVisible();
});

test('A direct model link opens the full canvas and retains edits saved inside a subgraph', async ({ page }) => {
  await page.goto('/build?project=dog-house');
  await expect(page.locator('.react-flow__node')).toHaveCount(12);
  await page.getByRole('button', { name: 'Open reel and payout tables', exact: true }).click();
  await page.getByLabel('Data row 11', { exact: true }).fill('200');
  await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
  await page.getByRole('button', { name: 'Open base game graph', exact: true }).click();
  await page.goto('/build?project=dog-house');
  await expect(page.locator('.react-flow__node')).toHaveCount(12);
  await page.getByRole('button', { name: 'Open reel and payout tables', exact: true }).click();
  await expect(page.getByLabel('Data row 11', { exact: true })).toHaveValue('200');
});

test('Dog House math is editable in nested constructor graphs and affects actual payouts', async ({ page }) => {
  await build(page);
  await focus(page, 'base-spin'); await page.getByRole('button', { name: 'Open subgraph', exact: true }).click();
  await focus(page, 'wild-1'); await expect(page.getByLabel('Outcome 2 weight')).toHaveValue('1');
  await page.getByLabel('Outcome 2 weight').fill('2'); await page.getByLabel('Outcome 2 weight').fill('1');
  await focus(page, 'line-1'); await page.getByRole('button', { name: 'Open subgraph', exact: true }).click();
  await focus(page, 'sum'); await expect(page.getByLabel('Expression operator', { exact: true })).toHaveValue('Add');
  await page.getByLabel('Label', { exact: true }).fill('Add line win · reviewed in UI');
  await page.getByRole('button', { name: 'Save subgraph and return' }).click();
  await page.getByRole('button', { name: 'Save subgraph and return' }).click();
  await page.getByRole('button', { name: 'Edit project data and expressions' }).click();
  await page.getByLabel('Data table').selectOption('linePaytable');
  // Shih Tzu, four-of-a-kind: 100 → 200 line coins. Seed 42/0 has line 11 with two ×2 Wilds.
  await page.getByLabel('Data row 11', { exact: true }).fill('200');
  await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
  const downloadPromise = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export project', exact: true }).click();
  const project = JSON.parse(await fs.readFile((await (await downloadPromise).path())!, 'utf8'));
  expect(project.mechanics['dog-line'].nodes.find((n: {id:string}) => n.id === 'sum').label).toContain('reviewed in UI');
  expect(project.initialState.linePaytable[10]).toBe('200');
  await page.reload(); await page.getByRole('tab', { name: 'Play', exact: true }).click();
  const response = page.waitForResponse(r => r.url().endsWith('/api/play/round'));
  await page.getByRole('button', { name: 'Spin', exact: true }).click();
  const result = await (await response).json(); expect(result.win).toBe(40);
  expect(result.state.lineFactorHistory[0][10]).toBe('4');
  await expect(page.getByTestId('game-credit')).toHaveText('10,039.00');
  expect(result.configHash).toHaveLength(64);
  await page.reload(); await expect(page.getByTestId('game-credit')).toHaveText('10,039.00');
  await page.getByRole('button', { name: 'Game settings' }).click(); await expect(page.getByLabel('Game round index')).toHaveValue('1');
  await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
});

test('All modes and independent rational expectation run through the real UI graph pipeline', async ({ page }) => {
  test.setTimeout(90000);
  await build(page); await page.getByRole('tab', { name: 'Play', exact: true }).click();
  await page.getByRole('button', { name: 'Verify RTP', exact: true }).click();
  await page.getByRole('button', { name: 'Run rational expectation graph', exact: true }).click();
  await expect(page.getByTestId('reference-result')).toContainText('98.0000%');
  await expect(page.getByTestId('rtp-target')).toContainText('Target verified');
  await page.getByLabel('Analysis samples').fill('1000');
  await page.getByRole('button', { name: 'Verify all modes', exact: true }).click();
  await expect(page.getByTestId('math-Exact')).toContainText('BudgetExceeded', { timeout: 20000 });
  await expect(page.getByTestId('math-Pruned')).toContainText('ExactInterval', { timeout: 20000 });
  await expect(page.getByTestId('math-Sampled')).toContainText('n=1,000', { timeout: 20000 });
  await expect(page.getByTestId('math-Hybrid')).toContainText('Sampled · Complete', { timeout: 20000 });
  const promise = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export verification JSON', exact: true }).click();
  const report = JSON.parse(await fs.readFile((await (await promise).path())!, 'utf8'));
  const exact = report.reference.state.expectedRtp;
  const expected = Number(exact.numerator) / Number(exact.denominator);
  expect(expected).toBeCloseTo(0.98, 12);
  expect(report.targetRtp).toBe(0.98); expect(report.targetVerified).toBe(true);
  expect(report.results.Pruned.lower).toBeLessThanOrEqual(expected); expect(report.results.Pruned.upper).toBeGreaterThanOrEqual(expected);
  expect(report.results.Sampled.rtp).toBe(report.results.Hybrid.rtp);
  expect(report.results.Exact.rtp).toBeUndefined();
  await page.getByRole('button', { name: 'Open expectation graph in constructor', exact: true }).click();
  await expect(page).toHaveURL('/build'); await focus(page, 'rtp-total');
  await expect(page.getByLabel('Expression operator', { exact: true })).toHaveValue('Add');
  await page.getByRole('button', { name: 'Edit project data and expressions' }).click();
  await page.getByRole('button', { name: 'Execute & inspect', exact: true }).click(); await page.getByRole('button', { name: 'Execute graph', exact: true }).click();
  await expect(page.getByTestId('executed-state')).toContainText(exact.numerator, { timeout: 10000 });
  await page.getByRole('button', { name: 'Close dialog', exact: true }).click(); await page.getByRole('button', { name: 'Return to game graph', exact: true }).click();
  await expect(page.getByLabel('Focus graph node')).toContainText('Base game');
});

test('Bonus, sticky multipliers, replay and all game controls use the constructor state trace', async ({ page }) => {
  test.setTimeout(60000);
  await build(page); await page.getByRole('tab', { name: 'Play', exact: true }).click();
  await page.getByRole('button', { name: 'Game rules and paytable' }).click();
  await expect(page.getByRole('dialog')).toContainText('20 fixed lines'); await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
  await page.getByRole('button', { name: 'Increase bet' }).click(); await page.getByRole('button', { name: 'Decrease bet' }).click();
  await page.getByRole('button', { name: 'Toggle sound' }).click(); await expect(page.getByRole('button', { name: 'Toggle sound' })).toHaveAttribute('aria-pressed', 'true');
  await page.getByRole('button', { name: 'Toggle quick spin' }).click();
  await page.getByRole('button', { name: 'Game settings' }).click(); await page.getByLabel('Game round index').fill('230'); await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
  const response = page.waitForResponse(r => r.url().endsWith('/api/play/round')); await page.getByRole('button', { name: 'Spin', exact: true }).click();
  const raw = await (await response).json(); expect(raw.state.bonusGrid).toHaveLength(9); expect(raw.state.boards.length).toBe(Number(raw.state.fsCount) + 1);
  await expect(page.getByRole('dialog', { name: 'Free spins won!' })).toBeVisible();
  await page.getByRole('button', { name: 'Start free spins', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Bonus complete' })).toBeVisible({ timeout: 15000 });
  expect(raw.state.multiplierHistory.slice(1).some((row: string[]) => row.some(x => x !== '0'))).toBeTruthy();
  for (let frame = 2; frame < raw.state.boards.length; frame++) for (let p = 0; p < 15; p++) if (raw.state.multiplierHistory[frame - 1][p] !== '0') {
    expect(raw.state.boards[frame][p]).toBe('2'); expect(raw.state.multiplierHistory[frame][p]).toBe(raw.state.multiplierHistory[frame - 1][p]);
  }
  await page.getByRole('button', { name: 'Continue', exact: true }).click(); const credit = await page.getByTestId('game-credit').textContent();
  await page.getByRole('button', { name: 'Round history' }).click(); const replay = page.waitForResponse(r => r.url().endsWith('/api/play/round')); await page.getByRole('button', { name: 'Replay', exact: true }).click();
  const replayed = await (await replay).json(); expect(replayed.state.boards).toEqual(raw.state.boards); expect(replayed.win).toBe(raw.win);
  await expect(page.getByTestId('game-credit')).toHaveText(credit!);
  await page.getByRole('button', { name: 'Start free spins', exact: true }).click(); await page.getByRole('button', { name: 'Continue', exact: true }).click({ timeout: 15000 });
  await page.getByRole('button', { name: 'Autoplay', exact: true }).click(); await page.getByLabel('Autoplay rounds').selectOption('5'); await page.getByRole('button', { name: 'Start autoplay', exact: true }).click();
  await expect(page.getByRole('button', { name: /Stop autoplay/ })).toBeVisible(); await page.getByRole('button', { name: /Stop autoplay/ }).click();
  await expect(page.getByRole('button', { name: 'Autoplay', exact: true })).toBeVisible();
});

test('98% target calibration is calculated by editable AST and applied to actual UI draw weights', async ({ page }) => {
  test.setTimeout(60000);
  await build(page);
  await page.getByRole('button', { name: 'Edit project data and expressions' }).click();
  await page.getByLabel('Data table').selectOption('targetRtpPercent'); await expect(page.getByLabel('State field value')).toHaveValue('98');
  await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
  await focus(page, 'free-spin'); await page.getByRole('button', { name: 'Open subgraph', exact: true }).click();
  await focus(page, 'wild-1'); await page.getByLabel('Outcome 2 weight').fill('1'); await page.getByLabel('Outcome 3 weight').fill('1');
  await page.getByRole('button', { name: 'Save subgraph and return' }).click();
  await page.getByRole('tab', { name: 'Play', exact: true }).click(); await page.getByRole('button', { name: 'Verify RTP', exact: true }).click();
  await page.getByRole('button', { name: 'Run rational expectation graph', exact: true }).click();
  await expect(page.getByTestId('rtp-target')).toContainText('Target not met');
  await page.getByRole('button', { name: 'Apply calibrated Wild weights', exact: true }).click(); await expect(page).toHaveURL('/build');
  await focus(page, 'free-spin'); await page.getByRole('button', { name: 'Open subgraph', exact: true }).click();
  for (const col of [1, 2, 3]) {
    await focus(page, `wild-${col}`);
    await expect(page.getByLabel('Outcome 2 weight')).toHaveValue('303792868695691');
    await expect(page.getByLabel('Outcome 3 weight')).toHaveValue('696207131304309');
  }
  await page.getByRole('button', { name: 'Save subgraph and return' }).click(); await page.reload();
  await page.getByRole('tab', { name: 'Play', exact: true }).click(); await page.getByRole('button', { name: 'Verify RTP', exact: true }).click();
  await page.getByRole('button', { name: 'Run rational expectation graph', exact: true }).click();
  await expect(page.getByTestId('rtp-target')).toContainText('Target verified'); await expect(page.getByTestId('reference-result')).toContainText('98.0000%');
});

test('Mobile game and constructor data dialogs remain usable', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 }); await build(page);
  await page.getByRole('button', { name: 'Edit project data and expressions' }).click(); await page.getByLabel('Data table').selectOption('baseReel0');
  await expect(page.getByLabel('Data row 1', { exact: true })).toBeVisible(); await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
  await page.getByRole('tab', { name: 'Play', exact: true }).click(); await expect(page.getByRole('button', { name: 'Spin', exact: true })).toBeVisible();
  const box = await page.getByTestId('game-reels').boundingBox(); expect(box!.x).toBeGreaterThanOrEqual(0); expect(box!.x + box!.width).toBeLessThanOrEqual(390);
  await page.getByRole('button', { name: 'Game rules and paytable' }).click(); await expect(page.getByRole('dialog')).toBeVisible(); await page.keyboard.press('Escape'); await expect(page.getByRole('dialog')).toHaveCount(0);
});

test('The same Dog House constructor graph saves and runs through Simulate, Results and Export', async ({ page }) => {
  test.setTimeout(45000);
  await build(page); await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await page.getByLabel('Simulation spins').fill('1000'); await page.getByLabel('Replay seed').fill('42');
  const save = page.waitForResponse(r => r.url().endsWith('/api/configs') && r.request().method() === 'POST' && r.status() !== 429, { timeout: 70000 });
  const creation = waitForRunLaunch(page);
  await page.getByRole('button', { name: /start run/i }).click(); expect((await save).status()).toBe(201);
  const run = await (await creation).json();
  await expect.poll(async () => (await (await page.request.get(`/api/runs/${run.id}`)).json()).status, { timeout: 20000 }).toBe('completed');
  const measured = JSON.parse((await (await page.request.get(`/api/runs/${run.id}`)).json()).resultJson);
  expect(measured.seed).toBe(42); expect(measured.rtp).toBeGreaterThan(0);
  await page.getByRole('tab', { name: 'Results', exact: true }).click(); await expect(page.getByRole('heading', { name: 'The Dog House · UI graph' })).toBeVisible();
  await page.getByRole('tab', { name: 'Export', exact: true }).click();
  const html = page.getByRole('button', { name: /html/i }).first(); await expect(html).toBeEnabled({ timeout: 10000 });
  const download = page.waitForEvent('download'); await html.click();
  const report = await fs.readFile((await (await download).path())!, 'utf8'); expect(report).toContain('Sampled');
});
