import { test, expect } from '@playwright/test';
import { connectionError, nodePorts } from '../src/lib/graphConnections';
import type { GraphNode, GraphEdge } from '../src/store';

const node = (id: string, inputs: Record<string, { type: string }>, outputs: Record<string, { type: string }>): GraphNode => ({ id, position: { x: 0, y: 0 }, data: { label: id, nodeType: 'state', backendNode: { id, nodeType: 'getState', inputs, outputs } } });
test('Native state-to-state connections use declared types, preserving implicit conversions and multiple outputs', () => {
  const source = node('a', {}, { first: { type: 'State' }, second: { type: 'State' }, symbol: { type: 'Symbol' }, yes: { type: 'Boolean' } });
  const target = node('b', { state: { type: 'State' }, text: { type: 'String' }, trigger: { type: 'Trigger' } }, {});
  const nodes = [source, target];
  expect(connectionError(nodes, [], { source: 'a', target: 'b', sourceHandle: 'first', targetHandle: 'state' })).toBeNull();
  expect(connectionError(nodes, [], { source: 'a', target: 'b', sourceHandle: 'symbol', targetHandle: 'text' })).toBeNull();
  expect(connectionError(nodes, [], { source: 'a', target: 'b', sourceHandle: 'yes', targetHandle: 'trigger' })).toBeNull();
  expect(connectionError(nodes, [], { source: 'a', target: 'b', sourceHandle: 'yes', targetHandle: 'text' })).toContain('type mismatch');
  expect(connectionError(nodes, [], { source: 'a', target: 'b', sourceHandle: null, targetHandle: 'state' })).toContain('output port');
  const edge = { id: 'preserved-id', source: 'a', target: 'b', sourceHandle: 'first', targetHandle: 'state' } as GraphEdge;
  expect(connectionError(nodes, [edge], { source: 'a', target: 'b', sourceHandle: 'first', targetHandle: 'state' })).toContain('already exists');
  expect(connectionError(nodes, [edge], { source: 'a', target: 'b', sourceHandle: 'second', targetHandle: 'state' })).toBeNull();
});
test('Legacy primitive ports match canonical serialization and reject missing nodes/ports and unknown types', () => {
  const draw: GraphNode = { id: 'draw', position: { x: 0, y: 0 }, data: { nodeType: 'draw', label: 'Draw' } };
  const sink: GraphNode = { id: 'sink', position: { x: 0, y: 0 }, data: { nodeType: 'sink', label: 'Sink' } };
  expect(nodePorts(draw, 'inputs')).toEqual([{ id: 'in', type: 'Wins' }]);
  expect(nodePorts(draw, 'outputs')).toEqual([{ id: 'out', type: 'Wins' }]);
  expect(nodePorts(sink, 'outputs')).toEqual([]);
  expect(connectionError([draw, sink], [], { source: 'draw', target: 'sink', sourceHandle: null, targetHandle: null })).toBeNull();
  expect(connectionError([draw, sink], [], { source: 'missing', target: 'sink', sourceHandle: null, targetHandle: null })).toContain('no longer');
  expect(connectionError([draw, sink], [], { source: 'draw', target: 'sink', sourceHandle: 'bad', targetHandle: 'in' })).toContain('output port');
  const unknown = node('unknown', { in: {} as { type: string } }, {});
  expect(connectionError([draw, unknown], [], { source: 'draw', target: 'unknown', sourceHandle: 'out', targetHandle: 'in' })).toContain('not declared');
});
