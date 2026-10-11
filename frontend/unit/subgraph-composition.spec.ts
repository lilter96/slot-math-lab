import { test, expect } from '@playwright/test';
import { extractSubgraph, type ComposableGraph } from '../src/lib/subgraphComposition';

const ports = { state: { name: 'state', type: 'State' } };
const graph = (): ComposableGraph => ({
  name: 'Example', initialState: { amount: 3 }, paytables: { award: 5 },
  nodes: [
    { id: 'before', nodeType: 'getState', outputs: ports },
    { id: 'first', nodeType: 'modifyState', expressionId: 'add', outputKey: 'amount', inputs: ports, outputs: ports },
    { id: 'second', nodeType: 'library', mechanicName: 'award', inputs: ports, outputs: ports },
    { id: 'after', nodeType: 'metricsSink', inputs: ports },
  ],
  edges: [['before', 'first'], ['first', 'second'], ['second', 'after']].map(([sourceNodeId, targetNodeId], i) => ({
    id: `e${i}`, sourceNodeId, targetNodeId, sourcePort: 'state', targetPort: 'state',
  })),
  expressions: { add: { exprType: 'constant', kind: 'Integer', value: '8' }, unrelated: { exprType: 'constant', kind: 'Boolean', value: 'false' } },
  mechanics: { award: { name: 'award', nodes: [{ id: 'award', nodeType: 'modifyState', inputs: ports, outputs: ports }], edges: [], expressions: {} } },
});

test('Extraction keeps expressions, dependencies, data and external port connections', () => {
  const original = graph();
  const saved = structuredClone(original);
  const result = extractSubgraph(original, ' payout ', ['first', 'second'], 'instance');
  expect(original).toEqual(saved);
  expect(result.nodes.map(node => node.id)).toEqual(['before', 'instance', 'after']);
  expect(result.edges).toEqual([
    { ...original.edges[0], targetNodeId: 'instance' },
    { ...original.edges[2], sourceNodeId: 'instance' },
  ]);
  expect(result.mechanics!.payout.nodes).toEqual(original.nodes.slice(1, 3));
  expect(result.mechanics!.payout.edges).toEqual([original.edges[1]]);
  expect(result.mechanics!.payout.expressions).toEqual({ add: original.expressions!.add });
  expect(result.mechanics!.award).toEqual(original.mechanics!.award);
  expect(result.initialState).toEqual(original.initialState);
  expect(result.paytables).toEqual(original.paytables);
  expect(result.nodes[1]).toMatchObject({ nodeType: 'library', mechanicName: 'payout', inputs: ports, outputs: ports });
});

test('Extraction rejects ambiguous boundaries, missing selections and name collisions without mutating the model', () => {
  const original = graph(), saved = structuredClone(original);
  expect(() => extractSubgraph(original, 'award', ['first'], 'instance')).toThrow(/already used/);
  expect(() => extractSubgraph(original, 'new', ['first'], 'before')).toThrow(/unique ID/);
  expect(() => extractSubgraph(original, 'new', [], 'instance')).toThrow(/Select/);
  expect(() => extractSubgraph(original, 'new', ['absent'], 'instance')).toThrow(/Select/);
  expect(() => extractSubgraph(original, 'new', ['after'], 'instance')).toThrow(/sink/);
  expect(() => extractSubgraph(original, 'new', ['before', 'second'], 'instance')).toThrow(/Several outputs/);
  expect(original).toEqual(saved);
});
