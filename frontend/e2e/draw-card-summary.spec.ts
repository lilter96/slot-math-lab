import {expect, test, waitForWorkspace} from './fixtures';
import {readFile} from 'node:fs/promises';
import path from 'node:path';

const rows = [{outcomeId: 'positive', weight: 10000000000, value: 10000000001}, {
    outcomeId: 'negative',
    weight: 10000000000,
    value: -10000000000
}];
const source = {
    schemaVersion: '1.0.0', name: 'Exact authored Draw summary', nodes: [
        {id: 'draw', nodeType: 'draw', label: 'Signed draw', drawWeights: rows},
        {id: 'sink', nodeType: 'metricsSink', winCap: 10000},
    ], edges: [], unknown: {retained: true}
};
const link = (model: unknown) => '/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(model))).toString('base64'));

test('Canvas Draw mean is the exact authored fraction, wraps on all sizes and preserves the model on export', async ({page}) => {
    await page.goto(link(source));
    const card = page.locator('.react-flow__node[data-id="draw"]'), summary = card.getByTestId('draw-source-summary');
    await expect(summary).toHaveText('Static mean1/2');
    await expect(card.locator('.prov')).toHaveCount(0);
    for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
        await page.setViewportSize({width, height});
        await page.getByLabel('Focus graph node').selectOption('draw');
        await page.getByRole('button', {name: 'Close inspector', exact: true}).click();
        await expect(summary).toBeVisible();
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
        await page.screenshot({path: path.resolve('../artifacts/ui', `draw-card-${test.info().project.name}-${width}.png`)});
    }
    const download = page.waitForEvent('download');
    await page.getByRole('button', {name: 'Export project', exact: true}).click();
    expect(JSON.parse(await readFile((await (await download).path())!, 'utf8'))).toMatchObject(source);
});

test('Native and legacy dynamic weights remain explicit even when static rows are absent or misleading', async ({page}) => {
    for (const kind of ['native', 'legacy']) {
        const model = kind === 'native' ? {
            ...source,
            nodes: [{...source.nodes[0], weightExpressionId: 'state-weights'}, source.nodes[1]]
        } : {
            name: 'Legacy dynamic draw',
            nodes: [{
                id: 'draw',
                type: 'draw',
                position: {x: 0, y: 0},
                data: {nodeType: 'draw', label: 'Dynamic draw', drawWeights: rows, weightExpressionId: 'state.weights'}
            }],
            edges: [],
            tables: {schemaVersion: '1.0.0'},
        };
        await page.goto(link(model));
        await expect(page.locator('.react-flow__node[data-id="draw"]').getByTestId('draw-source-summary')).toHaveText('modeDynamic weights');
    }
    await page.goto(link({
        ...source,
        nodes: [{id: 'draw', nodeType: 'draw', label: 'No static rows', weightExpressionId: 'state-weights'}]
    }));
    await expect(page.getByTestId('draw-source-summary')).toHaveText('modeDynamic weights');
    await page.goto(link({
        ...source,
        nodes: [{...source.nodes[0], drawWeights: [{outcomeId: 'zero', weight: 0, value: 1}]}]
    }));
    await expect(page.getByTestId('draw-source-summary')).toHaveText('Static meanUndefined (zero total)');
    await page.goto(link({...source, nodes: [{id: 'draw', nodeType: 'draw', label: 'Unconfigured'}]}));
    await expect(page.getByTestId('draw-source-summary')).toHaveText('modeUnconfigured');
});

test('Long exact fractions wrap inside the Draw card at every supported viewport', async ({page}) => {
    const model = {
        ...source,
        nodes: [{
            ...source.nodes[0],
            drawWeights: [{outcomeId: 'one', weight: Number.MAX_SAFE_INTEGER - 1, value: 1}, {
                outcomeId: 'zero',
                weight: 1,
                value: 0
            }]
        }, source.nodes[1]]
    };
    await page.goto(link(model));
    await waitForWorkspace(page);
    const summary = page.getByTestId('draw-source-summary'), value = summary.locator('.v');
    await expect(value).toHaveText('9007199254740990/9007199254740991');
    for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
        await page.setViewportSize({width, height});
        await page.getByLabel('Focus graph node').selectOption('draw');
        await page.getByRole('button', {name: 'Close inspector', exact: true}).click();
        expect(await value.evaluate(element => {
            const bounds = element.getBoundingClientRect(), row = element.parentElement!.getBoundingClientRect();
            return element.scrollWidth <= element.clientWidth + 1 && bounds.height > Number.parseFloat(getComputedStyle(element).fontSize) * 1.8
                && bounds.right <= row.right + 1 && bounds.left >= row.left - 1;
        })).toBe(true);
        await page.screenshot({path: path.resolve('../artifacts/ui', `draw-card-long-${test.info().project.name}-${width}.png`)});
    }
});
