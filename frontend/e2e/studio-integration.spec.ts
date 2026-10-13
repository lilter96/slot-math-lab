import {readFile} from 'node:fs/promises';
import type {Route} from '@playwright/test';
import {expect, type Page, test, waitForWorkspace} from './fixtures';
import {graphStudiosCorpus} from '../unit/fixtures/graphStudiosCorpus';
import {computedLineBoundProof} from '../unit/fixtures/computedLineBoundProof';
import {reelBindings} from '../src/lib/authoring/reelBindings';
import {cellBindings} from '../src/lib/authoring/cellBindings';
import {weightBindings} from '../src/lib/authoring/weightBindings';
import {payBindings} from '../src/lib/authoring/payBindings';
import {lineBindings} from '../src/lib/authoring/lineBindings';
import {readable} from '../src/lib/authoring/studioPresentation';
import type {ModelGraph} from '../src/lib/modelProjection';

const corpus = graphStudiosCorpus().graphs;
const model = (caseId: string) => corpus.find(value => value.caseId === caseId)!.graph;

async function open(page: Page, graph: ModelGraph) {
    await page.goto('/build');
    await waitForWorkspace(page);
    await page.getByLabel('Import project file').setInputFiles({
        name: 'integrated-model.json',
        mimeType: 'application/json',
        buffer: Buffer.from(JSON.stringify(graph))
    });
    await expect(page.locator('.project-name')).toHaveText(String(graph.name));
    await page.getByRole('tab', {name: 'Studio', exact: true}).click();
}

async function backup(page: Page) {
    const pending = page.waitForEvent('download');
    await page.getByRole('button', {name: 'Back up project', exact: true}).click();
    return JSON.parse(await readFile((await (await pending).path())!, 'utf8')).graph as ModelGraph;
}

async function apply(page: Page) {
    const pending = page.waitForResponse(response => new URL(response.url()).pathname === '/api/validate' && response.status() !== 429, {timeout: 70000});
    await page.getByRole('button', {name: 'Validate & apply', exact: true}).click();
    const result = await (await pending).json();
    expect(result, JSON.stringify(result)).toMatchObject({isValid: true});
    await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeEnabled();
}

async function playAndRoundtrip(page: Page, graph: ModelGraph) {
    await page.getByRole('button', {name: 'Play test round', exact: true}).click();
    await expect(page.locator('.studio-round-workbench output')).toContainText('Seed 42');
    await page.getByRole('tab', {name: 'Build', exact: true}).click();
    await expect(page.locator('.project-name')).toHaveText(String(graph.name));
    await page.getByRole('tab', {name: 'Studio', exact: true}).click();
    expect(await backup(page)).toEqual(graph);
    await open(page, graph);
    expect(await backup(page)).toEqual(graph);
}

async function choose(page: Page, label: string) {
    await page.locator('.studio-source-explorer button').filter({hasText: label}).first().click();
}

