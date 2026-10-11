type Port = { name: string; type: string };
type Node = { id: string; nodeType: string; inputs?: Record<string, Port>; outputs?: Record<string, Port>; [key: string]: unknown };
type Edge = { id: string; sourceNodeId: string; sourcePort: string; targetNodeId: string; targetPort: string };
type Definition = { name: string; nodes: Node[]; edges: Edge[]; expressions: Record<string, unknown> };
export type ComposableGraph = Record<string, unknown> & { nodes: Node[]; edges: Edge[]; mechanics?: Record<string, Definition>; expressions?: Record<string, unknown> };

/** Move selected atoms into an editable definition and replace them with one reference. */
export function extractSubgraph(graph: ComposableGraph, name: string, selected: readonly string[], instanceId: string): ComposableGraph {
  name = name.trim();
  if (!name) throw new Error('Give the subgraph a name.');
  if (Object.hasOwn(graph.mechanics ?? {}, name)) throw new Error('That subgraph name is already used. Open its definition to edit it.');
  if (graph.nodes.some(node => node.id === instanceId)) throw new Error('The new instance must have a unique ID.');
  const ids = new Set(selected);
  const nodes = graph.nodes.filter(node => ids.has(node.id));
  if (!nodes.length || nodes.length !== ids.size) throw new Error('Select the atoms to extract first.');
  if (nodes.some(node => node.nodeType === 'metricsSink')) throw new Error('Keep the round payout sink in the full game graph.');
  const edges = graph.edges.filter(edge => ids.has(edge.sourceNodeId) && ids.has(edge.targetNodeId));
  const inputs: Record<string, Port> = {}, outputs: Record<string, Port> = {};
  for (const node of nodes) {
    for (const [port, contract] of Object.entries(node.inputs ?? {})) {
      if (edges.some(edge => edge.targetNodeId === node.id && edge.targetPort === port)) continue;
      if (Object.hasOwn(inputs, port)) throw new Error(`Several inputs are called "${port}". Include the connecting atoms or give boundary ports distinct names.`);
      inputs[port] = contract;
    }
    for (const [port, contract] of Object.entries(node.outputs ?? {})) {
      if (edges.some(edge => edge.sourceNodeId === node.id && edge.sourcePort === port)) continue;
      if (Object.hasOwn(outputs, port)) throw new Error(`Several outputs are called "${port}". Include the branch join or give boundary ports distinct names.`);
      outputs[port] = contract;
    }
  }
  if (!Object.keys(outputs).length) throw new Error('The subgraph needs an output to continue the round.');
  const expressions: Record<string, unknown> = {};
  for (const node of nodes) {
    for (const property of ['expressionId', 'conditionId', 'stopConditionId', 'weightExpressionId']) {
      const id = node[property];
      if (typeof id === 'string' && Object.hasOwn(graph.expressions ?? {}, id)) expressions[id] = graph.expressions![id];
    }
  }
  const instance: Node = { nodeType: 'library', id: instanceId, label: name, mechanicName: name, parameters: {}, inputs, outputs };
  const first = graph.nodes.findIndex(node => ids.has(node.id));
  return {
    ...graph,
    nodes: graph.nodes.flatMap((node, index) => index === first ? [instance] : ids.has(node.id) ? [] : [node]),
    edges: graph.edges.filter(edge => !ids.has(edge.sourceNodeId) || !ids.has(edge.targetNodeId)).map(edge => ({ ...edge,
      ...(ids.has(edge.sourceNodeId) ? { sourceNodeId: instanceId } : {}),
      ...(ids.has(edge.targetNodeId) ? { targetNodeId: instanceId } : {}),
    })),
    mechanics: { ...graph.mechanics, [name]: { name, nodes, edges, expressions } },
  };
}
