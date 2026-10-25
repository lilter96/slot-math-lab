import {type APIRequestContext, expect, test} from './fixtures';
import {Flow, gameGraph, Mechanics} from '../src/games/shared/graph/flow';
import {reelState, spinReels} from '../src/games/shared/graph/mechanics/reels';
import {and, at, div, eq, num, state, str, when} from '../src/games/shared/graph/expr';
import {changeReels, reelSources} from '../src/lib/reelCommands';
import {changeBoundWeights, weightBindings} from '../src/lib/authoring/weightBindings';
import {changeBoundReels, reelBindings} from '../src/lib/authoring/reelBindings';
import {changeBoundLines, lineBindings} from '../src/lib/authoring/lineBindings';
import {linePayTable, paylines, paylineState} from '../src/games/shared/graph/mechanics/paylines';
import {scatterPay, scatterPayTable} from '../src/games/shared/graph/mechanics/scatter';
import {changeBoundPayout, payBindings} from '../src/lib/authoring/payBindings';
import {cellBindings, changeBoundCells} from '../src/lib/authoring/cellBindings';
import {cellState, spinCells} from '../src/games/shared/graph/mechanics/cells';
import {useAppStore} from '../src/store';
import {loadProject, peekRootProject} from '../src/lib/projectFiles';
import {commitGraphEdit, prepareGraphEdit, undoGraphEdit} from '../src/lib/authoring/transactions';
import type {ModelGraph} from '../src/lib/modelProjection';

async function exact(api: APIRequestContext, graph: ModelGraph, rtp: number) {
    const validation = await api.post('/api/validate', {data: {config: graph}});
    expect(validation.ok()).toBe(true);
    expect((await validation.json()).isValid).toBe(true);
    const response = await api.post('/api/evaluate/light', {
        data: {
            config: graph,
            maxBranches: 10000,
            sampleSize: 5000
        }
    });
    expect(response.ok()).toBe(true);
    const result = await response.json();
    expect(result.strategy).toBe('Exact');
    expect(result.rtp).toBeCloseTo(rtp, 12);
}

test('Foundation: ordered physical-reel edit changes the real window payout and no-op keeps seeded state', async ({request}) => {
    const spec = {name: 'ordered-custom-source', stripKey: 'customStrip', rows: 2, strips: [['A', 'B', 'C'], ['C']]};
    const mechanics = new Mechanics();
    const root = new Flow().use('spin', 'Custom ordered window', mechanics.add(spinReels(spec)))
        .write('award', 'A then B on reel one pays one', 'coins', when(and(eq(at('board', num(0)), str('A')), eq(at('board', num(1)), str('B'))), num(1), num(0)))
        .sink('sink', 'Round payout', 'coins', 1);
    const graph = gameGraph({
        id: 'custom-reel-proof',
        name: 'Ordered reel proof',
        description: 'Three physical stops have windows AB, BC, CA. Only AB pays.',
        root,
        mechanics,
        initialState: {coins: 0, ...reelState(spec)},
        tables: {
            symbols: ['A', 'B', 'C'].map(id => ({id, name: id, kind: 'Standard'})),
            boardConfig: {rows: 2, columns: 2},
            opaque: {retained: true}
        }
    }) as ModelGraph;
    const before = JSON.stringify(graph), source = reelSources(graph)[0];
    expect(source).toBeDefined();
    const noOp = changeReels(graph, source.key, source.strips);
    expect(noOp).toEqual(graph);
    await exact(request, graph, 1 / 3);
    const changed = changeReels(graph, source.key, [['A', 'C', 'B'], ['C']]);
    await exact(request, changed, 0);
    expect((changed.initialState as Record<string, unknown>).customStrip0).toEqual(['A', 'C', 'B', 'A']);
    expect(changed.opaque).toEqual({retained: true});
    expect(JSON.stringify(graph)).toBe(before);
    for (const seed of [7, 42]) {
        const responses = [];
        for (const config of [graph, noOp]) {
            const response = await request.post('/api/play/round', {data: {config, seed, roundIndex: 0}});
            expect(response.ok()).toBe(true);
            responses.push(await response.json());
        }
        expect(responses[1]).toEqual(responses[0]);
    }
});
test('Foundation: independent outcome weights change real exact probability without altering payload or unrelated source', async ({request}) => {
    const graph: ModelGraph = {
        schemaVersion: '1.0.0',
        id: 'custom-weight-proof',
        name: 'Independent draw proof',
        opaque: {keep: true},
        nodes: [
            {
                id: 'draw',
                nodeType: 'draw',
                drawWeights: [{outcomeId: '001', weight: 3, value: 1, custom: 'keep'}, {
                    outcomeId: 'zero',
                    weight: 1,
                    value: 0
                }],
                outputs: {out: {name: 'out', type: 'Wins'}}
            },
            {id: 'sink', nodeType: 'metricsSink', winCap: 10, inputs: {in: {name: 'in', type: 'Wins'}}},
        ],
        edges: [{id: 'edge', sourceNodeId: 'draw', sourcePort: 'out', targetNodeId: 'sink', targetPort: 'in'}]
    };
    const before = JSON.stringify(graph), source = weightBindings(graph)[0];
    useAppStore.setState({projectId: 'api-proof-project', graphTrail: []});
    loadProject(graph as Record<string, unknown>);
    const edit = prepareGraphEdit('Change one draw', current => changeBoundWeights(current, source, new Map([['001', '1'], ['zero', '3']])));
    const applied = await commitGraphEdit(edit, undefined, async config => {
        const response = await request.post('/api/validate', {data: {config}});
        expect(response.ok()).toBe(true);
        return response.json();
    });
    const changed = peekRootProject() as ModelGraph;
    expect(changed).toEqual(edit.candidate);
    await exact(request, graph, 0.75);
    await exact(request, changed, 0.25);
    expect(changed.nodes![0].drawWeights).toEqual([{
        outcomeId: '001',
        weight: 1,
        value: 1,
        custom: 'keep'
    }, {outcomeId: 'zero', weight: 3, value: 0}]);
    expect(changed.edges).toEqual(graph.edges);
    expect(changed.opaque).toEqual(graph.opaque);
    expect(JSON.stringify(graph)).toBe(before);
    undoGraphEdit(applied);
    expect(peekRootProject()).toEqual(graph);
    await exact(request, peekRootProject() as ModelGraph, 0.75);
});

