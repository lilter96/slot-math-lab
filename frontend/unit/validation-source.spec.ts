import { test, expect } from '@playwright/test';
import { validationSource } from '../src/lib/validationSource';
import type { ModelGraph } from '../src/lib/modelProjection';

test('Compiler namespaces resolve repeated references to actual authored nodes and edges', () => {
  const graph: ModelGraph = { nodes: [{ id: 'left', nodeType: 'library', mechanicName: 'shared' }, { id: 'right', nodeType: 'library', mechanicName: 'shared' }], edges: [], mechanics: {
    shared: { nodes: [{ id: 'call', nodeType: 'library', mechanicName: 'leaf' }] },
    leaf: { nodes: [{ id: 'value/with/slashes', nodeType: 'draw' }], edges: [{ id: 'edge/with/slashes' }] },
  } };
  expect(validationSource(graph, 'left/call/value/with/slashes', 'nodes')).toEqual({ mechanics: ['shared', 'leaf'], id: 'value/with/slashes' });
  expect(validationSource(graph, 'right/call/edge/with/slashes', 'edges')).toEqual({ mechanics: ['shared', 'leaf'], id: 'edge/with/slashes' });
  expect(validationSource(graph, 'unknown/value', 'nodes')).toBeNull();
});

test('Ambiguous IDs and recursive definitions do not fabricate source locations', () => {
  const graph: ModelGraph = { nodes: [{ id: 'call', nodeType: 'library', mechanicName: 'shared' }, { id: 'call/leaf', nodeType: 'draw' }], mechanics: {
    shared: { nodes: [{ id: 'leaf', nodeType: 'draw' }, { id: 'repeat', nodeType: 'library', mechanicName: 'shared' }] },
  } };
  expect(validationSource(graph, 'call/leaf', 'nodes')).toBeNull();
  expect(validationSource(graph, 'call/repeat/repeat/missing', 'nodes')).toBeNull();
  expect(validationSource(graph, 'call', 'nodes')).toEqual({ mechanics: [], id: 'call' });
});
