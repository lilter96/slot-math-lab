import {expect, test} from '@playwright/test';
import {createWoodlandWhisperGraph} from '../src/games/woodland-whisper/graph';
import {reelBindings} from '../src/lib/authoring/reelBindings';
import {changeBoundWeights, weightBindings} from '../src/lib/authoring/weightBindings';
import type {ModelGraph} from '../src/lib/modelProjection';

const rows = [{outcomeId: '001', weight: 3, value: 0, opaque: true}, {outcomeId: 'zero', weight: 1, value: 0}];
const graph: ModelGraph = {
    nodes: [{id: 'first', nodeType: 'library', mechanicName: 'shared'}, {
        id: 'second',
        nodeType: 'library',
        mechanicName: 'shared'
    }],
    edges: [],
    opaque: {keep: true},
    mechanics: {
        shared: {
            nodes: [{
                id: 'draw',
                nodeType: 'draw',
                drawWeights: rows,
                stateWriteKey: 'symbol'
            }, {id: 'independent', nodeType: 'draw', drawWeights: structuredClone(rows), stateWriteKey: 'otherSymbol'}],
            expressions: {}
        }
    }
};
test('Binding declares exact values and shared callers without merging equal independent distributions', () => {
    const bindings = weightBindings(graph);
    expect(bindings).toHaveLength(2);
    expect(bindings[0]).toMatchObject({capability: 'editable', total: '4', writes: [{outcomeIds: ['001', 'zero']}]});
    expect(bindings[0].uses.map(use => use.id)).toEqual(['first', 'second']);
    const before = JSON.stringify(graph),
        next = changeBoundWeights(graph, bindings[0], new Map([['zero', '3'], ['001', '1']]));
    expect(next.mechanics!.shared.nodes[0]).toMatchObject({
        stateWriteKey: 'symbol',
        drawWeights: [{outcomeId: '001', weight: 1, value: 0, opaque: true}, {outcomeId: 'zero', weight: 3, value: 0}]
    });
    expect(next.mechanics!.shared.nodes[1]).toEqual(graph.mechanics!.shared.nodes[1]);
    expect(next.opaque).toEqual(graph.opaque);
    expect(JSON.stringify(graph)).toBe(before);
});
test('Stale definitions and changed shared-use scope refuse edits while invalid/dynamic sources remain visible', () => {
    const binding = weightBindings(graph)[0], changed = structuredClone(graph);
    changed.nodes!.pop();
    expect(() => changeBoundWeights(changed, binding, new Map([['001', '1'], ['zero', '3']]))).toThrow('shared uses changed');
    const dynamic: ModelGraph = {
        nodes: [{
            id: 'dynamic',
            nodeType: 'draw',
            drawWeights: rows,
            weightExpressionId: 'state-weights'
        }, {
            id: 'bad',
            nodeType: 'draw',
            drawWeights: [{outcomeId: 'duplicate', weight: 1, value: 0}, {outcomeId: 'duplicate', weight: 1, value: 0}]
        }]
    };
    expect(weightBindings(dynamic).map(item => item.capability)).toEqual(['dynamic', 'custom']);
    expect(weightBindings(dynamic).every(item => !item.writes.length)).toBe(true);
    expect(() => changeBoundWeights(graph, binding, new Map([['001', ''], ['zero', '1']]))).toThrow('whole weights');
});
test('Conditioned reel distributions remain visible but cannot bypass their dependency contract through a generic weight edit', () => {
    const graph = createWoodlandWhisperGraph(undefined, {buy: true}) as ModelGraph, source = reelBindings(graph)[0];
    const scopes = new Set([...source.source.stops.map(stop => stop.scope), ...source.source.conditions!.map(condition => condition.scope)]);
    const generated = weightBindings(graph).filter(binding => binding.source.scope !== null && scopes.has(binding.source.scope));
    expect(generated.length).toBeGreaterThan(0);
    const before = structuredClone(graph);
    for (const binding of generated) {
        expect(binding.capability).toBe('computed');
        expect(binding.writes).toEqual([]);
        expect(() => changeBoundWeights(graph, binding, new Map(binding.rows.map(row => [row.outcomeId, String(row.weight + 1)])))).toThrow('conditioned reel');
    }
    expect(graph).toEqual(before);
});
