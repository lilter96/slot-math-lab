import type { ModelGraph, ModelNode } from './modelProjection';
export interface DrawRow { outcomeId: string; weight: number; value: number; [key: string]: unknown }
export interface DrawSource { key: string; scope: string | null; nodeId: string; label: string; rows: DrawRow[] }
function validRow(value: unknown): value is DrawRow {
  if (!value || typeof value !== 'object') return false;
  const row = value as DrawRow;
  return typeof row.outcomeId === 'string' && Number.isInteger(row.weight) && row.weight >= 0
    && Number.isInteger(row.value) && row.value >= -2147483648 && row.value <= 2147483647;
}
export function weightedSources(graph: ModelGraph): DrawSource[] {
  const scan = (nodes: ModelNode[], scope: string | null) => nodes.flatMap(node => {
    if (node.nodeType !== 'draw' || node.weightExpressionId || !Array.isArray(node.drawWeights) || !node.drawWeights.length || !node.drawWeights.every(validRow)) return [];
    return [{ key: JSON.stringify([scope, node.id]), scope, nodeId: node.id, label: `${scope ?? 'Full game'} / ${node.label ?? node.id}`, rows: node.drawWeights }];
  });
  return [...scan(graph.nodes ?? [], null), ...Object.entries(graph.mechanics ?? {}).flatMap(([id, definition]) => scan(definition.nodes, id))];
}
export function validateWeights(weights: string[]): string | null {
  if (weights.some(weight => !/^\d+$/.test(weight.trim()) || !Number.isSafeInteger(Number(weight)) || Number(weight) > 2147483647)) return 'Weights must be whole numbers from 0 to 2,147,483,647.';
  const sum = weights.reduce((total, weight) => total + Number(weight), 0);
  if (sum <= 0) return 'At least one outcome needs a positive weight.';
  if (sum > 2147483647) return 'The total weight exceeds the supported integer range.';
  return null;
}
export function changeWeights(graph: ModelGraph, sourceKey: string, weights: string[]): ModelGraph {
  const source = weightedSources(graph).find(item => item.key === sourceKey);
  if (!source) throw new Error('This draw is absent or uses a dynamic weight expression. Open its graph.');
  if (weights.length !== source.rows.length) throw new Error('Outcome count changed. Reopen the table.');
  const error = validateWeights(weights);
  if (error) throw new Error(error);
  const replace = (nodes: ModelNode[]) => nodes.map(node => node.id === source.nodeId
    ? { ...node, drawWeights: source.rows.map((row, index) => ({ ...row, weight: Number(weights[index]) })) } : node);
  if (source.scope == null) return { ...graph, nodes: replace(graph.nodes ?? []) };
  const definition = graph.mechanics![source.scope];
  return { ...graph, mechanics: { ...graph.mechanics, [source.scope]: { ...definition, nodes: replace(definition.nodes) } } };
}