test('Foundation: physical binding preserves weighted stops through real validation, apply and undo', async ({request}) => {
    const spec = {name: 'private-window', stripKey: 'privateStrip', rows: 2, strips: [['A', 'B', 'C']]};
    const mechanics = new Mechanics(), root = new Flow().use('spin', 'Private window', mechanics.add(spinReels(spec)))
        .write('award', 'AB pays one', 'coins', when(and(eq(at('board', num(0)), str('A')), eq(at('board', num(1)), str('B'))), num(1), num(0))).sink('sink', 'Payout', 'coins', 1);
    const graph = gameGraph({
        id: 'weighted-reel-proof',
        name: 'Weighted ordered source',
        description: 'AB at one of three weighted stops pays one.',
        root,
        mechanics,
        initialState: {coins: 0, ...reelState(spec)},
        tables: {
            symbols: ['A', 'B', 'C'].map(id => ({id, name: id, kind: 'Standard'})),
            boardConfig: {rows: 2, columns: 1}
        }
    }) as ModelGraph;
    const draw = graph.mechanics!['private-window'].nodes.find(node => node.nodeType === 'draw')!;
    draw.drawWeights[0].weight = 2;
    draw.drawWeights[0].opaque = {keep: true};
    const binding = reelBindings(graph)[0], stops = structuredClone(binding.stops);
    [stops[0][0], stops[0][1]] = [stops[0][1], stops[0][0]];
    useAppStore.setState({projectId: 'weighted-reel-project', graphTrail: []});
    loadProject(graph as Record<string, unknown>);
    const edit = prepareGraphEdit('Reorder retained weighted stops', current => changeBoundReels(current, binding, stops));
    const applied = await commitGraphEdit(edit, undefined, async config => {
        const response = await request.post('/api/validate', {data: {config}});
        expect(response.ok()).toBe(true);
        return response.json();
    });
    await exact(request, graph, 0.5);
    await exact(request, peekRootProject() as ModelGraph, 0);
    expect((peekRootProject() as ModelGraph).mechanics!['private-window'].nodes.find(node => node.nodeType === 'draw')!.drawWeights[1]).toMatchObject({
        outcomeId: '1',
        weight: 2,
        value: 0,
        opaque: {keep: true}
    });
    undoGraphEdit(applied);
    expect(peekRootProject()).toEqual(graph);
    await exact(request, peekRootProject() as ModelGraph, 0.5);
});

