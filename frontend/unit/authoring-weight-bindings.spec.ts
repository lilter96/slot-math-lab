import { test, expect } from '@playwright/test';
import { weightBindings, changeBoundWeights } from '../src/lib/authoring/weightBindings';
import type { ModelGraph } from '../src/lib/modelProjection';
const rows = [{ outcomeId: '001', weight: 3, value: 0, opaque: true }, { outcomeId: 'zero', weight: 1, value: 0 }];
const graph: ModelGraph = { nodes: [{ id: 'first', nodeType: 'library', mechanicName: 'shared' }, { id: 'second', nodeType: 'library', mechanicName: 'shared' }], edges: [], opaque: { keep: true }, mechanics: { shared: { nodes: [{ id: 'draw', nodeType: 'draw', drawWeights: rows, stateWriteKey: 'symbol' }, { id: 'independent', nodeType: 'draw', drawWeights: structuredClone(rows), stateWriteKey: 'otherSymbol' }], expressions: {} } } };
test('Binding declares exact values and shared callers without merging equal independent distributions', () => {
  const bindings = weightBindings(graph); expect(bindings).toHaveLength(2);
  expect(bindings[0]).toMatchObject({ capability: 'editable', total: '4', writes: [{ outcomeIds: ['001', 'zero'] }] });
  expect(bindings[0].uses.map(use => use.id)).toEqual(['first', 'second']);
  const before = JSON.stringify(graph), next = changeBoundWeights(graph, bindings[0], new Map([['zero', '3'], ['001', '1']]));
  expect(next.mechanics!.shared.nodes[0]).toMatchObject({ stateWriteKey: 'symbol', drawWeights: [{ outcomeId: '001', weight: 1, value: 0, opaque: true }, { outcomeId: 'zero', weight: 3, value: 0 }] });
  expect(next.mechanics!.shared.nodes[1]).toEqual(graph.mechanics!.shared.nodes[1]); expect(next.opaque).toEqual(graph.opaque); expect(JSON.stringify(graph)).toBe(before);
});
test('Stale definitions and changed shared-use scope refuse edits while invalid/dynamic sources remain visible', () => {
  const binding = weightBindings(graph)[0], changed = structuredClone(graph); changed.nodes!.pop();
  expect(() => changeBoundWeights(changed, binding, new Map([['001', '1'], ['zero', '3']]))).toThrow('shared uses changed');
  const dynamic: ModelGraph = { nodes: [{ id: 'dynamic', nodeType: 'draw', drawWeights: rows, weightExpressionId: 'state-weights' }, { id: 'bad', nodeType: 'draw', drawWeights: [{ outcomeId: 'duplicate', weight: 1, value: 0 }, { outcomeId: 'duplicate', weight: 1, value: 0 }] }] };
  expect(weightBindings(dynamic).map(item => item.capability)).toEqual(['dynamic', 'custom']);
  expect(weightBindings(dynamic).every(item => !item.writes.length)).toBe(true);
  expect(() => changeBoundWeights(graph, binding, new Map([['001', ''], ['zero', '1']]))).toThrow('whole weights');
});
