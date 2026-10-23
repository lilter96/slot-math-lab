import {calculateWithQuota, expect, type Page, saveValidatedDialog, test, waitForWorkspace} from './fixtures';
import {readFile} from 'node:fs/promises';

const constant = (value: string, kind = 'Integer') => ({exprType: 'constant', kind, value});
const graph = {
    schemaVersion: '1.0', name: 'Messy custom graph', opaqueData: {retained: true},
    nodes: [
        {
            id: 'custom',
            nodeType: 'library',
            mechanicName: 'custom-definition',
            outputs: {out: {name: 'out', type: 'Number'}}
        },
        {
            id: 'sink',
            nodeType: 'metricsSink',
            winStateKey: 'win',
            winCap: 100,
            inputs: {in: {name: 'in', type: 'Number'}}
        },
    ],
    edges: [{id: 'edge', sourceNodeId: 'custom', sourcePort: 'out', targetNodeId: 'sink', targetPort: 'in'}],
    mechanics: {
        'custom-definition': {
            name: 'Editable custom definition',
            nodes: [{
                id: 'write',
                nodeType: 'modifyState',
                expressionId: 'award',
                outputKey: 'win',
                outputs: {out: {name: 'out', type: 'Number'}}
            }],
            edges: [],
            expressions: {
                award: {
                    exprType: 'if',
                    condition: constant('true', 'Boolean'),
                    thenExpr: {
                        exprType: 'binary',
                        op: 'Add',
                        left: constant('1/3', 'Rational'),
                        right: constant('2/3', 'Rational')
                    },
                    elseExpr: constant('99')
                }
            },
        },
    },
};

async function exportModel(page: Page) {
    const download = page.waitForEvent('download');
    await page.getByRole('button', {name: 'Export project', exact: true}).click();
    return JSON.parse(await readFile((await (await download).path())!, 'utf8'));
}

test('Simplify previews the complete nested model and applies one reversible exact edit', {tag: '@critical'}, async ({page}, testInfo) => {
    await page.goto('/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(graph))).toString('base64')));
    await waitForWorkspace(page);
    const original = await exportModel(page);
    await calculateWithQuota(page, '/api/validate/simplify', 'Simplify graph');
    const dialog = page.getByRole('dialog', {name: 'Simplify graph', exact: true});
    await expect(dialog).toContainText('1 expression can be simplified.');
    await expect(dialog).toContainText('Calculate exact constants');
    await dialog.getByText('Changed expressions', {exact: true}).click();
    const originalViewport = page.viewportSize();
    for (const viewport of [{width: 1440, height: 1000}, {width: 390, height: 844}]) {
        await page.setViewportSize(viewport);
        await expect(dialog).toBeInViewport();
        expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
        await testInfo.attach(`simplification-preview-${viewport.width}`, {
            body: await page.screenshot({fullPage: true}),
            contentType: 'image/png',
        });
    }
    if (originalViewport) await page.setViewportSize(originalViewport);
    await saveValidatedDialog(page, 'Apply simplification');
    const simplified = await exportModel(page);
    expect(simplified.opaqueData).toEqual(original.opaqueData);
    expect(simplified.nodes).toEqual(original.nodes);
    expect(simplified.edges).toEqual(original.edges);
    expect(simplified.mechanics['custom-definition'].expressions.award).toEqual(constant('1'));
    await page.getByRole('button', {name: 'Undo simplification', exact: true}).click();
    expect(await exportModel(page)).toEqual(original);
    await calculateWithQuota(page, '/api/validate/simplify', 'Simplify graph');
    await expect(dialog).toContainText('1 expression can be simplified.');
    await page.getByRole('button', {name: 'Close dialog', exact: true}).click();
    expect(await exportModel(page)).toEqual(original);
});
