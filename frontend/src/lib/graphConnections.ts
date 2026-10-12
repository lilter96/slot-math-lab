import { mapNodeToBackend } from './configPayload';
import type { GraphNode, GraphEdge, GraphNodeData } from '../store';
import type { Connection } from '@xyflow/react';
export interface GraphPort { id: string; type: string }
export function nodePorts(node: Pick<GraphNode, 'id' | 'data'>, direction: 'inputs' | 'outputs'): GraphPort[] {
  const canonical = mapNodeToBackend({ ...node, position: { x: 0, y: 0 } } as GraphNode);
  const values = canonical[direction] as Record<string, { type?: unknown }> | undefined;
  return Object.entries(values ?? {}).map(([id, port]) => ({ id, type: typeof port?.type === 'string' ? port.type : 'Unknown' }));
}
export function dataPorts(id: string, data: GraphNodeData, direction: 'inputs' | 'outputs') { return nodePorts({ id, data }, direction); }
export function connectionError(nodes: GraphNode[], edges: GraphEdge[], connection: Connection): string | null {
  const source = nodes.find(node => node.id === connection.source), target = nodes.find(node => node.id === connection.target);
  if (!source || !target) return 'A selected node is no longer in this graph. Select current source and target nodes.';
  const output = nodePorts(source, 'outputs'), input = nodePorts(target, 'inputs');
  const find = (ports: GraphPort[], id: string | null) => id ? ports.find(port => port.id === id) : ports.length === 1 ? ports[0] : undefined;
  const from = find(output, connection.sourceHandle), to = find(input, connection.targetHandle);
  if (!from) return 'Choose an output port declared by the source node.';
  if (!to) return 'Choose an input port declared by the target node.';
  if (from.type === 'Unknown' || to.type === 'Unknown') return 'The selected port type is not declared. Inspect the source graph.';
  if (from.type !== to.type && !(from.type === 'Symbol' && to.type === 'String') && !(from.type === 'Boolean' && to.type === 'Trigger')) return `Port type mismatch: ${from.id} is ${from.type}, while ${to.id} is ${to.type}.`;
  if (edges.some(edge => edge.source === source.id && edge.target === target.id
    && (edge.sourceHandle ?? (output.length === 1 ? output[0].id : '')) === from.id
    && (edge.targetHandle ?? (input.length === 1 ? input[0].id : '')) === to.id)) return 'This exact port connection already exists.';
  return null;
}
