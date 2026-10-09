import { useAppStore, type GraphNode, type GraphEdge } from '../store';
import { buildConfigPayload } from './configPayload';
export function loadProject(config: Record<string, unknown>) {
  if (!Array.isArray(config.nodes) || !Array.isArray(config.edges)) throw new Error('Project must contain nodes and edges');
  const frontendTypes: Record<string, GraphNode['data']['nodeType']> = { metricsSink: 'sink', getState: 'state', putState: 'state', modifyState: 'state', data: 'state' };
  const nodes = config.nodes.map((n, i): GraphNode => {
    if (!n || typeof n.id !== 'string' || typeof n.nodeType !== 'string') throw new Error('Invalid node');
    const nodeType = frontendTypes[n.nodeType] ?? n.nodeType;
    return { id: n.id, type: nodeType, position: { x: 80 + (i % 5) * 270, y: 100 + Math.floor(i / 5) * 200 }, data: {
      nodeType, label: n.label ?? n.id, backendNode: n, winCap: n.winCap, drawWeights: n.drawWeights,
      mechanicName: n.mechanicName, stateWriteKey: n.stateWriteKey, stateKey: n.outputKey ?? n.stateKey,
      stateOp: n.nodeType === 'modifyState' ? 'modify' : n.nodeType === 'putState' ? 'put' : 'get',
      iterations: n.maxIterations, terminationExpr: n.stopConditionId, expression: n.conditionId ?? n.expressionId,
    } };
  });
  const edges = config.edges.map((e): GraphEdge => ({ id: e.id, source: e.sourceNodeId, target: e.targetNodeId,
    sourceHandle: e.sourcePort, targetHandle: e.targetPort }));
  const { nodes: _nodes, edges: _edges, name, ...tables } = config;
  void _nodes; void _edges;
  useAppStore.setState({ nodes, edges, tables, configName: typeof name === 'string' ? name : 'Imported project', selectedNodeId: null });
}
export function openMechanic(name: string) {
  const graph = exportProject(); if (!graph) return;
  const mechanic = (graph.mechanics as Record<string, Record<string, unknown>>)?.[name];
  if (!mechanic) throw new Error('Mechanic is not defined in this project');
  const trail = [...useAppStore.getState().graphTrail, { graph, mechanic: name }];
  const { nodes: _nodes, edges: _edges, ...shared } = graph; void _nodes; void _edges;
  loadProject({ ...shared, ...mechanic, expressions: mechanic.expressions ?? {} });
  useAppStore.setState({ graphTrail: trail });
}
export function closeMechanic() {
  const state = useAppStore.getState(); const current = exportProject(); const parent = state.graphTrail.at(-1);
  if (!parent || !current) return;
  const mechanics = { ...(current.mechanics as Record<string, Record<string, unknown>>) };
  mechanics[parent.mechanic] = { ...mechanics[parent.mechanic], nodes: current.nodes, edges: current.edges, expressions: current.expressions };
  loadProject({ ...parent.graph, mechanics, initialState: current.initialState });
  useAppStore.setState({ graphTrail: state.graphTrail.slice(0, -1) });
}
export function rootProject(): Record<string, unknown> | null {
  // Save every open subgraph back into its parent before running/exporting the full model.
  while (useAppStore.getState().graphTrail.length) closeMechanic();
  return exportProject();
}
export function exportProject() {
  const state = useAppStore.getState();
  return buildConfigPayload(state.nodes, state.edges, { name: state.configName ?? 'Untitled', tables: state.tables });
}
export function loadCoinExample() {
  loadProject({ schemaVersion: '1.0.0', name: 'REF-A Coin', nodes: [
    { nodeType: 'draw', id: 'coin-draw', label: 'Coin draw', drawWeights: [
      { outcomeId: 'three', weight: 1, value: 3 }, { outcomeId: 'one', weight: 3, value: 1 }, { outcomeId: 'zero', weight: 4, value: 0 }],
      outputs: { out: { name: 'out', type: 'Wins' } } },
    { nodeType: 'metricsSink', id: 'coin-sink', label: 'Metrics', winCap: 10000, inputs: { in: { name: 'in', type: 'Wins' } } },
  ], edges: [{ id: 'coin-edge', sourceNodeId: 'coin-draw', sourcePort: 'out', targetNodeId: 'coin-sink', targetPort: 'in' }] });
}

export const catalogExamples = Object.fromEntries(Object.entries(import.meta.glob('../../../backend/SlotMath.Core.Tests/TestData/AuditCatalog/*.json', { eager: true, import: 'default' })).map(([path, config]) => [path.split('/').pop()!.replace('.json', ''), config as Record<string, unknown>]));

/** Build the full current draft without navigating away from an open mechanic. */
export function peekRootProject(): Record<string, unknown> | null {
  let current = exportProject();
  for (const parent of useAppStore.getState().graphTrail.toReversed()) {
    if (!current) return null;
    const mechanics = { ...(current.mechanics as Record<string, Record<string, unknown>>) };
    mechanics[parent.mechanic] = { ...mechanics[parent.mechanic], nodes: current.nodes, edges: current.edges, expressions: current.expressions };
    current = { ...parent.graph, mechanics, initialState: current.initialState };
  }
  return current;
}