test('Foundation: cell outcome identity drives board payout while numeric Draw payload remains zero', async ({request}) => {
    const root = new Flow().draw('cell', 'Independent symbol cell', 'symbol', [['001', 3], ['B', 1]])
        .write('award', 'Symbol 001 pays one', 'coins', when(eq({
            exprType: 'fieldAccess',
            target: 'state',
            path: ['symbol']
        }, str('001')), num(1), num(0))).sink('sink', 'Payout', 'coins', 1);
    const graph = gameGraph({
        id: 'cell-identity-proof',
        name: 'Cell source proof',
        description: 'An outcome ID, rather than its numeric payload, selects the symbol payout.',
        root,
        mechanics: new Mechanics(),
        initialState: {symbol: '', coins: 0},
        tables: {
            symbols: [{id: '001', name: 'A', kind: 'Standard'}, {id: 'B', name: 'B', kind: 'Standard'}],
            boardConfig: {rows: 1, columns: 1}
        }
    }) as ModelGraph;
    const binding = weightBindings(graph)[0];
    expect(binding.rows.every(row => row.value === 0)).toBe(true);
    useAppStore.setState({projectId: 'cell-proof-project', graphTrail: []});
    loadProject(graph as Record<string, unknown>);
    const edit = prepareGraphEdit('Change one cell', current => changeBoundWeights(current, binding, new Map([['001', '1'], ['B', '3']])));
    await commitGraphEdit(edit, undefined, async config => {
        const response = await request.post('/api/validate', {data: {config}});
        expect(response.ok()).toBe(true);
        return response.json();
    });
    await exact(request, graph, 0.75);
    await exact(request, peekRootProject() as ModelGraph, 0.25);
    expect((peekRootProject() as ModelGraph).nodes![0].drawWeights.map((row: {
        value: number
    }) => row.value)).toEqual([0, 0]);
});

test('Foundation: payline board path edits the actual executable consumer and changes exact payout', async ({request}) => {
    const mechanics = new Mechanics(),
        root = new Flow().use('lines', 'Read the configured line', mechanics.add(paylines(mechanics, [[0, 0]], 2))).sink('sink', 'Line win', 'lineCoins', 10);
    const graph = gameGraph({
        id: 'custom-line-proof',
        name: 'Custom line proof',
        description: 'A fixed board pays one only on its top row.',
        root,
        mechanics,
        initialState: {
            board: ['1', '2', '1', '2'], ...paylineState(0, linePayTable({
                symbolCount: 3,
                wild: 0,
                pays: {1: {2: 1}},
                reels: 2
            }))
        },
        tables: {
            boardConfig: {rows: 2, columns: 2},
            paylineSets: [{id: 'my-line-set', paylines: [{positions: [0, 0], opaque: {keep: true}}]}]
        }
    }) as ModelGraph;
    const binding = lineBindings(graph)[0];
    expect(binding).toBeDefined();
    useAppStore.setState({projectId: 'line-proof-project', graphTrail: []});
    loadProject(graph as Record<string, unknown>);
    const edit = prepareGraphEdit('Move the first line cell', current => changeBoundLines(current, binding, [[1, 0]]));
    const applied = await commitGraphEdit(edit, undefined, async config => {
        const response = await request.post('/api/validate', {data: {config}});
        expect(response.ok()).toBe(true);
        return response.json();
    });
    await exact(request, graph, 1);
    await exact(request, peekRootProject() as ModelGraph, 0);
    expect((peekRootProject() as ModelGraph).paylineSets).toEqual([{
        id: 'my-line-set',
        paylines: [{positions: [1, 0], opaque: {keep: true}}]
    }]);
    undoGraphEdit(applied);
    expect(peekRootProject()).toEqual(graph);
});

test('Foundation: line payout edit synchronizes a rational precision declaration and the real exact win', async ({request}) => {
    const mechanics = new Mechanics(),
        root = new Flow().use('lines', 'Pay the configured line', mechanics.add(paylines(mechanics, [[0, 0]], 1)))
            .write('credits', 'Win per round price', 'winCredits', div(state('lineCoins'), state('roundCost'))).sink('sink', 'Round win', 'winCredits', 10);
    const graph = gameGraph({
        id: 'custom-pay-proof',
        name: 'Custom rational payout',
        description: 'A fixed line pays five credits of a six-credit price.',
        root,
        mechanics,
        initialState: {
            board: ['1', '1'],
            totalBet: 6,
            roundCost: 6,
            winCredits: 0, ...paylineState(0, linePayTable({symbolCount: 3, wild: 0, pays: {1: {2: 5}}, reels: 2}))
        },
        tables: {
            boardConfig: {rows: 1, columns: 2},
            symbols: [{id: '0', name: 'Wild', kind: 'Wild'}, {id: '1', name: 'A', kind: 'Standard'}, {
                id: '2',
                name: 'B',
                kind: 'Standard'
            }],
            paytables: [{
                id: 'private-payout',
                entries: [{symbolId: '1', counts: [2], payouts: ['5/6'], opaque: {keep: true}}]
            }]
        }
    }) as ModelGraph;
    const binding = payBindings(graph)[0];
    expect(binding).toBeDefined();
    useAppStore.setState({projectId: 'pay-proof-project', graphTrail: []});
    loadProject(graph as Record<string, unknown>);
    const edit = prepareGraphEdit('Change two-match payout', current => changeBoundPayout(current, binding, '1', 2, '3'));
    const applied = await commitGraphEdit(edit, undefined, async config => {
        const response = await request.post('/api/validate', {data: {config}});
        expect(response.ok()).toBe(true);
        return response.json();
    });
    await exact(request, graph, 5 / 6);
    await exact(request, peekRootProject() as ModelGraph, 0.5);
    expect((peekRootProject() as ModelGraph).paytables).toEqual([{
        id: 'private-payout',
        entries: [{symbolId: '1', counts: [2], payouts: ['1/2'], opaque: {keep: true}}]
    }]);
    expect((peekRootProject() as ModelGraph).mechanics).toEqual(graph.mechanics);
    undoGraphEdit(applied);
    expect(peekRootProject()).toEqual(graph);
});

