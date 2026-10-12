import {expect, test} from '@playwright/test';
import {createAncientDragonGraph} from '../src/games/ancient-dragon/graph';
import {createWoodlandWhisperGraph} from '../src/games/woodland-whisper/graph';
import {createX7ClubGraph} from '../src/games/x7-club/graph';
import type {ModelGraph} from '../src/lib/modelProjection';
import {computedLineBound} from '../src/lib/authoring/payoutBounds';
import {changeBoundPayout, payBindings} from '../src/lib/authoring/payBindings';
import {computedLineBoundProof} from './fixtures/computedLineBoundProof';

for (const [name, create] of [['Ancient Dragon', createAncientDragonGraph], ['Woodland Whisper', createWoodlandWhisperGraph], ['Woodland bought', () => createWoodlandWhisperGraph(undefined, {buy: true})]] as const) {
    test(`${name}: a highest payout increase updates the proven uncapped bound with the actual loop budget and price`, () => {
        const graph = create() as ModelGraph, bound = computedLineBound(graph);
        expect(bound).not.toBeNull();
        const pay = payBindings(graph).find(pay => pay.kind === 'line')!, row = pay.rows[0], count = row.counts.at(-1)!,
            amount = pay.maximum + 10;
        const before = structuredClone(graph),
            next = changeBoundPayout(graph, pay, row.symbolId, count, String(amount)),
            updated = computedLineBound(next)!;
        expect(updated.cap).toBeGreaterThan(bound!.cap);
        expect(next.nodes!.find(node => node.id === bound!.sinkId)!.winCap).toBe(updated.cap);
        expect(next.mechanics).toEqual(graph.mechanics);
        expect(graph).toEqual(before);
        expect(updated.ordinarySpins).toBe('1');
    });
}
test('Actual game-rule caps and unproven round accumulation stay unclassified and cannot be silently raised', () => {
    const rule = createX7ClubGraph() as ModelGraph;
    expect(computedLineBound(rule)).toBeNull();
    const graph = createAncientDragonGraph() as ModelGraph;
    const spin = Object.values(graph.mechanics!).find(scope => scope.nodes.some(node => node.outputKey === 'spinCoins'))!;
    const round = spin.nodes.find(node => node.outputKey === 'roundCoins')!;
    spin.nodes.push({...round, id: 'custom-additional-round-award'});
    expect(computedLineBound(graph)).toBeNull();
    const changed = createAncientDragonGraph() as ModelGraph;
    changed.nodes!.push({id: 'custom-put', nodeType: 'putState', stateKey: 'linePays'});
    expect(computedLineBound(changed)).toBeNull();
    const plugin = createAncientDragonGraph() as ModelGraph;
    Object.values(plugin.mechanics!)[0].nodes.push({
        id: 'custom-plugin',
        nodeType: 'plugin',
        pluginId: 'external-operation'
    });
    expect(computedLineBound(plugin)).toBeNull();
});
test('Independent finite fixture has budget bound 15 and a six-credit payout needs bound 35', () => {
    const graph = computedLineBoundProof() as ModelGraph;
    expect(computedLineBound(graph)).toMatchObject({
        cap: 15,
        ordinarySpins: '1',
        repeatedSpins: '3',
        freeMultiplier: 3
    });
    const binding = payBindings(graph).find(binding => binding.kind === 'line')!;
    const next = changeBoundPayout(graph, binding, '1', 2, '6');
    expect(next.nodes!.find(node => node.id === 'sink')!.winCap).toBe(35);
    const capped = changeBoundPayout(graph, binding, '1', 2, '6', 'keep');
    expect(capped.nodes!.find(node => node.id === 'sink')!.winCap).toBe(15);
    const changed = structuredClone(graph);
    changed.mechanics!.feature.nodes.find(node => node.nodeType === 'loop')!.maxIterations = 4;
    expect(() => changeBoundPayout(changed, binding, '1', 2, '6')).toThrow('source');
});
test('Explicit keep policy permits X7 payout increases while retaining its authored game-rule cap', () => {
    const graph = createX7ClubGraph() as ModelGraph,
        binding = payBindings(graph).find(binding => binding.kind === 'line')!;
    expect(binding.cap).not.toBeNull();
    const row = binding.rows[0],
        next = changeBoundPayout(graph, binding, row.symbolId, row.counts.at(-1)!, String(binding.maximum + 100), 'keep');
    expect(next.nodes).toEqual(graph.nodes);
    expect(next.mechanics).toEqual(graph.mechanics);
    expect(() => changeBoundPayout(graph, binding, row.symbolId, row.counts[0], '1', 'recompute')).toThrow('No proven');
});
