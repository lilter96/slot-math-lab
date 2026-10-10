import { waitForRunLaunch, test, expect, getWithQuota, type Page } from './fixtures';
import { readFile } from 'node:fs/promises';

async function metric(page: Page, name: string, expression: string, assertion = false) {
  await page.getByRole('button', { name: '＋ Track metric', exact: true }).click();
  await page.getByLabel('Metric name').fill(name); await page.getByLabel('Metric observation level').selectOption('node');
  await page.getByLabel('Metric graph node').selectOption('sink'); await page.getByLabel('Metric numeric expression').fill(expression); await page.getByLabel('Metric unit').fill('signal');
  if (assertion) {
    await page.getByLabel('Enable advanced measurement').check(); await page.getByLabel('Assert exactly zero or false').check();
  } else {
    await page.getByLabel('Metric filter mode').selectOption('expression'); await page.getByLabel('Metric filter expression').fill('state.signal > 0');
  }
  await page.getByRole('button', { name: 'Save measurement', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
}

test('Native decimal state retains nonzero metrics, exact assertions, charts, exported input and both-engine replay', async ({ page, request }) => {
  test.setTimeout(240000);
  await page.goto('/build'); await page.getByLabel('Import project file').setInputFiles('e2e/fixtures/numeric-input-model.json');
  await expect(page.locator('.react-flow__node')).toHaveCount(2); await page.getByRole('tab', { name: 'Simulate', exact: true }).click();
  await metric(page, 'Tiny signal', 'state.signal'); await metric(page, 'Decimal residual', 'state.signal - 0.000000001', true);
  await page.getByRole('button', { name: 'Configure verification profile', exact: true }).click();
  await page.getByLabel('Verification profile name').fill('Decimal input identity'); await page.getByLabel('Required check 1', { exact: true }).selectOption('observation-integrity');
  await page.getByLabel('Required minimum 1').fill('1000'); await page.getByRole('button', { name: 'Add required check', exact: true }).click();
  await page.getByLabel('Required measurement 2').selectOption({ label: 'Decimal residual' }); await page.getByLabel('Required check 2', { exact: true }).selectOption('exact-zero-assertion');
  await page.getByLabel('Required minimum 2').fill('1000'); await page.getByRole('button', { name: 'Save verification profile', exact: true }).click();
  await expect(page.getByRole('dialog')).not.toBeVisible();
  const records = [];
  for (const engine of ['auto', 'reference']) {
    await page.getByLabel('Simulation spins').fill('1000'); await page.locator('#execution-configuration').evaluate((el: HTMLDetailsElement) => { el.open = true; });
    await page.getByLabel('Sampling engine', { exact: true }).selectOption(engine);
    const launch = waitForRunLaunch(page);
    await page.getByRole('button', { name: /^▶ Start (new )?run$/ }).click(); const run = await (await launch).json();
    await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
    const signal = page.getByRole('article', { name: 'Tracked metric Tiny signal', exact: true });
    for (const reducer of ['min', 'max', 'mean']) await expect(signal.locator(`[data-statistic=${reducer}]`)).toHaveText('1e-9 signal');
    await expect(signal).toContainText('1,000 matching'); await expect(signal).toContainText('0 invalid');
    await expect(signal.locator('svg .plot-label').filter({ hasText: /e-9/ }).first()).toBeVisible();
    await expect(page.getByRole('article', { name: 'Tracked metric Decimal residual', exact: true })).toContainText('no Observed Violations');
    await page.getByRole('button', { name: 'Evaluate pinned verification profile', exact: true }).click();
    await expect(page.getByLabel('Verification profile result')).toHaveAttribute('data-profile-status', 'criteriaMet', { timeout: 70000 });
    const download = page.waitForEvent('download'); await page.getByRole('button', { name: '↓ Export evidence', exact: true }).click();
    const bundle = JSON.parse(await readFile((await (await download).path())!, 'utf8'));
    expect(bundle.pinnedGraph.config.initialState.signal).toBe(1e-9); expect(bundle.progress.measurements[0]).toMatchObject({ count: 1000, min: 1e-9, max: 1e-9, mean: 1e-9, errors: 0 });
    expect(bundle.progress.measurements[1].analysis.assertion.status).toBe('noObservedViolations');
    const evidence = await (await getWithQuota(request, `/api/runs/${run.id}/evidence`)).json();
    expect(evidence.inputVerified).toBe(true); expect(evidence.diagnostics.find((d: { kind: string }) => d.kind === 'verification-profile').output.report.status).toBe('criteriaMet');
    records.push(evidence); await page.reload(); await expect(signal.locator('[data-statistic=mean]')).toHaveText('1e-9 signal');
  }
  expect(records[1].run.progress.measurements).toEqual(records[0].run.progress.measurements);
  expect(records[1].run.verificationProfileHash).toBe(records[0].run.verificationProfileHash);
  await page.goto(`/results?run=${records[0].run.id}`);
  const row = page.getByRole('table').filter({ has: page.getByRole('columnheader', { name: 'Measurement / scope', exact: true }) }).getByRole('row').filter({ hasText: 'Tiny signal' }); await expect(row).toContainText('1e-9 signal');
  await page.getByRole('button', { name: 'Replay pinned run', exact: true }).click(); await page.getByLabel('Pinned run engine').selectOption('reference');
  const replay = waitForRunLaunch(page);
  await page.getByRole('button', { name: 'Start pinned run', exact: true }).click(); const replayed = await (await replay).json();
  expect(replayed.verificationProfileHash).toBe(records[0].run.verificationProfileHash);
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 });
  await expect(page.getByRole('article', { name: 'Tracked metric Tiny signal', exact: true }).locator('[data-statistic=mean]')).toHaveText('1e-9 signal');
});
