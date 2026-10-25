import {expect, test} from '@playwright/test';
import {graphStudiosCorpus} from './fixtures/graphStudiosCorpus';
import {createNineLivesGraph} from '../src/games/nine-lives-tgslots/graph';
import {createLeMilitareGraph} from '../src/games/le-militare/graph';
import {createNineLivesGraph as reference} from '../src/games/nine-lives/graph';
import {changeBoundPayouts, payBindings} from '../src/lib/authoring/payBindings';
import {normalizedCredits} from '../src/lib/authoring/exactAmounts';
import type {ModelGraph} from '../src/lib/modelProjection';
import {Mechanics} from '../src/games/shared/graph/flow';
import {clusterPays, type ClusterBoard} from '../src/games/shared/graph/mechanics/clusters';
import {exactInteger} from '../src/lib/authoring/exactAmounts';

test('Every TGSlots entry, all eighteen Le Militare variants and the separate reference expose executable cluster bindings without no-op rewriting', () => {
    const corpus = graphStudiosCorpus().graphs.filter(entry => /^(nine-lives-tgslots|le-militare|nine-lives\/reference)/.test(entry.caseId));
    expect(corpus).toHaveLength(21);
    for (const {caseId, graph} of corpus) {
        const binding = payBindings(graph).find(value => value.kind === 'cluster');
        expect(binding, caseId).toBeDefined();
        const row = binding!.rows[0];
        expect(changeBoundPayouts(graph, binding!, [{
            symbolId: row.symbolId,
            count: row.counts[0],
            value: row.values[0]
        }]), caseId).toEqual(graph);
    }
});
for (const [label, create] of [['TGSlots', createNineLivesGraph], ['weighted Le Militare', createLeMilitareGraph], ['reference', reference]] as const) {
    test(`${label}: exact payout edits synchronize the real lookup and precision while preserving unrelated control, rule caps and input`, () => {
        const graph = create() as ModelGraph, before = structuredClone(graph),
            binding = payBindings(graph).find(value => value.kind === 'cluster')!, row = binding.rows[0],
            value = Number(row.values[0]) + 1;
        const next = changeBoundPayouts(graph, binding, [{
            symbolId: row.symbolId,
            count: row.counts[0],
            value: String(value)
        }]);
        expect(graph).toEqual(before);
        expect(next.nodes).toEqual(graph.nodes);
        expect(next.edges).toEqual(graph.edges);
        const state = next.initialState as Record<string, unknown>;
        if (label === 'reference') expect((state.clusterPaytable as string[])[0]).toBe(String(value));
        else expect((state.clusterPays as number[])[Number(row.symbolId) * (binding.columns + 1) + row.counts[0]]).toBe(value);
        const table = (next.paytables as {
            id: string;
            entries: { payouts: string[] }[]
        }[]).find(value => value.id === binding.tableId)!;
        expect(table.entries[0].payouts[0]).toBe(normalizedCredits(value, binding.roundCost));
        expect(state.capCoins).toBe((graph.initialState as typeof state).capCoins);
        expect(payBindings(next).find(value => value.kind === 'cluster')!.rows[0].values[0]).toBe(String(value));
    });
}
test('Minimum crosses five and one in both directions: pruning, isolated-cell handling and shared callers reconcile without replacing the game', () => {
    let graph = createNineLivesGraph() as ModelGraph;
    graph.nodes![0].opaque = {retained: true};
    const original = structuredClone(graph);
    let binding = payBindings(graph).find(value => value.kind === 'cluster')!;
    const row = binding.rows[0];
    graph = changeBoundPayouts(graph, binding, [{symbolId: row.symbolId, count: 4, value: '1'}]);
    expect((graph.initialState as Record<string, unknown>).clusterMinSize).toBe(4);
    expect(graph.nodes).toEqual(original.nodes);
    binding = payBindings(graph).find(value => value.kind === 'cluster')!;
    graph = changeBoundPayouts(graph, binding, [{symbolId: row.symbolId, count: 1, value: '2'}]);
    expect((graph.initialState as Record<string, unknown>).clusterMinSize).toBe(1);
    binding = payBindings(graph).find(value => value.kind === 'cluster')!;
    graph = changeBoundPayouts(graph, binding, [{
        symbolId: row.symbolId,
        count: 1,
        value: '',
        remove: true
    }, {symbolId: row.symbolId, count: 4, value: '', remove: true}]);
    expect((graph.initialState as Record<string, unknown>).clusterMinSize).toBe(5);
    expect(payBindings(graph).some(value => value.kind === 'cluster')).toBe(true);
    expect(graph.nodes).toEqual(original.nodes);
});
test('Runtime overrides, extra lookup consumers, mismatched metadata and stale revisions reject before mutation', () => {
    const graph = createNineLivesGraph() as ModelGraph,
        binding = payBindings(graph).find(value => value.kind === 'cluster')!;
    for (const key of ['clusterPays', 'clusterMinSize']) {
        const altered = structuredClone(graph);
        altered.nodes!.push({id: 'override', nodeType: 'putState', stateKey: key});
        expect(payBindings(altered).filter(value => value.kind === 'cluster')).toEqual([]);
    }
    const altered = structuredClone(graph);
    altered.expressions = {
        ...altered.expressions as object,
        anotherLookup: {exprType: 'fieldAccess', target: 'state', path: ['clusterPays']}
    };
    expect(payBindings(altered).filter(value => value.kind === 'cluster')).toEqual([]);
    const stale = structuredClone(graph);
    (stale.initialState as Record<string, unknown>).clusterMinSize = 4;
    expect(() => changeBoundPayouts(stale, binding, [{
        symbolId: binding.rows[0].symbolId,
        count: 5,
        value: '1'
    }])).toThrow('changed');
    expect(stale).not.toEqual(graph);
});
test('Alpha-renamed cluster definitions, nodes and expressions retain authored IDs and opaque node/edge fields through minimum changes', () => {
    const graph = createNineLivesGraph() as ModelGraph,
        names = new Map(Object.keys(graph.mechanics!).map(name => [name, `private-${name}`]));
    graph.mechanics = Object.fromEntries(Object.entries(graph.mechanics!).map(([scope, definition]) => [names.get(scope)!, definition]));
    for (const definition of [graph, ...Object.values(graph.mechanics!)]) {
        const nodes = definition.nodes ?? [], nodeIds = new Map(nodes.map(node => [node.id, `authored-${node.id}`])),
            ast = definition.expressions as Record<string, unknown>;
        for (const node of nodes) {
            node.id = nodeIds.get(node.id)!;
            node.opaque = {keep: node.id};
            if (node.mechanicName) node.mechanicName = names.get(node.mechanicName)!;
            for (const field of ['expressionId', 'conditionId', 'stopConditionId']) if (typeof node[field] === 'string') node[field] = `exact-${node[field]}`;
        }
        definition.expressions = Object.fromEntries(Object.entries(ast ?? {}).map(([id, value]) => [`exact-${id}`, value]));
        for (const edge of definition.edges as Record<string, unknown>[]) {
            edge.sourceNodeId = nodeIds.get(String(edge.sourceNodeId));
            edge.targetNodeId = nodeIds.get(String(edge.targetNodeId));
            edge.opaque = {retained: true};
        }
    }
    const original = structuredClone(graph), binding = payBindings(graph).find(value => value.kind === 'cluster')!;
    expect(binding).toBeDefined();
    const next = changeBoundPayouts(graph, binding, [{symbolId: binding.rows[0].symbolId, count: 4, value: '1'}]);
    expect(graph).toEqual(original);
    expect(next.nodes).toEqual(graph.nodes);
    expect((next.initialState as Record<string, unknown>).clusterMinSize).toBe(4);
    for (const [scope, definition] of Object.entries(next.mechanics!)) for (const node of definition.nodes) {
        const prior = graph.mechanics![scope]?.nodes.find(value => value.id === node.id);
        if (prior) expect(node.opaque).toEqual(prior.opaque);
    }
    expect(payBindings(next).some(value => value.kind === 'cluster')).toBe(true);
});

