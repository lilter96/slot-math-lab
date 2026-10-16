import {readFile} from 'node:fs/promises';
import {expect, type Page, test} from './fixtures';
import {machineProofGraph, weightedMachine} from '../unit/fixtures/machineProofs';
import type {ModelGraph} from '../src/lib/modelProjection';

async function open(page: Page, graph: ModelGraph) {
    await page.goto('/build');
    await page.getByLabel('Import project file').waitFor({state: 'attached', timeout: 70000});
    await page.getByLabel('Import project file').setInputFiles({
        name: 'raw-input-proof.json',
        mimeType: 'application/json',
        buffer: Buffer.from(JSON.stringify(graph))
    });
    await expect(page.locator('.project-name')).toHaveText(String(graph.name));
    await page.goto('/model/machine?scope=weighted-machine');
    await expect(page.getByRole('button', {name: 'Create / reuse', exact: true})).toBeEnabled({timeout: 70000});
    await page.locator('.machine-explorer section').getByRole('button', {name: 'Choose once', exact: false}).click();
    await expect(page.locator('.machine-draw-rows')).toBeVisible();
}

async function download(page: Page, name: string) {
    const promise = page.waitForEvent('download');
    await page.getByRole('button', {name, exact: true}).click();
    return JSON.parse(await readFile((await (await promise).path())!, 'utf8'));
}

for (const field of ['Weight', 'Payload', 'State visit budget', 'Initial value (JSON literal)']) test(`PR19 raw ${field} input cannot round, survive as a valid integer, or reach the canonical graph`, async ({page}) => {
    test.setTimeout(180000);
    const graph = machineProofGraph(weightedMachine());
    await open(page, graph);
    const locator = () => field === 'Initial value (JSON literal)' ? page.locator('.machine-variable').getByLabel(field, {exact: true}).first() : page.getByLabel(field, {exact: true}).first();
    const apply = page.getByRole('button', {name: 'Validate & apply', exact: true});
    for (const raw of ['1.00000000000000001', '9007199254740990.1', '9007199254740990.9', '']) {
        await locator().fill(raw);
        await expect(locator()).toHaveValue(raw);
        await expect(locator()).toHaveAttribute('aria-invalid', 'true');
        await expect(apply).toBeDisabled();
        const pending = await download(page, 'Download draft');
        const savedRaw = field === 'State visit budget' ? pending.machine.stepBudgetInput
            : field === 'Initial value (JSON literal)' ? pending.machine.variables[0].initialInput
                : pending.machine.states[0].actions.find((action: {
                    kind: string
                }) => action.kind === 'draw').outcomes[0][field === 'Weight' ? 'weightInput' : 'payloadInput'];
        expect(savedRaw).toBe(raw);
        expect((await download(page, 'Back up project')).graph).toEqual(graph);
        await page.reload();
        await page.locator('.machine-explorer section').getByRole('button', {
            name: 'Choose once',
            exact: false
        }).click();
        await expect(locator()).toHaveValue(raw, {timeout: 70000});
        await expect(apply).toBeDisabled();
        expect((await download(page, 'Back up project')).graph).toEqual(graph);
    }
});

test('PR19 an explicit zero weight applies through the real compiler and stays zero after reload/export/reimport', async ({
                                                                                                                              page,
                                                                                                                              request
                                                                                                                          }) => {
    test.setTimeout(180000);
    const graph = machineProofGraph(weightedMachine());
    await open(page, graph);
    await page.getByLabel('Weight', {exact: true}).first().fill('0');
    await expect(page.getByRole('button', {name: 'Validate & apply', exact: true})).toBeEnabled();
    await page.getByRole('button', {name: 'Validate & apply', exact: true}).click();
    await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeEnabled({timeout: 70000});
    const saved = (await download(page, 'Back up project')).graph as ModelGraph;
    const rows = Object.values(saved.mechanics ?? {}).flatMap(value => value.nodes).filter(node => node.nodeType === 'draw').flatMap(node => node.drawWeights ?? []);
    expect(rows[0].weight).toBe(0);
    const response = await request.post('/api/play/round', {data: {config: saved, seed: 42, roundIndex: 0}});
    expect(response.ok()).toBe(true);
    expect((await response.json()).win).toBe(3);
    await page.reload();
    await page.locator('.machine-explorer section').getByRole('button', {name: 'Choose once', exact: false}).click();
    await expect(page.getByLabel('Weight', {exact: true}).first()).toHaveValue('0', {timeout: 70000});
    expect((await download(page, 'Back up project')).graph).toEqual(saved);
    await open(page, saved);
    expect((await download(page, 'Back up project')).graph).toEqual(saved);
});
