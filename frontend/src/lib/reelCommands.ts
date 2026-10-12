import { Mechanics } from '../games/shared/graph/flow';
import { reelState, spinReels, type ReelSet } from '../games/shared/graph/mechanics/reels';
import { canonicalJson } from './hash';
import type { ModelGraph, ModelNode } from './modelProjection';
import { applyReelCondition, findReelCondition, type ReelCondition } from './reelConditioning';

interface StopSource { scope: string; nodeId: string; reel: number }
export interface ReelSource { key: string; label: string; rows: number; stripKeys: string[]; strips: string[][]; stops: StopSource[]; conditions?: ReelCondition[] }
type Value = Record<string, unknown>;
export const reelObject = (value: unknown): Value | null => value && typeof value === 'object' && !Array.isArray(value) ? value as Value : null;
const object = reelObject;
function fields(expression: unknown): string[] {
  const value = object(expression);
  if (!value) return Array.isArray(expression) ? expression.flatMap(fields) : [];
  if (value.exprType === 'fieldAccess' && value.target === 'state' && Array.isArray(value.path) && value.path.length === 1) return [String(value.path[0])];
  return Object.values(value).flatMap(fields);
}
const nodeKeys = ['id', 'nodeType', 'inputs', 'outputs', 'stateWriteKey', 'boardStateKey', 'stateKey', 'drawWeights', 'weightExpressionId', 'outputKey', 'expressionId', 'mechanicName', 'parameters', 'conditionId', 'stopConditionId', 'maxIterations', 'winStateKey', 'winCap', 'pluginId'];
export function sameReelScope(actual: ModelGraph, expected: ModelGraph): boolean {
  if (actual.initialState && canonicalJson(actual.initialState) !== canonicalJson(expected.initialState ?? {})) return false;
  if (Array.isArray(actual.plugins) && actual.plugins.length && canonicalJson(actual.plugins) !== canonicalJson(expected.plugins ?? [])) return false;
  const project = (nodes: ModelNode[] = []) => nodes.map(node => Object.fromEntries(nodeKeys.filter(key => node[key] !== undefined).map(key => [key, node[key]])));
  return canonicalJson(project(actual.nodes)) === canonicalJson(project(expected.nodes))
    && canonicalJson(actual.edges) === canonicalJson(expected.edges)
    && canonicalJson(actual.expressions) === canonicalJson(expected.expressions);
}
const sameScope = sameReelScope;