test('Saved grid traversal remains editable without converting its algorithm or adding adjacency data', () => {
    const graph = createNineLivesGraph() as ModelGraph,
        binding = payBindings(graph).find(value => value.kind === 'cluster')!,
        geometry = graph.boardConfig as { columns: number; rows: number },
        root = binding.cluster!.roots[0], definition = graph.mechanics![root],
        observer = definition.nodes!.find(node => node.nodeType === 'library' && !binding.scopes.includes(String(node.mechanicName))),
        board: ClusterBoard = {
            ...geometry, wild: 0, symbolCount: (graph.symbols as unknown[]).length,
            componentTraversal: 'grid',
            pays: Object.fromEntries(binding.rows.map(row => [Number(row.symbolId),
                Object.fromEntries(row.counts.map((count, i) => [count, exactInteger(row.values[i])]))]))
        }, mechanics = new Mechanics();
    expect(mechanics.add(clusterPays(mechanics, board, observer ? {
        mechanic: String(observer.mechanicName), label: String(observer.label)
    } : undefined))).toBe(root);
    graph.mechanics = {...graph.mechanics, ...mechanics.all()};
    delete (graph.initialState as Record<string, unknown>).clusterAdjacency;
    const legacyBinding = payBindings(graph).find(value => value.kind === 'cluster')!;
    expect(legacyBinding).toBeDefined();
    const next = changeBoundPayouts(graph, legacyBinding, [{
        symbolId: legacyBinding.rows[0].symbolId,
        count: 1,
        value: '1'
    }]);
    expect(next.initialState).not.toHaveProperty('clusterAdjacency');
    expect(next.nodes).toEqual(graph.nodes);
    expect(payBindings(next).some(value => value.kind === 'cluster')).toBe(true);
});

test('Changed or mutable adjacency cannot claim the geometric helper payout contract', () => {
    const graph = createNineLivesGraph() as ModelGraph;
    (graph.initialState as Record<string, unknown>).clusterAdjacency = [0];
    expect(payBindings(graph).some(value => value.kind === 'cluster')).toBe(false);
    const mutable = createNineLivesGraph() as ModelGraph;
    mutable.nodes!.push({id: 'replace-arcs', nodeType: 'modifyState', outputKey: 'clusterAdjacency'});
    expect(payBindings(mutable).some(value => value.kind === 'cluster')).toBe(false);
});
