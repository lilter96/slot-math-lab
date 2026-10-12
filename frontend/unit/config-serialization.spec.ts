import { expect, test } from '@playwright/test';
import { buildConfigPayload } from '../src/lib/configPayload';
import type { GraphNode } from '../src/store';
const nodes: GraphNode[] = [{ id: 'draw', type: 'draw', position: { x: 0, y: 0 }, data: { nodeType: 'draw', label: 'Draw', weightExpressionId: 'state-weights' } }];

test('Strict serialization rejects malformed legacy DSL without modifying its editable source', () => {
  const original = JSON.stringify(nodes);
  expect(() => buildConfigPayload(nodes, [], { name: 'Draft' })).toThrow('Select a state field');
  expect(JSON.stringify(nodes)).toBe(original);
  const fixed = nodes.map(node => ({ ...node, data: { ...node.data, weightExpressionId: 'state.weights' } }));
  const config = buildConfigPayload(fixed, [], { name: 'Draft', tables: { initialState: { weights: [3, 1] }, unknown: { retained: true } } })!;
  expect(config.expressions).toMatchObject({ 'draw:weightExpressionId': { exprType: 'fieldAccess', target: 'state', path: ['weights'] } });
  expect(config.unknown).toEqual({ retained: true });
});

test('Outgoing model serialization rejects unsupported numeric values before JSON can normalize them', () => {
  const plain = nodes.map(node => ({ ...node, data: { ...node.data, weightExpressionId: '' } }));
  for (const value of [Infinity, NaN, Number.MAX_SAFE_INTEGER + 1]) expect(() => buildConfigPayload(plain, [], { name: 'Draft', tables: { initialState: { value } } })).toThrow(/exact browser range/);
  expect(buildConfigPayload(plain, [], { name: 'Draft', tables: { initialState: { value: 10000000000 } } })?.initialState).toEqual({ value: 10000000000 });
});
