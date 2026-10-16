import {expect, test, waitForWorkspace} from './fixtures';
import {readFile} from 'node:fs/promises';
import path from 'node:path';

const config = {
    schemaVersion: '1.0.0',
    name: 'Keyboard connections',
    initialState: {win: 1},
    expressions: {choose: {exprType: 'constant', kind: 'Boolean', value: 'true'}},
    nodes: [
        {
            id: 'gate',
            nodeType: 'branch',
            label: 'Choose path',
            conditionId: 'choose',
            inputs: {state: {name: 'state', type: 'State'}},
            outputs: {true: {name: 'true', type: 'State'}, false: {name: 'false', type: 'State'}}
        },
        {
            id: 'sink',
            nodeType: 'metricsSink',
            label: 'Payout',
            winCap: 10,
            winStateKey: 'win',
            inputs: {in: {name: 'in', type: 'State'}}
        },
    ],
    edges: []
};
const link = (graph: unknown) => '/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(graph))).toString('base64'));

test('Keyboard connection form creates independent branch ports, rejects duplicates and restores focus', async ({
                                                                                                                    page,
                                                                                                                    request
                                                                                                                }) => {
    await page.goto(link(config));
    const trigger = page.getByRole('button', {name: 'Connect graph ports', exact: true});
    await trigger.focus();
    await page.keyboard.press('Enter');
    const dialog = page.getByRole('dialog', {name: 'Connect graph nodes'});
    await expect(dialog).toBeVisible();
    expect(await dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
    const connect = dialog.getByRole('button', {name: 'Connect ports', exact: true});
    await connect.focus();
    await page.keyboard.press('Enter');
    await expect(dialog.getByRole('status')).toContainText('Connection added');
    await expect(connect).toBeDisabled();
    await dialog.getByLabel('Output port', {exact: true}).selectOption('false');
    await connect.focus();
    await page.keyboard.press('Enter');
    await page.keyboard.press('Escape');
    await expect(dialog).toHaveCount(0);
    await expect(trigger).toBeFocused();
    await expect(page.locator('.react-flow__edge')).toHaveCount(2);
    const download = page.waitForEvent('download');
    await page.getByRole('button', {name: 'Export project', exact: true}).click();
    const saved = JSON.parse(await readFile((await (await download).path())!, 'utf8'));
    expect(new Set(saved.edges.map((edge: { id: string }) => edge.id)).size).toBe(2);
    expect(saved.edges.map((edge: { sourcePort: string }) => edge.sourcePort).sort()).toEqual(['false', 'true']);
    expect(saved.edges.every((edge: { targetPort: string }) => edge.targetPort === 'in')).toBe(true);
    const checked = await request.post('/api/validate', {data: {config: saved}});
    expect(checked.ok()).toBe(true);
    expect((await checked.json()).isValid).toBe(true);
});

test('Both connection paths use actual typed ports and incompatible form choices cannot mutate the graph', async ({page}) => {
    const typed = {
        ...config,
        nodes: [...config.nodes, {
            id: 'wrong',
            nodeType: 'metricsSink',
            winCap: 10,
            inputs: {in: {name: 'in', type: 'Wins'}}
        }]
    };
    await page.goto(link(typed));
    await page.getByRole('button', {name: 'Connect graph ports', exact: true}).click();
    const dialog = page.getByRole('dialog', {name: 'Connect graph nodes'});
    await dialog.getByLabel('Target node', {exact: true}).selectOption('wrong');
    await expect(dialog.getByRole('alert')).toContainText('State');
    await expect(dialog.getByRole('button', {name: 'Connect ports', exact: true})).toBeDisabled();
    await page.keyboard.press('Escape');
    await expect(page.locator('.react-flow__edge')).toHaveCount(0);
    const output = page.locator('.react-flow__node[data-id="gate"] .react-flow__handle[data-handleid="true"]');
    const input = page.locator('.react-flow__node[data-id="sink"] .react-flow__handle[data-handleid="in"]');
    const from = (await output.boundingBox())!, to = (await input.boundingBox())!;
    await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
    await page.mouse.down();
    await page.mouse.move(to.x + to.width / 2, to.y + to.height / 2, {steps: 12});
    await page.mouse.up();
    await expect(page.locator('.react-flow__edge')).toHaveCount(1);
});

test('Legacy handles expose their actual canonical ports and mobile graph editing remains explicitly limited', async ({page}) => {
    const legacy = {
        name: 'Legacy ports', nodes: [
            {
                id: 'draw',
                type: 'draw',
                position: {x: 80, y: 100},
                data: {nodeType: 'draw', label: 'Draw', drawWeights: [{outcomeId: 'one', weight: 1, value: 1}]}
            },
            {id: 'sink', type: 'sink', position: {x: 400, y: 100}, data: {nodeType: 'sink', label: 'Sink', winCap: 10}},
        ], edges: [], tables: {schemaVersion: '1.0.0'}, mechanics: []
    };
    await page.goto(link(legacy));
    await expect(page.locator('.react-flow__node[data-id="draw"] .react-flow__handle[data-handleid="out"]')).toHaveCount(1);
    await expect(page.locator('.react-flow__node[data-id="draw"] .react-flow__handle.target[data-handleid="in"]')).toHaveCount(1);
    await page.getByRole('button', {name: 'Connect graph ports', exact: true}).click();
    await page.getByRole('dialog').getByRole('button', {name: 'Connect ports', exact: true}).click();
    await page.keyboard.press('Escape');
    await expect(page.locator('.react-flow__edge')).toHaveCount(1);
    await page.setViewportSize({width: 390, height: 844});
    await expect(page.getByRole('button', {name: 'Connect graph ports', exact: true})).toBeDisabled();
    await expect(page.getByRole('status').filter({hasText: 'read only'})).toBeVisible();
});

test('A delayed JSON import cannot replace a newer independent project with identical node IDs', async ({page}) => {
    await page.addInitScript(() => {
        const original = File.prototype.text;
        const host = window as unknown as { importStarted: boolean; importFinished: boolean; releaseImport(): void };
        const gate = new Promise<void>(resolve => {
            host.releaseImport = resolve;
        });
        File.prototype.text = async function () {
            const content = await original.call(this);
            if (this.name === 'slow.json') {
                host.importStarted = true;
                await gate;
                host.importFinished = true;
            }
            return content;
        };
    });
    await page.goto(link(config));
    const importer = page.getByLabel('Import project file', {exact: true});
    await importer.setInputFiles({
        name: 'slow.json',
        mimeType: 'application/json',
        buffer: Buffer.from(JSON.stringify({...config, name: 'Obsolete import'}))
    });
    await expect.poll(() => page.evaluate(() => (window as unknown as {
        importStarted: boolean
    }).importStarted)).toBe(true);
    await importer.setInputFiles({
        name: 'new.json',
        mimeType: 'application/json',
        buffer: Buffer.from(JSON.stringify({...config, name: 'New independent project'}))
    });
    await expect(page.getByRole('link', {
        name: 'Choose project',
        exact: true
    })).toContainText('New independent project');
    await page.evaluate(() => (window as unknown as { releaseImport(): void }).releaseImport());
    await expect.poll(() => page.evaluate(() => (window as unknown as {
        importFinished: boolean
    }).importFinished)).toBe(true);
    await expect(page.getByRole('link', {
        name: 'Choose project',
        exact: true
    })).toContainText('New independent project');
});

test('Connection dialog closes when another project reuses the same authored IDs', async ({page}) => {
    await page.goto(link(config));
    await page.getByRole('button', {name: 'Connect graph ports', exact: true}).click();
    await expect(page.getByRole('dialog', {name: 'Connect graph nodes'})).toBeVisible();
    await page.getByLabel('Import project file', {exact: true}).setInputFiles({
        name: 'independent.json',
        mimeType: 'application/json',
        buffer: Buffer.from(JSON.stringify({...config, name: 'Independent same IDs'}))
    });
    await expect(page.getByRole('link', {name: 'Choose project', exact: true})).toContainText('Independent same IDs');
    await expect(page.getByRole('dialog', {name: 'Connect graph nodes'})).toHaveCount(0);
    await expect(page.locator('.react-flow__edge')).toHaveCount(0);
    await page.getByRole('button', {name: 'Connect graph ports', exact: true}).click();
    await page.getByRole('dialog').getByRole('button', {name: 'Connect ports', exact: true}).click();
    await expect(page.locator('.react-flow__edge')).toHaveCount(1);
});

test('Connection authoring fits requested viewport sizes with explicit mobile limits', async ({page}) => {
    for (const [width, height] of [[1440, 900], [1280, 800], [1024, 768], [390, 844]]) {
        await page.setViewportSize({width, height});
        await page.goto(link(config));
        await waitForWorkspace(page);
        const trigger = page.getByRole('button', {name: 'Connect graph ports', exact: true});
        if (width > 390) {
            await trigger.click();
            await expect(page.getByRole('dialog', {name: 'Connect graph nodes'})).toBeVisible();
        } else await expect(trigger).toBeDisabled();
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
        await page.screenshot({
            path: path.resolve('../docs/design/qa', `keyboard-connection-${width}.png`),
            fullPage: true
        });
    }
});
