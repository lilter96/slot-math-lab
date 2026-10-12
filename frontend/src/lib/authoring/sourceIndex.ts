import type { ModelGraph, ModelNode } from '../modelProjection';

export type SourcePath = readonly (string | number)[];
export interface NodeSource {
  key: string;
  scope: string | null;
  id: string;
  path: SourcePath;
  node: Readonly<ModelNode>;
}
export interface ExpressionSource { key: string; scope: string | null; id: string; path: SourcePath; value: unknown }
export interface StateRead { path: SourcePath; statePath: SourcePath }
export interface LibraryUse { source: NodeSource; definition: string; present: boolean }
export interface GraphSourceIndex {
  nodes: NodeSource[];
  expressions: ExpressionSource[];
  stateReads: StateRead[];
  stateWrites: { source: NodeSource; key: string }[];
  libraryUses: LibraryUse[];
  issues: { path: SourcePath; message: string }[];
}
export const sourceKey = (scope: string | null, id: string) => JSON.stringify([scope, id]);
const object = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);

/** A read-only structural inventory, not a recognizer/evaluator for named games. */
export function indexGraphSources(graph: ModelGraph): GraphSourceIndex {
  const index: GraphSourceIndex = { nodes: [], expressions: [], stateReads: [], stateWrites: [], libraryUses: [], issues: [] };
  const definitions = graph.mechanics ?? {};
  function reads(value: unknown, path: SourcePath) {
    if (Array.isArray(value)) { value.forEach((child, position) => reads(child, [...path, position])); return; }
    if (!object(value)) return;
    if (value.exprType === 'fieldAccess' && value.target === 'state' && Array.isArray(value.path) && value.path.every(part => typeof part === 'string' || Number.isSafeInteger(part))) {
      index.stateReads.push({ path, statePath: [...value.path] });
    }
    for (const [key, child] of Object.entries(value)) reads(child, [...path, key]);
  }
  function scan(value: Record<string, unknown>, scope: string | null, prefix: SourcePath) {
    const nodes = value.nodes;
    if (!Array.isArray(nodes)) { index.issues.push({ path: [...prefix, 'nodes'], message: 'This scope has no node array.' }); return; }
    const ids = new Set<string>();
    nodes.forEach((node, position) => {
      const path = [...prefix, 'nodes', position];
      if (!object(node) || typeof node.id !== 'string' || typeof node.nodeType !== 'string') { index.issues.push({ path, message: 'This node needs authored string identity and type.' }); return; }
      if (ids.has(node.id)) index.issues.push({ path, message: 'Duplicate node identity in this scope; editing needs an unambiguous source.' });
      ids.add(node.id);
      const source: NodeSource = { key: sourceKey(scope, node.id), scope, id: node.id, path, node: node as ModelNode };
      index.nodes.push(source);
      if (node.nodeType === 'library' && typeof node.mechanicName === 'string') index.libraryUses.push({ source, definition: node.mechanicName, present: Object.hasOwn(definitions, node.mechanicName) });
      const writeKey = node.nodeType === 'draw' ? node.stateWriteKey : ['putState', 'modifyState'].includes(node.nodeType) ? node.outputKey : undefined;
      if (typeof writeKey === 'string') index.stateWrites.push({ source, key: writeKey });
      reads(node, path);
    });
    if (object(value.expressions)) for (const [id, expression] of Object.entries(value.expressions)) {
      const path = [...prefix, 'expressions', id];
      index.expressions.push({ key: sourceKey(scope, id), scope, id, path, value: expression }); reads(expression, path);
    }
  }
  scan(graph, null, []);
  for (const [scope, definition] of Object.entries(definitions)) scan(definition, scope, ['mechanics', scope]);
  return index;
}

/** Resolve current locations by authored identity; an old array offset is never an edit target. */
export function resolveNodeSource(graph: ModelGraph, key: string): NodeSource {
  const matches = indexGraphSources(graph).nodes.filter(node => node.key === key);
  if (matches.length !== 1) throw new Error('The source is missing or ambiguous. Rebuild its binding before editing.');
  return matches[0];
}
