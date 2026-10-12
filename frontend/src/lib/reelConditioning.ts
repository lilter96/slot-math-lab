import { showingState, showingStops, spinReelsShowing, type ReelSet, type Showing } from '../games/shared/graph/mechanics/reels';
import { canonicalJson } from './hash';
import type { ModelGraph, ModelNode } from './modelProjection';
import type { ReelSource } from './reelCommands';

export interface ReelCondition { scope: string; symbol: string; symbolName: string; atLeast: number }
const symbolKey = '__reelEditorSymbol', thresholdKey = '__reelEditorAtLeast';
type RecordValue = Record<string, unknown>;
const record = (value: unknown): RecordValue => value && typeof value === 'object' && !Array.isArray(value) ? value as RecordValue : {};
function constraints(condition: ReelCondition) {
  return { [symbolKey]: { exprType: 'constant', kind: 'String', value: condition.symbol }, [thresholdKey]: { exprType: 'constant', kind: 'Integer', value: String(condition.atLeast) } };
}

/** Bounded source authoring only; this is not an evaluator or an RNG implementation. */
export function conditionedReels(source: ReelSource, condition: ReelCondition, strips = source.strips) {
  const spec: ReelSet = { name: condition.scope, stripKey: source.stripKeys[0].replace(/\d+$/, ''), strips, rows: source.rows };
  const showing: Showing = { name: condition.scope, symbol: condition.symbol, symbolName: condition.symbolName, atLeast: condition.atLeast };
  let combinations = 1;
  for (const strip of strips) {
    const counts = new Set(strip.map((_, stop) => Array.from({ length: source.rows }, (_, row) => strip[(stop + row) % strip.length]).filter(symbol => symbol === condition.symbol).length));
    combinations *= counts.size;
    if (combinations > 4096) throw new Error('The conditioned strip edit exceeds 4,096 showing-pattern combinations. Keep a smaller mapped source or inspect its graph.');
  }
  let distribution: ReturnType<typeof showingStops>;
  try { distribution = showingStops(spec, showing); }
  catch { throw new Error(`No reel stops satisfy at least ${condition.atLeast} ${condition.symbolName} symbols (ID ${condition.symbol}). Restore the required symbols or reset the unapplied strips.`); }
  const total = distribution.patterns.reduce((sum, pattern) => sum + pattern.weight, 0);
  if (!Number.isSafeInteger(total) || distribution.patterns.some(pattern => !Number.isSafeInteger(pattern.weight) || pattern.weight < 1)) throw new Error('Conditioned draw weights exceed the editor\'s safe exact integer range. No source changes were applied.');
  return { definition: spinReelsShowing(spec, showing) as unknown as ModelGraph, state: showingState(spec, showing), distribution };
}

export function findReelCondition(graph: ModelGraph, source: ReelSource, scope: string, sameScope: (a: ModelGraph, b: ModelGraph) => boolean): ReelCondition | null {
  const actual = graph.mechanics?.[scope]; if (!actual) return null;
  const state = record(graph.initialState), patterns = state.showPatterns;
  if (!Array.isArray(patterns) || !patterns.length || patterns.some(row => !Array.isArray(row) || row.length !== source.strips.length || row.some(count => !Number.isInteger(count) || count < 0 || count > source.rows))) return null;
  const expressions = record(actual.expressions), symbol = record(expressions[symbolKey]), threshold = record(expressions[thresholdKey]);
  const declared = expressions[symbolKey] !== undefined || expressions[thresholdKey] !== undefined;
  const symbols = Array.isArray(graph.symbols) ? graph.symbols as { id: string; name: string }[] : [];
  const upper = Math.min(...patterns.map(row => (row as number[]).reduce((sum, count) => sum + count, 0)));
  const found: ReelCondition[] = [];
  for (const entry of symbols) for (let atLeast = 0; atLeast <= (declared ? source.rows * source.strips.length : upper); atLeast++) {
    const condition = { scope, symbol: String(entry.id), symbolName: entry.name, atLeast };
    if (declared && (canonicalJson(symbol) !== canonicalJson(constraints(condition)[symbolKey]) || canonicalJson(threshold) !== canonicalJson(constraints(condition)[thresholdKey]))) continue;
    try {
      const expected = conditionedReels(source, condition);
      if (declared) expected.definition.expressions = { ...record(expected.definition.expressions), ...constraints(condition) };
      if (sameScope(actual, expected.definition) && canonicalJson(patterns) === canonicalJson(expected.state.showPatterns)) found.push(condition);
    } catch { /* No matching/bounded distribution: keep this source read-only. */ }
  }
  return found.length === 1 ? found[0] : null;
}

export function applyReelCondition(graph: ModelGraph, source: ReelSource, condition: ReelCondition, strips: string[][]): void {
  const generated = conditionedReels(source, condition, strips), actual = graph.mechanics![condition.scope];
  const previous = conditionedReels(source, condition).definition;
  const nextIds = new Set(generated.definition.nodes!.map(node => node.id));
  for (const node of actual.nodes.filter(node => !nextIds.has(node.id))) {
    const original = previous.nodes!.find(item => item.id === node.id)!;
    if (Object.keys(node).some(key => !(key in original)) || node.label !== original.label) throw new Error(`Rebuilding conditioned stops would remove authored metadata on ${node.id}. Edit its source graph instead.`);
  }
  const nodes = generated.definition.nodes!.map(node => {
    const retained = actual.nodes.find(old => old.id === node.id);
    return retained ? { ...retained, ...node, label: retained.label ?? node.label } : node;
  });
  const edges = generated.definition.edges as RecordValue[];
  const retainedEdges = actual.edges as RecordValue[];
  graph.mechanics![condition.scope] = { ...actual, nodes: nodes as ModelNode[], edges: edges.map(edge => ({ ...retainedEdges.find(old => old.id === edge.id), ...edge })), expressions: { ...record(generated.definition.expressions), ...constraints(condition) } };
  (graph.initialState as RecordValue).showPatterns = generated.state.showPatterns;
}
