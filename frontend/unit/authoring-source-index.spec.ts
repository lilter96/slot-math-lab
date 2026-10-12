import { test, expect } from '@playwright/test';
import { indexGraphSources, resolveNodeSource, sourceKey } from '../src/lib/authoring/sourceIndex';
import type { ModelGraph } from '../src/lib/modelProjection';
const graph: ModelGraph = {
  custom: { opaque: ['keep', 10000000000] }, initialState: { counter: 0 },
  nodes: [{ id: '001', nodeType: 'library', mechanicName: 'custom-round', parameters: { limit: { exprType: 'fieldAccess', target: 'state', path: ['counter'] } } }],
  edges: [], expressions: { untouched: { exprType: 'constant', kind: 'Integer', value: '9223372036854775807' } },
  mechanics: { 'custom-round': { nodes: [{ id: '001', nodeType: 'modifyState', outputKey: 'counter', expressionId: 'update', opaque: { keep: true } }], edges: [], expressions: { update: { exprType: 'fieldAccess', target: 'state', path: ['counter'] } } } },
};
test('Source inventory keeps scoped string identities, inline reads, consumers and opaque source without mutation', () => {
  const before = JSON.stringify(graph), index = indexGraphSources(graph);
  expect(index.issues).toEqual([]); expect(index.nodes.map(node => node.key)).toEqual([sourceKey(null, '001'), sourceKey('custom-round', '001')]);
  expect(index.libraryUses).toMatchObject([{ definition: 'custom-round', present: true }]);
  expect(index.stateReads.map(read => read.statePath)).toEqual([['counter'], ['counter']]);
  expect(index.stateWrites.map(write => write.key)).toEqual(['counter']); expect(index.expressions[0].value).toEqual(graph.expressions && (graph.expressions as Record<string, unknown>).untouched);
  expect(JSON.stringify(graph)).toBe(before);
});
test('Source resolution follows IDs after ordering/renaming and refuses missing or duplicate edit targets', () => {
  const reordered = structuredClone(graph); reordered.nodes!.unshift({ id: 'other', nodeType: 'draw' });
  expect(resolveNodeSource(reordered, sourceKey(null, '001')).path).toEqual(['nodes', 1]);
  reordered.mechanics = { 'renamed-definition': reordered.mechanics!['custom-round'] }; reordered.nodes![1].mechanicName = 'renamed-definition';
  expect(indexGraphSources(reordered).libraryUses[0]).toMatchObject({ definition: 'renamed-definition', present: true });
  expect(resolveNodeSource(reordered, sourceKey('renamed-definition', '001')).node.outputKey).toBe('counter');
  expect(() => resolveNodeSource(reordered, sourceKey('custom-round', '001'))).toThrow('missing or ambiguous');
  reordered.nodes!.push(reordered.nodes![1]); expect(() => resolveNodeSource(reordered, sourceKey(null, '001'))).toThrow('missing or ambiguous');
});
test('External catalog operations and dynamic expressions stay explicit without inventing executable definitions', () => {
  const index = indexGraphSources({ nodes: [{ id: 'external', nodeType: 'library', mechanicName: 'catalog-operation' }, { id: 'dynamic', nodeType: 'draw', weightExpressionId: 'weights' }, { id: 'opaque', nodeType: 'plugin', outputKey: 'not-a-proven-write' }], expressions: { weights: { exprType: 'fieldAccess', target: 'state', path: ['weights', 0] } } });
  expect(index.libraryUses[0].present).toBe(false); expect(index.nodes[1].node.weightExpressionId).toBe('weights');
  expect(index.stateWrites).toEqual([]); expect(index.stateReads[0].statePath).toEqual(['weights', 0]); expect(index.expressions).toHaveLength(1);
});
test('PutState declares its actual StateKey write rather than a ModifyState outputKey alias', () => {
  const index = indexGraphSources({ nodes: [{ id: 'put', nodeType: 'putState', stateKey: 'actual', outputKey: 'opaque-extension' }, { id: 'data', nodeType: 'data', stateKey: 'lookup', rows: ['1', '2'] }], edges: [] });
  expect(index.stateWrites.map(write => write.key)).toEqual(['actual', 'lookup']);
});
