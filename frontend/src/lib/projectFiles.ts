import { assertModelNumbers } from './modelJson';
import { useAppStore, type GraphNode, type GraphEdge, retainCurrentProject } from '../store';
import { buildConfigPayload } from './configPayload';
export function loadProject(config: Record<string, unknown>) {
  assertModelNumbers(config);
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
  useAppStore.setState({ nodes, edges, tables, configName: typeof name === 'string' ? name : 'Imported project', selectedNodeId: null, inspectorOpen: useAppStore.getState().inspectorPinned, liveRtp: null, liveProvenance: null });
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

/** Add a reference to the current editable definition, preserving its ports and shared dependencies. */
export function reuseMechanic(name: string) {
  const root = rootProject();
  const mechanic = (root?.mechanics as Record<string, { nodes: Record<string, unknown>[]; edges: Record<string, unknown>[] }> | undefined)?.[name];
  if (!mechanic) throw new Error('Mechanic is not defined in this project');
  const inputs: Record<string, unknown> = {}, outputs: Record<string, unknown> = {};
  for (const node of mechanic.nodes) {
    for (const [port, contract] of Object.entries(node.inputs as Record<string, unknown> ?? {})) {
      if (!mechanic.edges.some(edge => edge.targetNodeId === node.id && edge.targetPort === port)) inputs[port] = contract;
    }
    for (const [port, contract] of Object.entries(node.outputs as Record<string, unknown> ?? {})) {
      if (!(port in outputs) && !mechanic.edges.some(edge => edge.sourceNodeId === node.id && edge.sourcePort === port)) outputs[port] = contract;
    }
  }
  const id = `subgraph-${crypto.randomUUID()}`;
  const count = useAppStore.getState().nodes.length;
  useAppStore.getState().addNode({ id, type: 'library', position: { x: 80 + count % 5 * 270, y: 100 + Math.floor(count / 5) * 200 },
    data: { nodeType: 'library', label: name, mechanicName: name,
      backendNode: { nodeType: 'library', id, label: name, mechanicName: name, parameters: {}, inputs, outputs } } });
  useAppStore.getState().selectNode(id);
}
export function exportProject() {
  const state = useAppStore.getState();
  const name = state.configName ?? 'Untitled';
  const config = buildConfigPayload(state.nodes, state.edges, { name, tables: state.tables });
  // An empty definition is a valid editing draft. Save it before returning to its
  // parent; otherwise rootProject repeatedly tries to close the same empty graph.
  return config ?? (state.graphTrail.length ? { ...state.tables, schemaVersion: '1.0.0', name, nodes: [], edges: [] } : null);
}
/** Switch examples/imports while retaining a restorable canonical editor draft. */
export function switchProject(config: Record<string, unknown>) {
  assertModelNumbers(config);
  retainCurrentProject();
  const current = useAppStore.getState();
  if (!current.authoringDraft && current.nodes.length) {
    const { nodes, edges, tables, graphTrail, verificationSource, configName, selectedNodeId, projectId } = current;
    useAppStore.setState({ authoringDraft: { nodes, edges, tables, graphTrail, verificationSource, configName, selectedNodeId, projectId } });
  }
  loadProject(config);
  useAppStore.setState({ projectId: crypto.randomUUID(), graphTrail: [], verificationSource: null, resultsDraft: null, liveRtp: null, liveProvenance: null });
}
export function loadCoinExample() {
  switchProject({ schemaVersion: '1.0.0', name: 'REF-A Coin', nodes: [
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