test('Foundation: scatter edit keeps total-bet units distinct from a bought round price', async ({request}) => {
    const mechanics = new Mechanics(),
        root = new Flow().use('scatters', 'Pay scatters', mechanics.add(scatterPay()), {symbol: 1})
            .write('credits', 'Win per bought price', 'winCredits', div(state('scatterCoins'), state('roundCost'))).sink('sink', 'Round win', 'winCredits', 10);
    const graph = gameGraph({
        id: 'custom-scatter-proof',
        name: 'Scatter unit proof',
        description: 'Three scatters pay two bets; the round costs six bets.',
        root,
        mechanics,
        initialState: {
            board: ['1', '1', '1'],
            totalBet: 5,
            roundCost: 30,
            winCredits: 0,
            scatterCount: 0,
            scatterCoins: 0,
            scatterPays: scatterPayTable({3: 2}, 3)
        },
        tables: {
            boardConfig: {rows: 1, columns: 3},
            symbols: [{id: '0', name: 'A', kind: 'Standard'}, {id: '1', name: 'Scatter', kind: 'Scatter'}],
            paytables: [{id: 'private-scatter-payout', entries: [{symbolId: '1', counts: [3], payouts: ['1/3']}]}]
        }
    }) as ModelGraph;
    const binding = payBindings(graph)[0];
    expect(binding.unit).toBe('total-bet multiples');
    useAppStore.setState({projectId: 'scatter-proof-project', graphTrail: []});
    loadProject(graph as Record<string, unknown>);
    const edit = prepareGraphEdit('Reduce scatter award', current => changeBoundPayout(current, binding, '1', 3, '1'));
    await commitGraphEdit(edit, undefined, async config => {
        const response = await request.post('/api/validate', {data: {config}});
        expect(response.ok()).toBe(true);
        return response.json();
    });
    await exact(request, graph, 1 / 3);
    await exact(request, peekRootProject() as ModelGraph, 1 / 6);
    expect((peekRootProject() as ModelGraph).paytables).toEqual([{
        id: 'private-scatter-payout',
        entries: [{symbolId: '1', counts: [3], payouts: ['1/6']}]
    }]);
});

test('Foundation: editing one independent board cell changes only its draw law through the real compiler', async ({request}) => {
    const mechanics = new Mechanics(),
        root = new Flow().use('cells', 'Independent cell board', mechanics.add(spinCells({
            name: 'private-cells',
            cells: 2,
            outcomes: [['1', 3], ['2', 1]]
        })))
            .use('lines', 'AA pays one', mechanics.add(paylines(mechanics, [[0, 0]], 1))).sink('sink', 'Line win', 'lineCoins', 1);
    const graph = gameGraph({
        id: 'custom-cell-board-proof',
        name: 'Independent cell board proof',
        description: 'Two cells independently show A with probability 3/4. AA pays one.',
        root,
        mechanics,
        initialState: {
            ...cellState({cells: 2}), ...paylineState(0, linePayTable({
                symbolCount: 3,
                wild: 0,
                pays: {1: {2: 1}},
                reels: 2
            }))
        },
        tables: {boardConfig: {rows: 1, columns: 2}}
    }) as ModelGraph;
    const binding = cellBindings(graph)[0], first = binding.cells[0].source, second = binding.cells[1].source;
    useAppStore.setState({projectId: 'cell-board-proof-project', graphTrail: []});
    loadProject(graph as Record<string, unknown>);
    const edit = prepareGraphEdit('Change the first board cell', current => changeBoundCells(current, binding, new Map([[first.key, new Map([['1', '1'], ['2', '3']])]])));
    const applied = await commitGraphEdit(edit, undefined, async config => {
        const response = await request.post('/api/validate', {data: {config}});
        expect(response.ok()).toBe(true);
        return response.json();
    });
    await exact(request, graph, 9 / 16);
    await exact(request, peekRootProject() as ModelGraph, 3 / 16);
    expect(weightBindings(peekRootProject() as ModelGraph).find(source => source.key === second.key)!.rows).toEqual(second.rows);
    undoGraphEdit(applied);
    expect(peekRootProject()).toEqual(graph);
});
