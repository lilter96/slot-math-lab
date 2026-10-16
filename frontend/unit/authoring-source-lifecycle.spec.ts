import {expect, test} from '@playwright/test';
import {
    changePrizeDomain,
    changeSourceOutcomes,
    copySourceUse,
    createDrawingSource
} from '../src/lib/authoring/sourceLifecycle';
import {sourceKey} from '../src/lib/authoring/sourceIndex';
import {changeBoundReels, reelBindings} from '../src/lib/authoring/reelBindings';
import {cellBindings} from '../src/lib/authoring/cellBindings';
import {weightBindings} from '../src/lib/authoring/weightBindings';
import {prizeBindings} from '../src/lib/authoring/prizeBindings';
import {prizeStudioProof} from './fixtures/prizeStudioProof';
import {emptySourceProof, sharedDrawingProof} from './fixtures/sourceLifecycleProof';
import type {ModelGraph} from '../src/lib/modelProjection';

test('New numeric source emits ordinary atoms and one explicit round entry; collisions do not replace existing source', () => {
    const graph = emptySourceProof() as ModelGraph, before = structuredClone(graph), input = {
        kind: 'outcomes' as const,
        name: 'my-source',
        stateKey: 'myRoll',
        outcomes: [{id: '2', weight: '3', payload: '0'}, {id: '10', weight: '1', payload: '0'}]
    };
    const next = createDrawingSource(graph, input, {kind: 'empty-round'});
    expect(graph).toEqual(before);
    expect(next.opaque).toEqual(graph.opaque);
    expect(next.nodes!.map(node => node.nodeType)).toEqual(['library', 'modifyState', 'metricsSink']);
    expect(next.mechanics!['my-source'].nodes[0].drawWeights).toEqual([{
        outcomeId: '2',
        weight: 3,
        value: 0
    }, {outcomeId: '10', weight: 1, value: 0}]);
    expect(() => createDrawingSource(next, input, {kind: 'definition'})).toThrow('already exists');
    expect(() => createDrawingSource(next, {
        ...input,
        name: 'other'
    }, {kind: 'definition'})).toThrow('unused state key');
});
for (const kind of ['cells', 'reels'] as const) test(`${kind}: copying one use isolates definitions/strip data and preserves original calls and opaque fields`, () => {
    const graph = sharedDrawingProof(kind) as ModelGraph, before = structuredClone(graph),
        next = copySourceUse(graph, sourceKey(null, 'second'), 'private-copy');
    expect(graph).toEqual(before);
    expect(next.mechanics!['shared-draw']).toEqual(graph.mechanics!['shared-draw']);
    expect(next.nodes!.find(node => node.id === 'first')).toEqual(graph.nodes!.find(node => node.id === 'first'));
    expect(next.nodes!.find(node => node.id === 'second')!.mechanicName).toBe('private-copy');
    expect(next.opaque).toEqual(graph.opaque);
    if (kind === 'reels') {
        expect(reelBindings(next)).toHaveLength(2);
        const privateSource = reelBindings(next).find(source => source.source.label.startsWith('private-copy'))!;
        const edited = changeBoundReels(next, privateSource, privateSource.stops.map(strip => strip.map(stop => ({
            ...stop,
            symbol: 'B'
        }))));
        expect((edited.initialState as Record<string, unknown>).sharedStrip0).toEqual((graph.initialState as Record<string, unknown>).sharedStrip0);
    } else {
        expect(cellBindings(next)).toHaveLength(2);
        const source = weightBindings(next).find(source => source.source.scope === 'private-copy')!;
        const edited = changeSourceOutcomes(next, source.key, [{id: 'B', weight: '1', payload: '0'}]);
        expect(cellBindings(edited)).toHaveLength(2);
        expect(edited.mechanics!['shared-draw']).toEqual(graph.mechanics!['shared-draw']);
    }
});
test('Changing one cell domain keeps peers, payloads and board assignment; no-op domains preserve source exactly', () => {
    const graph = sharedDrawingProof('cells') as ModelGraph,
        source = weightBindings(graph).find(source => source.source.scope === 'shared-draw')!;
    const unchanged = changeSourceOutcomes(graph, source.key, source.rows.map(row => ({
        id: row.outcomeId,
        weight: String(row.weight),
        payload: String(row.value)
    })));
    expect(unchanged).toEqual(graph);
    const next = changeSourceOutcomes(graph, source.key, [{id: 'B', weight: '1', payload: '0'}]);
    expect(cellBindings(next)[0].cells[0].source.rows).toEqual([{outcomeId: 'B', weight: 1, value: 0}]);
    expect(next.initialState).toEqual(graph.initialState);
    expect(() => changeSourceOutcomes(graph, source.key, [{
        id: 'unknown',
        weight: '1',
        payload: '0'
    }])).toThrow('dictionary');
});
test('Numeric prize domain synchronizes consumers and exact ordinal precision; retained rows preserve weights/opaque data and removal is explicit', () => {
    const graph = prizeStudioProof() as ModelGraph, binding = prizeBindings(graph)[0], before = structuredClone(graph);
    const next = changePrizeDomain(graph, binding.key, [{value: '1'}, {value: '5'}, {
        value: '10',
        addedWeight: '2'
    }], false);
    expect(graph).toEqual(before);
    expect(prizeBindings(next)[0].rows[0].values).toEqual(['1', '5', '10']);
    expect(next.mechanics![binding.prize!.sources[0].scope!].nodes[0].drawWeights).toEqual([{
        outcomeId: '1',
        weight: 3,
        value: 0
    }, {outcomeId: '5', weight: 1, value: 0}, {outcomeId: '10', weight: 2, value: 0}]);
    expect(() => changePrizeDomain(next, binding.key, [{value: '5'}], false)).toThrow('Confirm discarding');
    expect(prizeBindings(changePrizeDomain(next, binding.key, [{value: '5'}], true))[0].rows[0].values).toEqual(['5']);
    expect(changePrizeDomain(graph, binding.key, [{value: '1'}, {value: '5'}], false)).toEqual(graph);
});