for (const caseId of ['ancient-dragon/ordinary', 'woodland-whisper/ordinary', 'woodland-whisper/bought']) test(`Studio integrated ${caseId}: reel replacement/order, line payout and path commit together, then Graph/play/export/reimport retain the source`, async ({page}) => {
    const graph = model(caseId), reels = reelBindings(graph)[0],
        line = payBindings(graph).find(binding => binding.kind === 'line')!, row = line.rows[0],
        symbol = String((Number(reels.stops[0][0].symbol) + 1) % (graph.symbols as unknown[]).length);
    await open(page, graph);
    await page.getByLabel('Selected stop symbol').selectOption(symbol);
    await page.getByRole('button', {name: 'Move selected stop down', exact: true}).click();
    await page.getByRole('tab', {name: 'Paytables', exact: true}).click();
    await page.getByLabel(`line payout ${row.symbolId} count ${row.counts[0]}`, {exact: true}).fill(String(Number(row.values[0]) + 1));
    await page.getByRole('tab', {name: 'Paylines', exact: true}).click();
    const path = lineBindings(graph)[0].lines[0], changedRow = (path[0] + 1) % 3;
    await page.getByRole('button', {name: `Line 1 column 1 row ${changedRow + 1}`, exact: true}).click();
    await apply(page);
    const changed = await backup(page);
    expect(reelBindings(changed)[0].stops[0][1].symbol).toBe(symbol);
    expect(payBindings(changed).find(binding => binding.kind === 'line')!.rows[0].values[0]).toBe(String(Number(row.values[0]) + 1));
    expect(lineBindings(changed)[0].lines[0][0]).toBe(changedRow);
    await playAndRoundtrip(page, changed);
});
for (const caseId of ['x7-club/ordinary', 'x7-club/bought', 'nine-lives-tgslots/ordinary', 'nine-lives-tgslots/bought']) test(`Studio integrated ${caseId}: one board/landing distribution and its payout/prize edit preserve independent stages, payloads and entry price`, async ({page}) => {
    const graph = model(caseId), board = cellBindings(graph)[0],
        source = board?.cells[0].source ?? weightBindings(graph).find(binding => binding.source.node.stateWriteKey === 'landed1')!,
        outcome = source.rows[0];
    await open(page, graph);
    await choose(page, readable(board?.scope ?? source.source.scope!));
    if (!board) await page.getByLabel('Select outcome draw').selectOption(source.key);
    await page.getByLabel(`Weight ${outcome.outcomeId} in ${source.source.id}`, {exact: true}).fill(String(outcome.weight + 1));
    await page.getByRole('tab', {name: 'Paytables', exact: true}).click();
    const payout = payBindings(graph).find(binding => binding.kind === (caseId.startsWith('x7') ? 'prize' : 'cluster'))!;
    await choose(page, readable(payout.tableId));
    if (payout.kind === 'prize') await page.getByLabel('Prize 1 value', {exact: true}).fill(String(payout.maximum + 1)); else await page.getByLabel(`cluster payout ${payout.rows[0].symbolId} count ${payout.rows[0].counts[0]}`, {exact: true}).fill(String(Math.max(0, Number(payout.rows[0].values[0]) - 1)));
    await apply(page);
    const changed = await backup(page);
    expect((changed.initialState as Record<string, unknown>).roundCost).toBe((graph.initialState as Record<string, unknown>).roundCost);
    for (const other of cellBindings(graph).filter(value => value.key !== board?.key)) expect(changed.mechanics![other.scope]).toEqual(graph.mechanics![other.scope]);
    const edited = weightBindings(changed).find(value => value.key === source.key)!;
    expect(edited.rows[0].weight).toBe(outcome.weight + 1);
    expect(edited.rows.every(row => row.value === 0)).toBe(true);
    expect(changed.nodes).toEqual(graph.nodes);
    await playAndRoundtrip(page, changed);
});
for (const {caseId} of corpus.filter(value => value.caseId.startsWith('le-militare/'))) test(`Studio integrated ${caseId}: paid/free strips and multiplier weights stay separate and held/weighted-cell state survives real execution`, async ({page}) => {
    const graph = model(caseId), reels = reelBindings(graph),
        multiplier = weightBindings(graph).find(binding => binding.source.node.stateWriteKey === 'drawnMultiplier')!;
    await open(page, graph);
    for (const source of reels) {
        await choose(page, readable(source.source.label));
        await page.getByRole('button', {name: 'Move selected stop down', exact: true}).click();
    }
    await choose(page, String(multiplier.source.node.label));
    await page.getByLabel(`Weight ${multiplier.rows[0].outcomeId} in ${multiplier.source.id}`, {exact: true}).fill(String(multiplier.rows[0].weight + 1));
    await apply(page);
    const changed = await backup(page), stripKeys = new Set(reels.flatMap(binding => binding.source.stripKeys));
    for (const [key, value] of Object.entries(graph.initialState as Record<string, unknown>)) if (!stripKeys.has(key)) expect((changed.initialState as Record<string, unknown>)[key], key).toEqual(value);
    expect(weightBindings(changed).find(binding => binding.key === multiplier.key)!.rows[0].weight).toBe(multiplier.rows[0].weight + 1);
    expect(changed.nodes).toEqual(graph.nodes);
    expect(changed.paytables).toEqual(graph.paytables);
    await playAndRoundtrip(page, changed);
});

async function payoutDraft(page: Page) {
    const graph = computedLineBoundProof() as ModelGraph;
    await open(page, graph);
    await page.getByRole('tab', {name: 'Paytables', exact: true}).click();
    await page.getByLabel('line payout 1 count 2', {exact: true}).fill('6');
    return graph;
}

test('Studio recovery: rejected compiler validation retains editable inputs; cancelling a held validation ignores its late success', async ({page}) => {
    const graph = await payoutDraft(page);
    await page.route('**/api/validate', route => route.fulfill({
        json: {
            isValid: false,
            errors: [{message: 'Authored compiler rejection'}]
        }
    }));
    await page.getByRole('button', {name: 'Validate & apply', exact: true}).click();
    await expect(page.getByRole('alert')).toContainText('Authored compiler rejection');
    expect(await backup(page)).toEqual(graph);
    await expect(page.getByLabel('line payout 1 count 2', {exact: true})).toHaveValue('6');
    await page.unroute('**/api/validate');
    let held: Route | null = null;
    await page.route('**/api/validate', route => {
        held = route;
    });
    await page.getByRole('button', {name: 'Validate & apply', exact: true}).click();
    await expect(page.getByRole('button', {name: 'Cancel', exact: true})).toBeVisible();
    await page.getByRole('button', {name: 'Cancel', exact: true}).click();
    await expect(page.getByRole('alert')).toContainText('Cancelled');
    expect(held).not.toBeNull();
    await held!.fulfill({json: {isValid: true}}).catch(() => {
    });
    expect(await backup(page)).toEqual(graph);
    await expect(page.getByLabel('line payout 1 count 2', {exact: true})).toHaveValue('6');
});
test('Studio recovery: a late validation cannot replace a newly imported private project', async ({page}) => {
    await payoutDraft(page);
    let held: Route | null = null;
    await page.route('**/api/validate', route => {
        held = route;
    });
    await page.getByRole('button', {name: 'Validate & apply', exact: true}).click();
    await expect(page.getByRole('button', {name: 'Cancel', exact: true})).toBeVisible();
    const other = {
        ...computedLineBoundProof(),
        id: 'another-private-model',
        name: 'Another private project'
    } as ModelGraph;
    await open(page, other);
    expect(held).not.toBeNull();
    await held!.fulfill({json: {isValid: true}}).catch(() => {
    });
    expect(await backup(page)).toEqual(other);
    await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
    await expect(page.getByRole('button', {name: 'Validate & apply', exact: true})).toBeDisabled();
});
