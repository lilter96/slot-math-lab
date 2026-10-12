import { assertModelNumbers } from './modelJson';

/** Check the response envelope before the shared canonical loader can mutate a workspace. */
export function generatedModel(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('The server did not return a graph model.');
  const config = value as Record<string, unknown>;
  assertModelNumbers(config);
  if (typeof config.schemaVersion !== 'string' || !Array.isArray(config.nodes) || !config.nodes.length || !Array.isArray(config.edges)) throw new Error('Generated model needs schemaVersion, nodes and edges.');
  const ids = new Set<string>();
  for (const node of config.nodes) {
    if (!node || typeof node !== 'object' || typeof node.id !== 'string' || !node.id || typeof node.nodeType !== 'string' || !node.nodeType || ids.has(node.id)) throw new Error('Generated model has invalid or duplicate node identities.');
    ids.add(node.id);
  }
  const edges = new Set<string>();
  for (const edge of config.edges) {
    if (!edge || typeof edge !== 'object' || typeof edge.id !== 'string' || !edge.id || edges.has(edge.id) || !ids.has(edge.sourceNodeId) || !ids.has(edge.targetNodeId) || typeof edge.sourcePort !== 'string' || typeof edge.targetPort !== 'string') throw new Error('Generated model has invalid edge identities or ports.');
    edges.add(edge.id);
  }
  return config;
}
