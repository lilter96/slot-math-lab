import {calculateWithQuota, expect, getWithQuota, postWithQuota, test} from './fixtures';

test('Pinned population analysis preserves exact moments, distinguishes ruin and clears edited evidence', async ({
                                                                                                                     page,
                                                                                                                     request
                                                                                                                 }) => {
    test.setTimeout(180000);
    const config = {
        schemaVersion: '1.0.0', name: 'Exact population regression', nodes: [
            {
                nodeType: 'draw',
                id: 'd',
                drawWeights: [{outcomeId: 'zero', value: 0, weight: 1}, {outcomeId: 'two', value: 2, weight: 1}],
                outputs: {out: {name: 'out', type: 'Number'}}
            },
            {nodeType: 'metricsSink', id: 's', winCap: 10, inputs: {in: {name: 'in', type: 'Number'}}},
        ], edges: [{id: 'e', sourceNodeId: 'd', sourcePort: 'out', targetNodeId: 's', targetPort: 'in'}]
    };
    const saved = await postWithQuota(request, '/api/configs', {data: {config}});
    expect(saved.status()).toBe(201);
    const launched = await postWithQuota(request, '/api/runs', {
        data: {
            configId: (await saved.json()).id,
            sampleSize: 100,
            seed: 42,
            degreeOfParallelism: 1
        }
    });
    expect(launched.status()).toBe(202);
    const run = await launched.json();
    await expect.poll(async () => (await (await getWithQuota(request, `/api/runs/${run.id}`)).json()).status).toBe('completed');
    await page.goto(`/results?run=${run.id}`);
    const panel = page.getByRole('region', {name: 'Exact population analysis', exact: true});
    await expect(panel).toBeVisible();
    await panel.getByLabel('Collect solver and logical storage diagnostics').check();
    const response = page.waitForResponse(value => value.url().endsWith('/api/evaluate/graph') && value.status() === 200);
    await calculateWithQuota(page, '/api/evaluate/graph', 'Calculate exact population');
    const moments = await (await response).json();
    expect(moments).toMatchObject({
        requestedEvidence: 'Moments',
        distribution: 'NotRequested',
        analysis: {
            status: 'ExactMoments',
            mean: {rational: '1/1'},
            variance: {rational: '1/1'},
            fourthMoment: {rational: '8/1'},
            excessKurtosis: {rational: '-2/1'}
        }
    });
    await expect(panel.getByText('Exact population moments', {exact: true})).toBeVisible();
    await expect(panel.getByText(/Tail quantiles, expected shortfall/)).toBeVisible();
    await panel.getByText('Exact raw and central moments', {exact: true}).click();
    await panel.getByText('Solver diagnostics · Auto', {exact: true}).click();
    await expect(panel).toContainText('LoopKernelCoefficientSlots');
    for (const [width, height] of [[1440, 900], [390, 844]]) {
        await page.setViewportSize({width, height});
        for (const [name, anchor] of [['moments', '.population-metrics'], ['diagnostics', '.population-result details:last-of-type']] as const) {
            await panel.locator(anchor).first().evaluate(element => element.scrollIntoView({block: 'start'}));
            await page.screenshot({path: test.info().outputPath(`${name}-${width}.png`)});
        }
    }
    await panel.getByLabel('Calculate', {exact: true}).selectOption('Report');
    await expect(panel.getByLabel('Exact population result')).toHaveCount(0);
    await panel.getByLabel('Include independent-round operator exposure').check();
    await panel.getByLabel('Settled rounds', {exact: true}).fill('2');
    await panel.getByLabel('Initial reserve · optional credits', {exact: true}).fill('0');
    const lawResponse = page.waitForResponse(value => value.url().endsWith('/api/evaluate/graph') && value.status() === 200);
    await calculateWithQuota(page, '/api/evaluate/graph', 'Calculate exact population');
    const law = await (await lawResponse).json();
    expect(law.analysis.independentRounds).toMatchObject({
        probabilityFinalLossExceedsReserve: {rational: '1/4'},
        probabilityRuinByHorizon: {rational: '1/2'}
    });
    await expect(panel.getByRole('heading', {name: /Single-round upper-tail risk/})).toBeVisible();
    await expect(panel.getByRole('region', {name: 'Independent-round operator exposure'})).toContainText('1/4');
    const download = page.waitForEvent('download');
    await panel.getByRole('button', {name: 'Export population evidence'}).click();
    expect((await download).suggestedFilename()).toBe(`run-${run.id}-exact-population.json`);
    for (const [width, height] of [[1440, 900], [390, 844]]) {
        await page.setViewportSize({width, height});
        for (const [name, anchor] of [['form', '.population-fields'], ['risk', '.population-result h4'], ['exposure', '.population-exposure'], ['bottom', '.population-fingerprint']] as const) {
            await panel.locator(anchor).first().evaluate(element => element.scrollIntoView({block: 'start'}));
            await page.screenshot({path: test.info().outputPath(`population-${name}-${width}.png`)});
        }
        if (width === 390) {
            for (const control of await panel.locator('.population-fields input, .population-fields select, .population-actions button').all())
                expect((await control.boundingBox())!.height).toBeGreaterThanOrEqual(44);
        }
    }
    for (const [width, height] of [[1440, 900], [390, 844]]) {
        await page.setViewportSize({width, height});
        for (const name of ['Overview', 'Distribution', 'Compare', 'Reproducibility']) {
            await page.getByLabel('Result sections').getByRole('tab', {name, exact: true}).click();
            await expect(page.getByRole('tabpanel', {name, exact: true})).toBeVisible();
            expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
            await page.screenshot({path: test.info().outputPath(`results-${name}-${width}.png`), fullPage: true});
        }
        await page.getByLabel('Result sections').getByRole('tab', {name: 'Overview', exact: true}).click();
    }
    await expect(panel.getByLabel('Calculate', {exact: true})).toHaveValue('Report');
    await expect(panel.getByText('Complete exact law', {exact: true})).toBeVisible();
    // An accepted response for a different graph must never become displayed evidence.
    await page.route('**/api/evaluate/graph', async route => {
        const response = await route.fetch();
        const output = await response.json();
        await route.fulfill({json: {...output, configHash: 'a'.repeat(64)}});
    });
    await panel.getByLabel('Calculate', {exact: true}).selectOption('Mean');
    await panel.getByRole('button', {name: 'Calculate exact population', exact: true}).click();
    await expect(panel.getByRole('alert')).toContainText('fingerprint differs');
    await expect(panel.getByLabel('Exact population result')).toHaveCount(0);
    await page.unroute('**/api/evaluate/graph');
    await page.route('**/api/evaluate/graph', route => route.fulfill({
        json: {
            configHash: run.configHash, provenance: 'BudgetExceeded', status: 'BudgetExceeded', elapsedMs: 1,
            note: 'The calculation exceeded its budget. No exact RTP has been calculated.'
        }
    }));
    await calculateWithQuota(page, '/api/evaluate/graph', 'Calculate exact population');
    await expect(panel.getByText('Exact analysis unavailable', {exact: true})).toBeVisible();
    await expect(panel.locator('.population-metrics')).toHaveCount(0);
    await page.screenshot({path: test.info().outputPath('population-unavailable.png')});
    await page.unroute('**/api/evaluate/graph');
    let calls = 0;
    await page.route('**/api/evaluate/graph', route => {
        calls++;
        return route.fulfill({status: 500, json: {error: 'Calculation service unavailable.'}});
    });
    await panel.getByRole('button', {name: 'Calculate exact population', exact: true}).click();
    await expect(panel.getByRole('alert')).toContainText('Calculation service unavailable');
    expect(calls).toBe(1);
    await expect(panel.getByRole('status')).toHaveText('Calculation failed. No result retained.');
    await page.screenshot({path: test.info().outputPath('population-error.png')});
    await page.unroute('**/api/evaluate/graph');
    let release!: () => void, completed!: () => void, arrived!: () => void;
    const gate = new Promise<void>(resolve => {
        release = resolve;
    });
    const routed = new Promise<void>(resolve => {
        arrived = resolve;
    });
    const handled = new Promise<void>(resolve => {
        completed = resolve;
    });
    await page.route('**/api/evaluate/graph', async route => {
        arrived();
        await gate;
        try {
            await route.fulfill({json: {...moments, configHash: run.configHash}});
        } catch { /* The browser may already have aborted the request. */
        } finally {
            completed();
        }
    });
    await panel.getByRole('button', {name: 'Calculate exact population', exact: true}).click();
    await routed;
    await expect(panel.getByRole('button', {name: 'Cancel analysis', exact: true})).toBeVisible();
    await panel.getByRole('button', {name: 'Cancel analysis', exact: true}).click();
    release();
    await handled;
    await expect(panel.getByRole('status')).toHaveText('Calculation cancelled. No result retained.');
    await expect(panel.getByLabel('Exact population result')).toHaveCount(0);
});
