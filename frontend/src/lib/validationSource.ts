import type { ModelGraph } from './modelProjection';

export interface ValidationSource { mechanics: string[]; id: string }
/** Resolve the existing generic inliner namespace (library-node ID + '/').
 * Ambiguous authored IDs remain unresolved rather than choosing a wrong node. */
export function validationSource(graph: ModelGraph, id: string, kind: 'nodes' | 'edges'): ValidationSource | null {
  const found: ValidationSource[] = [];
  const visit = (scope: ModelGraph, remaining: string, path: string[], visited: string[]) => {
    const entries = scope[kind] as { id: string }[] | undefined;
    if (entries?.some(entry => entry.id === remaining)) found.push({ mechanics: path, id: remaining });
    for (const node of scope.nodes ?? []) {
      const definition = node.mechanicName && graph.mechanics?.[node.mechanicName];
      const prefix = node.id + '/';
      if (!definition || !remaining.startsWith(prefix) || visited.includes(node.mechanicName!)) continue;
      visit(definition, remaining.slice(prefix.length), [...path, node.mechanicName!], [...visited, node.mechanicName!]);
    }
  };
  visit(graph, id, [], []);
  return found.length === 1 ? found[0] : null;
}
