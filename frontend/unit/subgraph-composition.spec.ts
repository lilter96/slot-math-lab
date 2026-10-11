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

const reference = (id: string, target: string | null = 'state') => ({ exprType: 'fieldAccess', target, path: ['expressions', id] });

test('Extraction closes transitive and inline dependencies and preserves nested library parameters', () => {
  const original = graph();
  original.expressions = {
    add: { exprType: 'binary', op: 'Add', left: reference('rate'), right: reference('amount', null) },
    rate: reference('amount'), amount: { exprType: 'constant', kind: 'Integer', value: '7' },
    exit: reference('rate'), unrelated: { exprType: 'constant', kind: 'Integer', value: '99' },
  };
  original.nodes[1].outputs = { state: { ...ports.state, defaultValue: reference('exit') } };
  original.nodes[2].parameters = { multiplier: '3', option: 'bonus' };
  const saved = structuredClone(original);
  const extracted = extractSubgraph(original, 'payout', ['first', 'second'], 'one');
  expect(Object.keys(extracted.mechanics!.payout.expressions).sort()).toEqual(['add', 'amount', 'exit', 'rate']);
  expect(extracted.mechanics!.payout.nodes[1].parameters).toEqual({ multiplier: '3', option: 'bonus' });
  expect(original).toEqual(saved);
  // Extracting the same dependency graph again uses independent IDs without changing shared data.
  const nested = extractSubgraph({ ...extracted, nodes: saved.nodes, edges: saved.edges }, 'other', ['first'], 'two');
  expect(nested.mechanics!.other.expressions).toEqual(extracted.mechanics!.payout.expressions);
});

test('Extraction rejects unresolved and cyclic dependencies before changing the runnable graph', () => {
  const original = graph();
  original.expressions!.add = reference('missingParameter');
  const saved = structuredClone(original);
  expect(() => extractSubgraph(original, 'new', ['first'], 'one')).toThrow(/missingParameter.*not defined/);
  expect(original).toEqual(saved);
  original.expressions!.add = reference('second');
  original.expressions!.second = reference('add');
  expect(() => extractSubgraph(original, 'new', ['first'], 'one')).toThrow(/cycle/);
});
