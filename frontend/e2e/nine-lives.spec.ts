import { test, expect } from './fixtures';

test('Nine Lives is editable as nested atomic subgraphs', { tag: '@critical' }, async ({ page }) => {
  await page.goto('/build');
  await page.getByRole('button', { name: 'Build Nine Lives graph', exact: true }).click();
  await expect(page.getByTestId('model-navigation')).toContainText('Nine Lives');
  await page.getByRole('button', { name: 'Open evaluate-components graph', exact: true }).click();
  await expect(page.getByLabel('Focus graph node')).toContainText('sample');
  await page.getByRole('button', { name: 'Open evaluate-mask-components graph', exact: true }).click();
  await expect(page.getByLabel('Focus graph node')).toContainText('no-wilds');
  await page.getByRole('button', { name: 'Open scan-mask-without-wilds graph', exact: true }).click();
  await expect(page.getByLabel('Focus graph node')).toContainText('symbols');
  await page.getByRole('button', { name: 'Open restore-row-major graph', exact: true }).click();
  await page.getByLabel('Focus graph node').selectOption('restore');
  await expect(page.locator('.react-flow__node[data-id="restore"]')).toBeVisible();
  await page.getByRole('button', { name: 'Open full game graph', exact: true }).click();
  await expect(page.locator('.react-flow__node[data-id="round"]')).toBeVisible();
});

test('Nine Lives plays and replays a graph round without changing credits', { tag: '@critical' }, async ({ page }) => {
  await page.goto('/play');
  const spin = page.getByRole('button', { name: 'SPIN', exact: true });
  await expect(spin).toBeEnabled({ timeout: 30000 });
  await page.getByRole('button', { name: 'TURBO OFF', exact: true }).click();
  const response = page.waitForResponse(result => result.url().endsWith('/api/play/round'));
  await spin.click();
  const round = await (await response).json();
  expect(round.configHash).toHaveLength(64);
  expect(round.state.boards[0]).toHaveLength(30);
  await expect(spin).toBeEnabled({ timeout: 30000 });
  await expect(page.getByLabel('Replay config hash')).toHaveValue(round.configHash);
  const balance = await page.locator('.nl-balance').textContent();
  const repeated = page.waitForResponse(result => result.url().endsWith('/api/play/round'));
  await page.getByRole('button', { name: 'Replay round', exact: true }).click();
  const replay = await (await repeated).json();
  expect(replay).toEqual(round);
  await expect(page.locator('.nl-balance')).toHaveText(balance!);
  await page.reload();
  await expect(page.locator('.nl-balance')).toHaveText(balance!);
});

test('Nine Lives purchase resolves nine graph lives and charges the entry once', async ({ page }) => {
  test.setTimeout(180000);
  await page.goto('/play');
  await expect(page.getByRole('button', { name: 'SPIN', exact: true })).toBeEnabled({ timeout: 30000 });
  await page.getByRole('button', { name: 'TURBO OFF', exact: true }).click();
  await page.getByRole('button', { name: /BUY NINE LIVES/ }).click();
  const response = page.waitForResponse(result => result.url().endsWith('/api/play/round'));
  await page.getByRole('button', { name: 'DEAL.', exact: true }).click();
  const round = await (await response).json();
  expect(round.state.boards).toHaveLength(9);
  const numeric = (value: number | string | { numerator: string; denominator: string }) => typeof value === 'object' ? Number(value.numerator) / Number(value.denominator) : Number(value);
  for (let remaining = 9; remaining > 0; remaining--) {
    const next = page.getByRole('button', { name: `NEXT LIFE · ${remaining}`, exact: true });
    await expect(next).toBeEnabled({ timeout: 30000 });
    await next.click();
  }
  await expect(page.getByRole('button', { name: 'SPIN', exact: true })).toBeEnabled({ timeout: 30000 });
  const saved = await page.evaluate(() => JSON.parse(localStorage.getItem('slot-math-lab:nine-lives:v1')!));
  expect(saved.response.balance).toBe(100000 - 240 * 20 + numeric(round.state.roundCoins));
  expect(saved.response.state.remaining).toBe(0);
  expect(saved.nextIndex).toBe(1);
  expect(saved.cursor).toBe(10);
});
