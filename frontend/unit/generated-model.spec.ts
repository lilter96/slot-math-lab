import { expect, test } from '@playwright/test';
import { generatedModel } from '../src/lib/generatedModel';
const model = { schemaVersion: '1.0.0', nodes: [{ id: 'a', nodeType: 'draw', unknown: { keep: true } }], edges: [], initialState: { count: 10000000000 }, expressions: { keep: { exprType: 'constant', kind: 'Integer', value: '9223372036854775807' } } };
test('Generated model admission preserves opaque canonical fields and rejects malformed identities before loading', () => {
  expect(generatedModel(model)).toBe(model);
  for (const value of [null, {}, { ...model, nodes: [] }, { ...model, nodes: [model.nodes[0], model.nodes[0]] }, { ...model, edges: [{ id: 'e', sourceNodeId: 'a', targetNodeId: 'missing', sourcePort: 'out', targetPort: 'in' }] }, { ...model, initialState: { count: Infinity } }]) expect(() => generatedModel(value)).toThrow();
});