/** Adapt the existing generic authoring shape, never a game ID or kernel dispatch. */
export function reelSources(graph: ModelGraph): ReelSource[] {
  const state = object(graph.initialState), board = object(graph.boardConfig);
  const rows = Number(board?.rows), columns = Number(board?.columns);
  if (!state || !Number.isInteger(rows) || rows < 1 || !Number.isInteger(columns) || columns < 1 || rows * columns > 64) return [];
  const groups = new Map<string, ReelSource>();
  for (const [scope, definition] of Object.entries(graph.mechanics ?? {})) {
    const window = definition.nodes.find(node => node.nodeType === 'modifyState' && node.outputKey === 'board');
    const expression = window && object(definition.expressions)?.[String(window.expressionId)];
    const keys = [...new Set(fields(expression))].filter(key => Array.isArray(state[key]) && /\d+$/.test(key));
    if (keys.length !== columns) continue;
    const prefix = keys[0].replace(/\d+$/, '');
    const stripKeys = Array.from({ length: columns }, (_, index) => `${prefix}${index}`);
    if (keys.some(key => !stripKeys.includes(key))) continue;
    const stored = stripKeys.map(key => state[key]);
    if (stored.some(value => !Array.isArray(value) || value.length < rows || value.some(item => typeof item !== 'string'))) continue;
    const strips = (stored as string[][]).map(strip => strip.slice(0, strip.length - rows + 1));
    const spec: ReelSet = { name: scope, stripKey: prefix, strips, rows };
    const wrapped = reelState(spec);
    if (stripKeys.some(key => canonicalJson(wrapped[key]) !== canonicalJson(state[key]))) continue;
    const stops: StopSource[] = [];
    let matched = false;
    for (const shared of [false, true]) {
      const mechanics = shared ? new Mechanics() : undefined;
      const expected = spinReels(spec, mechanics) as unknown as ModelGraph;
      if (!sameScope(definition, expected)) continue;
      if (mechanics) {
        const definitions = mechanics.all();
        if (Object.entries(definitions).some(([name, stop]) => !graph.mechanics?.[name] || !sameScope(graph.mechanics[name], stop as unknown as ModelGraph))) continue;
        Object.entries(definitions).forEach(([name, stop], reel) => stops.push({ scope: name, nodeId: stop.nodes[0].id, reel }));
      } else definition.nodes.filter(node => node.nodeType === 'draw').forEach((node, reel) => stops.push({ scope, nodeId: node.id, reel }));
      matched = true; break;
    }
    const conditional = definition.nodes.some(node => node.nodeType === 'draw' && node.stateWriteKey === 'showPattern');
    if (!matched && !conditional) continue;
    const key = JSON.stringify(stripKeys), existing = groups.get(key);
    if (existing) existing.stops.push(...stops);
    else groups.set(key, { key, label: prefix, rows, stripKeys, strips, stops });
  }
  const conditionalScopes = Object.entries(graph.mechanics ?? {}).filter(([, definition]) => definition.nodes.some(node => node.nodeType === 'draw' && node.stateWriteKey === 'showPattern'));
  // The existing generic helper shares one showPatterns table. Multiple independent consumers need a separate mapping.
  if (conditionalScopes.length > 1) return [];
  for (const source of groups.values()) {
    for (const [scope, definition] of conditionalScopes) {
      const condition = findReelCondition(graph, source, scope, sameScope);
      if (!condition) continue;
      source.conditions = [...source.conditions ?? [], condition];
      definition.nodes.filter(node => node.nodeType === 'draw' && /^stop\d+$/.test(String(node.stateWriteKey))).forEach(node => source.stops.push({ scope, nodeId: node.id, reel: Number(String(node.stateWriteKey).slice(4)) }));
    }
  }
  if (conditionalScopes.length && ![...groups.values()].some(source => source.conditions?.length)) return [];
  if (conditionalScopes.length) {
    const reference = (value: unknown): boolean => {
      const expression = object(value);
      if (!expression) return Array.isArray(value) && value.some(reference);
      if (expression.exprType === 'fieldAccess' && expression.target === 'state' && Array.isArray(expression.path) && ['showPatterns', 'showPattern'].includes(String(expression.path[0]))) return true;
      return Object.values(expression).some(reference);
    };
    if (reference(graph.expressions) || Object.entries(graph.mechanics ?? {}).some(([scope, definition]) => !conditionalScopes.some(([name]) => name === scope) && reference(definition.expressions))) return [];
  }
  const allStops = new Set([...groups.values()].flatMap(source => source.stops.map(stop => JSON.stringify([stop.scope, stop.nodeId]))));
  // A conditioned or customized stop source is coupled to the same state; don't rewrite only one domain.
  const unowned = [graph.nodes ?? [], ...Object.entries(graph.mechanics ?? {}).map(([scope, definition]) => definition.nodes.filter(node => !allStops.has(JSON.stringify([scope, node.id]))))]
    .flat().filter(node => node.nodeType === 'draw').map(node => String(node.stateWriteKey));
  return [...groups.values()].filter(source => source.stops.length > 0 && !source.strips.some((_, reel) => unowned.includes(`stop${reel}`)));
}

export function validateReels(graph: ModelGraph, strips: string[][]): string | null {
  const symbols = Array.isArray(graph.symbols) ? graph.symbols as { id: string }[] : [];
  const known = new Set(symbols.map(symbol => String(symbol.id)));
  if (!known.size) return 'No authored symbol dictionary is available for this strip mapping.';
  if (strips.some(strip => !strip.length)) return 'Every reel needs at least one symbol.';
  if (strips.some(strip => strip.length > 4096)) return 'This editor supports at most 4,096 stops per reel.';
  if (strips.some(strip => strip.some(symbol => !known.has(symbol)))) return 'A strip contains an unknown symbol ID. Choose a symbol from this model.';
  return null;
}

export function changeReels(graph: ModelGraph, sourceKey: string, strips: string[][]): ModelGraph {
  const source = reelSources(graph).find(item => item.key === sourceKey);
  if (!source) throw new Error('No proven reel/stop mapping. Inspect the source graph before editing.');
  if (strips.length !== source.strips.length) throw new Error('Keep the authored reel count unchanged.');
  const invalid = validateReels(graph, strips); if (invalid) throw new Error(invalid);
  const next = structuredClone(graph), state = next.initialState as Value;
  for (let reel = 0; reel < strips.length; reel++) {
    const strip = strips[reel];
    state[source.stripKeys[reel]] = Array.from({ length: strip.length + source.rows - 1 }, (_, index) => strip[index % strip.length]);
  }
  for (const stop of source.stops) {
    if (source.conditions?.some(condition => condition.scope === stop.scope)) continue;
    const node = next.mechanics![stop.scope].nodes.find(node => node.id === stop.nodeId)!;
    const previous = node.drawWeights as Value[];
    node.drawWeights = strips[stop.reel].map((_, index) => ({ ...previous.find(row => row.outcomeId === String(index)), outcomeId: String(index), weight: 1, value: 0 }));
  }
  for (const condition of source.conditions ?? []) applyReelCondition(next, source, condition, strips);
  return next;
}
