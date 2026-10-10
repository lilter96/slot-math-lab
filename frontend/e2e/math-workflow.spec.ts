import { test, expect, waitForRunLaunch } from './fixtures';
import { parseExpression } from '../src/lib/expressionParser';

test('Expression authoring creates typed ASTs and rejects invalid syntax', () => {
  expect(parseExpression('state.total >= 3 && true')).toEqual({ exprType: 'binary', op: 'And',
    left: { exprType: 'compare', op: 'Gte', left: { exprType: 'fieldAccess', target: 'state', path: ['total'] }, right: { exprType: 'constant', kind: 'Integer', value: '3' } },
    right: { exprType: 'constant', kind: 'Boolean', value: 'true' } });
  expect(parseExpression('0.25')).toEqual({ exprType: 'constant', kind: 'Rational', value: '25/100' });
  expect(() => parseExpression('2 + @')).toThrow();
});

test('Real UI → API → compiler → exact → sampled workflow', async ({ page }) => {
  await page.goto('/');
  const evaluation = page.waitForResponse(r => r.url().includes('/api/evaluate/light') && r.request().method() === 'POST');
  await page.getByRole('button', { name: 'Load coin example' }).click();
  const response = await evaluation;
  expect(response.ok()).toBeTruthy();
  const metrics = await response.json();
  expect(metrics.strategy).toBe('Exact');
  expect(metrics.rtp).toBe(0.75); // independent closed form (3*1 + 1*3)/8
  expect(metrics.hitFrequency).toBe(0.5);
  await page.reload();
  await expect(page.getByText('Coin draw', { exact: true }).first()).toBeVisible();
  await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  const creation = waitForRunLaunch(page);
  await page.getByRole('button', { name: /start run/i }).click();
  const runResponse = await creation;
  expect(runResponse.status()).toBe(202);
  const run = await runResponse.json();
  expect(run.seed).toBe(42);
  await expect.poll(async () => {
    const response = await page.request.get(`/api/runs/${run.id}`);
    return (await response.json()).status;
  }, { timeout: 30000 }).toBe('completed');
  const result = JSON.parse((await (await page.request.get(`/api/runs/${run.id}`)).json()).resultJson);
  expect(result.seed).toBe(42);
  expect(result.rtp).toBeGreaterThan(0.73);
  expect(result.rtp).toBeLessThan(0.77);
});

for (const [mechanic, expected] of Object.entries({ lines: 5, ways: 5, cluster: 5, scatter: 5, 'sticky-wild': 5, 'hold-and-win': 35, cascade: 2 })) {
  test(`Catalog ${mechanic}: UI serialized graph matches manual payout`, async ({ page }) => {
    await page.goto('/');
    const response = page.waitForResponse(r => r.url().includes('/api/evaluate/light') && r.request().method() === 'POST');
    await page.getByLabel('Load catalog example').selectOption(mechanic);
    const metrics = await (await response).json();
    expect(metrics.strategy).toBe('Exact');
    expect(metrics.rtp).toBe(expected);
  });
}

test('PAR export contains measured RTP and actual provenance', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Load coin example' }).click();
  await page.getByRole('tab', { name: 'Export', exact: true }).click();
  const htmlButton = page.getByRole('button', { name: /html/i }).first();
  await expect(htmlButton).toBeEnabled();
  const downloadPromise = page.waitForEvent('download');
  await htmlButton.click();
  const download = await downloadPromise;
  const fs = await import('node:fs/promises');
  const html = await fs.readFile((await download.path())!, 'utf8');
  expect(html).toContain('75.00%');
  expect(html).toContain('Exact');
});
