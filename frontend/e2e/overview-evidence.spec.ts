import { test, expect, getWithQuota, waitForRunLaunch, type Page } from './fixtures';

test.setTimeout(180000);

async function calculate(page: Page) {
  const name = `Overview coin ${Date.now()}`;
  const config = { schemaVersion: '1.0.0', id: 'overview-coin', name, nodes: [
    { nodeType: 'draw', id: 'draw', drawWeights: [{ outcomeId: 'zero', weight: 1, value: 0 }, { outcomeId: 'one', weight: 1, value: 1 }], outputs: { out: { name: 'out', type: 'Wins' } } },
    { nodeType: 'metricsSink', id: 'sink', winCap: 10000, inputs: { in: { name: 'in', type: 'Wins' } } },
  ], edges: [{ id: 'edge', sourceNodeId: 'draw', sourcePort: 'out', targetNodeId: 'sink', targetPort: 'in' }] };
  const encoded = Buffer.from(encodeURIComponent(JSON.stringify(config))).toString('base64');
  await page.goto('/simulate?load=' + encodeURIComponent(encoded));
  await page.getByLabel('Simulation spins').fill('100');
  const accepted = waitForRunLaunch(page);
  await page.getByRole('button', { name: /^▶ Start (new )?run$/ }).click();
  const run = await (await accepted).json();
  await page.locator('.run-status').filter({ hasText: 'completed' }).waitFor({ timeout: 90000 });
  return { name, run };
}

test('Overview reads the real pinned result and withholds it after a parameter edit or different project', async ({ page, request }) => {
  const { name, run } = await calculate(page);
  await page.getByRole('tab', { name: 'Overview', exact: true }).click();
  const evidence = page.getByRole('region', { name: 'Analysis evidence' });
  await expect(evidence.getByRole('heading', { name: 'Last verified saved run' })).toBeVisible();
  await expect(evidence).toContainText(run.id);
  await expect(evidence.locator('.prov')).toHaveText('Sampled n=100');
  const authoritative = await (await getWithQuota(request, `/api/runs/${run.id}/evidence`)).json();
  const observed = await evidence.locator('.overview-run-metrics dd').first().innerText();
  expect(Number.parseFloat(observed)).toBeCloseTo(authoritative.run.progress.runningRtp * 100, 3);
  await expect(evidence.getByRole('link', { name: 'Inspect saved run' })).toHaveAttribute('href', `/results?run=${run.id}`);
  await page.getByRole('tab', { name: 'Model data', exact: true }).click();
  await page.getByLabel('Weight for one', { exact: true }).fill('3');
  await page.getByRole('button', { name: 'Validate & apply weights', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Validate & apply weights', exact: true })).toBeDisabled();
  await page.getByRole('tab', { name: 'Overview', exact: true }).click();
  await expect(evidence).toContainText('No verified completed result');
  await expect(evidence.locator('.prov')).toHaveCount(0);
  await page.getByRole('tab', { name: 'My projects', exact: true }).click();
  await page.getByRole('button', { name: 'Create Ancient Dragon project', exact: true }).click();
  await expect(evidence).toContainText('No verified completed result');
  await expect(evidence.locator('.prov')).toHaveCount(0);
  await page.getByRole('tab', { name: 'My projects', exact: true }).click();
  await page.locator('.project-list[aria-label="Personal projects"] .project-row').filter({ hasText: name }).getByRole('button', { name: 'Open project', exact: true }).click();
  await expect(evidence).toContainText('No verified completed result');
});

test('Delayed lookup cannot cross project identity and an offline refresh withholds cached proof', async ({ page }) => {
  const { name } = await calculate(page);
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  let requested = false, released = false;
  await page.route('**/api/runs/latest-model', async route => {
    if (!requested && route.request().postDataJSON()?.config?.name === name) {
      requested = true;
      const response = await route.fetch();
      await gate;
      await route.fulfill({ response });
      released = true;
    } else await route.continue();
  });
  await page.getByRole('tab', { name: 'Overview', exact: true }).click();
  const evidence = page.getByRole('region', { name: 'Analysis evidence' });
  await expect.poll(() => requested).toBe(true);
  await expect(evidence.getByRole('status')).toContainText('Checking saved results');
  await expect(evidence.locator('.prov')).toHaveCount(0);
  await page.getByRole('tab', { name: 'My projects', exact: true }).click();
  await page.getByRole('button', { name: 'Create Ancient Dragon project', exact: true }).click();
  await expect(evidence).toContainText('No verified completed result');
  release();
  await expect.poll(() => released).toBe(true);
  await expect(evidence.locator('.prov')).toHaveCount(0);
  await expect(evidence).not.toContainText('Last verified saved run');
  await page.getByRole('tab', { name: 'My projects', exact: true }).click();
  await page.locator('.project-list[aria-label="Personal projects"] .project-row').filter({ hasText: name }).getByRole('button', { name: 'Open project', exact: true }).click();
  await expect(evidence.getByRole('heading', { name: 'Last verified saved run' })).toBeVisible();
  await page.unroute('**/api/runs/latest-model');
  await page.route('**/api/runs/latest-model', route => route.abort());
  await page.getByRole('tab', { name: 'Build', exact: true }).click();
  await page.getByRole('tab', { name: 'Overview', exact: true }).click();
  await expect(evidence.getByRole('alert')).toBeVisible();
  await expect(evidence.locator('.prov')).toHaveCount(0);
  await expect(evidence.getByRole('link', { name: 'Inspect saved run' })).toHaveCount(0);
  await page.unroute('**/api/runs/latest-model');
  await evidence.getByRole('button', { name: 'Retry saved evidence' }).click();
  await expect(evidence.getByRole('heading', { name: 'Last verified saved run' })).toBeVisible();
});
