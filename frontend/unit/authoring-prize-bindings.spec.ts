import {expect, test} from '@playwright/test';
import {createX7ClubGraph} from '../src/games/x7-club/graph';
import {createNineLivesGraph} from '../src/games/nine-lives-tgslots/graph';
import {changeBoundPayouts, payBindings} from '../src/lib/authoring/payBindings';
import {applyStudioPatches} from '../src/lib/authoring/studioEdits';
import {prizeStudioProof} from './fixtures/prizeStudioProof';
import type {ModelGraph} from '../src/lib/modelProjection';

for (const [label, create] of [['X7', createX7ClubGraph], ['TGSlots', createNineLivesGraph]] as const) test(`${label}: both entries bind numeric prize consumers and preserve independent weights/payloads`, () => {
    for (const buy of [false, true]) {
        const graph = create(undefined, {buy}) as ModelGraph, before = structuredClone(graph),
            binding = payBindings(graph).find(value => value.kind === 'prize');
        expect(binding).toBeDefined();
        const row = binding!.rows[0], value = binding!.maximum + 1,
            next = changeBoundPayouts(graph, binding!, [{symbolId: row.symbolId, count: 1, value: String(value)}]);
        expect(graph).toEqual(before);
        expect(next.nodes).toEqual(graph.nodes);
        expect(next.edges).toEqual(graph.edges);
        expect(next.initialState).toEqual(graph.initialState);
        for (const source of binding!.prize!.sources) {
            const node = next.mechanics![source.scope!].nodes.find(node => node.id === source.id)!;
            const original = source.node.drawWeights as Record<string, unknown>[],
                changed = node.drawWeights as Record<string, unknown>[];
            expect(changed[0]).toEqual({...original[0], outcomeId: String(value)});
            expect(changed.slice(1)).toEqual(original.slice(1));
        }
        expect(payBindings(next).find(value => value.kind === 'prize')!.rows[0].values[0]).toBe(String(value));
        expect(changeBoundPayouts(graph, binding!, [{
            symbolId: row.symbolId,
            count: 1,
            value: row.values[0]
        }])).toEqual(graph);
    }
});
test('Prize changes compose with weights in either draft order and duplicate numeric identities reject without mutating input', () => {
    const graph = prizeStudioProof() as ModelGraph, binding = payBindings(graph).find(value => value.kind === 'prize')!,
        source = binding.prize!.sources[0];
    const payout = {kind: 'payout' as const, key: binding.key, symbolId: '1', count: 1, value: '3'},
        weight = {kind: 'weights' as const, key: source.key, values: [['1', '1'], ['5', '1']] as [string, string][]};
    expect(applyStudioPatches(graph, [payout, weight])).toEqual(applyStudioPatches(graph, [weight, payout]));
    expect(() => changeBoundPayouts(graph, binding, [{symbolId: '1', count: 1, value: '5'}])).toThrow('same identity');
    expect(() => changeBoundPayouts(graph, binding, [{symbolId: '1', count: 1, value: '0'}])).toThrow('positive');
    const altered = structuredClone(graph);
    altered.nodes!.push({id: 'override', nodeType: 'putState', stateKey: 'coinStake'});
    expect(payBindings(altered).filter(value => value.kind === 'prize')).toEqual([]);
});
