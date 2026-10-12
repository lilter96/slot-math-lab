import { canonicalJson } from '../hash';
import { validateWeights } from '../modelCommands';
import type { ModelGraph, ModelNode } from '../modelProjection';

const fields = ['nodeType', 'inputs', 'outputs', 'stateWriteKey', 'boardStateKey', 'stateKey', 'drawWeights', 'weightExpressionId', 'outputKey', 'expressionId', 'mechanicName', 'parameters', 'conditionId', 'stopConditionId', 'maxIterations', 'winStateKey', 'winCap', 'pluginId'];
const references = ['expressionId', 'conditionId', 'stopConditionId', 'weightExpressionId'];
export interface ReelShape { nodes: Map<string, ModelNode>; libraries: Map<string, string> }

/** Alpha-renamed helper contract. Only stop weights may vary; topology, AST and payloads must match. */
export function matchReelShape(actual: ModelGraph, expected: ModelGraph): ReelShape | null {
  const nodes = actual.nodes ?? [], template = expected.nodes ?? [];
  if (nodes.length !== template.length || new Set(nodes.map(node => node.id)).size !== nodes.length) return null;
  const ids = new Map(template.map((node, index) => [node.id, nodes[index].id]));
  const expressions = new Map<string, string>(), libraries = new Map<string, string>();
  const remember = (map: Map<string, string>, key: unknown, value: unknown) => {
    if (typeof key !== 'string' || typeof value !== 'string' || (map.has(key) && map.get(key) !== value)) return false;
    map.set(key, value); return true;
  };
  for (let index = 0; index < template.length; index++) {
    const from = template[index], to = nodes[index];
    for (const field of references) if (from[field] !== undefined && !remember(expressions, from[field], to[field])) return null;
    if (from.mechanicName !== undefined && !remember(libraries, from.mechanicName, to.mechanicName)) return null;
    const projected = Object.fromEntries(fields.filter(field => from[field] !== undefined).map(field => [field, from[field]]));
    for (const field of references) if (projected[field] !== undefined) projected[field] = expressions.get(String(projected[field]));
    if (projected.mechanicName !== undefined) projected.mechanicName = libraries.get(String(projected.mechanicName));
    if (from.nodeType === 'draw') {
      const rows = to.drawWeights, expectedRows = from.drawWeights;
      if (to.weightExpressionId || !Array.isArray(rows) || !Array.isArray(expectedRows) || rows.length !== expectedRows.length) return null;
      if (rows.some((row, position) => row.outcomeId !== expectedRows[position].outcomeId || row.value !== expectedRows[position].value || !Number.isSafeInteger(row.weight) || row.weight < 0)) return null;
      if (validateWeights(rows.map(row => String(row.weight)))) return null;
      projected.drawWeights = rows.map(row => ({ outcomeId: row.outcomeId, weight: row.weight, value: row.value }));
    }
    const own = Object.fromEntries(fields.filter(field => to[field] !== undefined).map(field => [field, to[field]]));
    if (Array.isArray(own.drawWeights)) own.drawWeights = own.drawWeights.map(row => ({ outcomeId: row.outcomeId, weight: row.weight, value: row.value }));
    if (canonicalJson(own) !== canonicalJson(projected)) return null;
  }
  const edges = (value: ModelGraph, rename = false) => (Array.isArray(value.edges) ? value.edges as Record<string, unknown>[] : []).map(edge => ({ source: rename ? ids.get(String(edge.sourceNodeId)) : edge.sourceNodeId,
    sourcePort: edge.sourcePort, target: rename ? ids.get(String(edge.targetNodeId)) : edge.targetNodeId, targetPort: edge.targetPort }));
  if (canonicalJson(edges(actual)) !== canonicalJson(edges(expected, true))) return null;
  const ast = Object.fromEntries(Object.entries(expected.expressions ?? {}).map(([id, value]) => [expressions.get(id) ?? id, value]));
  if (canonicalJson(actual.expressions ?? {}) !== canonicalJson(ast)) return null;
  if (actual.initialState && canonicalJson(actual.initialState) !== canonicalJson(expected.initialState ?? {})) return null;
  if (Array.isArray(actual.plugins) && actual.plugins.length && canonicalJson(actual.plugins) !== canonicalJson(expected.plugins ?? [])) return null;
  return { nodes: new Map(template.map((node, index) => [node.id, nodes[index]])), libraries };
}
