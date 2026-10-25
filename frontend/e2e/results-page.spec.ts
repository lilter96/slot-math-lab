import { test, expect, getWithQuota } from './fixtures';

const config = { schemaVersion: '1.0.0', name: 'Results design review', initialState: { targetRtpPercent: 75 }, nodes: [
  { id: 'draw', nodeType: 'draw', label: 'Coin draw', drawWeights: [{ outcomeId: 'one', weight: 3, value: 1 }, { outcomeId: 'zero', weight: 1, value: 0 }], outputs: { out: { name: 'out', type: 'Wins' } } },
  { id: 'sink', nodeType: 'metricsSink', label: 'Payout', winCap: 10, inputs: { in: { name: 'in', type: 'Wins' } } },
], edges: [{ id: 'edge', sourceNodeId: 'draw', sourcePort: 'out', targetNodeId: 'sink', targetPort: 'in' }] };

test('Complete Results page supports archive, four sections, responsive layout and pinned evidence', async ({ page, request }) => {
  const saved = await request.post('/api/configs', { data: { config } }); expect(saved.status()).toBe(201);
  const configId = (await saved.json()).id;
  const launched = await request.post('/api/runs', { data: { configId, sampleSize: 10000, seed: 42, degreeOfParallelism: 1 } }); expect(launched.status()).toBe(202);
  const run = await launched.json();
  await expect.poll(async () => (await (await getWithQuota(request, `/api/runs/${run.id}`)).json()).status).toBe('completed');
  await page.goto(`/results?run=${run.id}`);
  await expect(page.getByRole('heading', { name: config.name, exact: true })).toBeVisible();
  await expect(page.getByTestId('results-rtp')).not.toHaveText('—');
  await page.getByRole('button', { name: 'Calculate reference', exact: true }).click();
  await expect(page.locator('.results-reference-value')).toHaveText('75.000%');
  for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
    await page.setViewportSize({ width, height });
    for (const name of ['Overview', 'Distribution', 'Compare', 'Reproducibility']) {
      await page.getByLabel('Result sections').getByRole('tab', { name, exact: true }).click();
      await expect(page.getByRole('tabpanel', { name, exact: true })).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: `../artifacts/ui/results-page-${test.info().project.name}-${name.toLowerCase()}-${width}.png` });
      if (name === 'Overview') {
        const tools = page.getByRole('region', { name: 'Verification experiments' });
        for (const tool of ['Parameters & policies', 'Verified sampling design', 'Resource-stop impact', 'Rule / spec / asset evidence']) {
          await tools.getByRole('tab', { name: tool, exact: true }).click();
          await expect(tools.getByRole('tabpanel')).toBeVisible();
          if (tool === 'Parameters & policies') await tools.getByRole('button', { name: 'Add variant', exact: true }).click();
          if (tool === 'Verified sampling design') await tools.getByRole('button', { name: 'Load independent two-atom example', exact: true }).click();
          if (tool === 'Resource-stop impact') await tools.getByRole('button', { name: 'Add state', exact: true }).click();

          expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
          await tools.screenshot({ path: `../artifacts/ui/results-tools-${test.info().project.name}-${tool.split(' ')[0].toLowerCase()}-${width}.png` });
          if (width === 390) {
            for (const [index, group] of (await tools.locator('fieldset').all()).entries()) {
              await group.scrollIntoViewIfNeeded();
              await page.screenshot({ path: `../artifacts/ui/results-tool-fields-${test.info().project.name}-${tool.split(' ')[0].toLowerCase()}-${index}.png` });
            }
          }

        }
        const enumeration = page.locator('.graph-measurement-reference');
        if (!await enumeration.evaluate(element => element.hasAttribute('open'))) await enumeration.locator('summary').first().click();
        await enumeration.scrollIntoViewIfNeeded();
        await page.screenshot({ path: `../artifacts/ui/results-enumeration-${test.info().project.name}-${width}.png` });
        await page.locator('.execution-report').scrollIntoViewIfNeeded();
        await page.screenshot({ path: `../artifacts/ui/results-exposure-${test.info().project.name}-${width}.png` });
        await page.locator('.results-report-footer').scrollIntoViewIfNeeded();
        await page.screenshot({ path: `../artifacts/ui/results-bottom-${test.info().project.name}-${width}.png` });
      }

    }
    if (width === 390) {
      const archive = page.getByRole('complementary', { name: 'Run archive' }); await expect(archive).toBeHidden();
      await page.getByRole('button', { name: /Browse saved runs/ }).click(); await expect(archive).toBeVisible();
      await archive.getByLabel('Search saved runs').fill(config.name);
      await archive.getByRole('button', { name: new RegExp(`run ${run.id}`) }).click(); await expect(archive).toBeHidden();
    }
  }
  await page.getByLabel('Result sections').getByRole('tab', { name: 'Overview', exact: true }).focus(); await page.keyboard.press('End');
  await expect(page.getByRole('tab', { name: 'Reproducibility', exact: true })).toBeFocused();
  await page.getByRole('button', { name: 'Open pinned graph ↗', exact: true }).click(); await expect(page).toHaveURL(/build/);
});

test('Results empty and unavailable states give a useful recovery path', async ({ page }) => {
  await page.route('**/api/runs?*', route => route.fulfill({ json: { items: [], total: 0, completed: 0, active: 0, partial: 0, failed: 0, nextCursor: null } }));
  await page.goto('/results'); await expect(page.getByRole('heading', { name: 'Your calculation history starts here' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Start a simulation', exact: true })).toHaveAttribute('href', '/simulate');
  await page.screenshot({ path: `../artifacts/ui/results-page-${test.info().project.name}-empty.png` });
  await page.route('**/api/runs/missing/evidence', route => route.fulfill({ status: 404, json: { error: 'This private run is unavailable.' } }));
  await page.route('**/api/runs/missing', route => route.fulfill({ status: 404, json: { error: 'This private run is unavailable.' } }));
  await page.goto('/results?run=missing'); await expect(page.getByRole('alert')).toContainText('private run is unavailable');
  await expect(page.getByRole('button', { name: 'Retry this run', exact: true })).toBeVisible();
  await page.screenshot({ path: `../artifacts/ui/results-page-${test.info().project.name}-unavailable.png` });
});

 test('Unsupported pinned model response shows a recoverable Results error without rounded evidence', async ({ page }) => {
  await page.route('**/api/runs?*', route => route.fulfill({ json: { items: [], total: 0, completed: 0, active: 0, partial: 0, failed: 0, nextCursor: null } }));
  await page.route('**/api/runs/unsafe/evidence', route => route.fulfill({ contentType: 'application/json', body: '{"pinnedConfig":{"initialState":{"wide":9007199254740993}}}' }));
  await page.route('**/api/runs/unsafe', route => route.fulfill({ status: 404, json: { error: 'Unavailable' } }));
  await page.goto('/results?run=unsafe');
  await expect(page.getByRole('alert')).toContainText('exact browser range');
  await expect(page.getByTestId('results-rtp')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Retry this run', exact: true })).toBeVisible();
 });
