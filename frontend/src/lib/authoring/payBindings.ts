import { payline, linePayTable } from '../../games/shared/graph/mechanics/paylines';
import { scatterPay, scatterPayTable } from '../../games/shared/graph/mechanics/scatter';
import { canonicalJson } from '../hash';
import type { ModelGraph } from '../modelProjection';
import { reelObject } from '../reelCommands';
import { exactInteger, normalizedCredits } from './exactAmounts';
import { matchReelShape } from './reelShape';
import { indexGraphSources, type NodeSource, type SourcePath } from './sourceIndex';
import { activePayoutCap, computedLineBound, type ActivePayoutCap, type ComputedLineBound } from './payoutBounds';

interface PayEntry { symbolId: string; counts: number[]; payouts: string[]; [key: string]: unknown }
interface Paytable { id: string; entries: PayEntry[]; [key: string]: unknown }
export interface PayBinding {
  version: 1;
  key: string;
  revision: string;
  kind: 'line' | 'scatter';
  tableId: string;
  unit: 'line credits' | 'total-bet multiples';
  roundCost: number;
  scale: number;
  columns: number;
  wild: number | null;
  derivedWild: boolean;
  rows: { symbolId: string; counts: number[]; values: string[] }[];
  scopes: string[];
  uses: NodeSource[];
  reads: SourcePath[];
  writes: SourcePath[];
  maximum: number;
  bound: ComputedLineBound | null;
  cap: ActivePayoutCap | null;
}
function tables(graph: ModelGraph): Paytable[] {
  return Array.isArray(graph.paytables) ? (graph.paytables as Paytable[]).filter(table => typeof table.id === 'string' && Array.isArray(table.entries)
    && table.entries.length > 0 && new Set(table.entries.map(entry => entry.symbolId)).size === table.entries.length
    && table.entries.every(entry => typeof entry.symbolId === 'string' && Array.isArray(entry.counts) && Array.isArray(entry.payouts) && entry.counts.length === entry.payouts.length
      && new Set(entry.counts).size === entry.counts.length && entry.counts.every(count => Number.isSafeInteger(count) && count >= 0) && entry.payouts.every(pay => typeof pay === 'string'))) : [];
}
/** Payout metadata is admitted only when the runtime helper and every exact lookup value agree. */
export function payBindings(graph: ModelGraph): PayBinding[] {
  const state = reelObject(graph.initialState), board = reelObject(graph.boardConfig), cost = Number(state?.roundCost), bet = Number(state?.totalBet);
  const symbols = Array.isArray(graph.symbols) ? graph.symbols as { id: string; kind: string }[] : [];
  const columns = Number(board?.columns), rows = Number(board?.rows), cells = columns * rows;
  if (!state || !Number.isSafeInteger(cost) || cost <= 0 || !Number.isSafeInteger(columns) || columns < 1 || !Number.isSafeInteger(rows) || rows < 1 || cells > 64) return [];
  const index = indexGraphSources(graph), definitions = graph.mechanics ?? {}, bindings: PayBinding[] = [], bound = computedLineBound(graph), cap = activePayoutCap(graph);
  if (index.stateWrites.some(write => ['linePays', 'scatterPays', 'wildSymbol', 'roundCost', 'totalBet'].includes(write.key))) return [];
  const candidates = tables(graph);
  for (const kind of ['line', 'scatter'] as const) {
    const lookup = kind === 'line' ? state.linePays : state.scatterPays;
    if (!Array.isArray(lookup)) continue;
    if (kind === 'scatter' && (!Number.isSafeInteger(bet) || bet <= 0)) continue;
    const template = (kind === 'line' ? payline(columns) : scatterPay()) as unknown as ModelGraph;
    const scopes = Object.entries(definitions).filter(([, definition]) => matchReelShape(definition, template)).map(([scope]) => scope);
    if (!scopes.length) continue;
    const uses = index.libraryUses.filter(use => scopes.includes(use.definition)).map(use => use.source);
    const wild = kind === 'line' ? Number(state.wildSymbol) : null;
    if (kind === 'line' && (!Number.isSafeInteger(wild) || wild! < 0 || symbols.some((symbol, position) => symbol.id !== String(position)) || !symbols.length || wild! >= symbols.length)) continue;
    for (const table of candidates) {
      if (candidates.filter(candidate => candidate.id === table.id).length !== 1) continue;
      try {
        const values = table.entries.map(entry => entry.payouts.map(payout => exactInteger(payout, cost)));
        if (table.entries.some(entry => entry.counts.some(count => count > (kind === 'line' ? columns : cells)))) continue;
        const pays = Object.fromEntries(table.entries.map((entry, row) => [entry.symbolId, Object.fromEntries(entry.counts.map((count, position) => [count, values[row][position]]))]));
        let expected: unknown, scale = 1;
        if (kind === 'line') {
          if (table.entries.some(entry => !symbols.some(symbol => symbol.id === entry.symbolId))) continue;
          expected = linePayTable({ symbolCount: symbols.length, wild: wild!, pays, reels: columns });
        } else {
          if (table.entries.length !== 1 || !symbols.some(symbol => symbol.id === table.entries[0].symbolId && symbol.kind === 'Scatter')) continue;
          if (!uses.length || uses.some(use => reelObject(use.node.parameters)?.symbol !== table.entries[0].symbolId)) continue;
          if (values[0].some(credits => credits % bet !== 0)) continue;
          scale = bet;
          expected = scatterPayTable(Object.fromEntries(table.entries[0].counts.map((count, position) => [count, values[0][position] / bet])), cells);
        }
        if (canonicalJson(expected) !== canonicalJson(lookup)) continue;
        const tablePosition = (graph.paytables as Paytable[]).findIndex(candidate => candidate.id === table.id);
        bindings.push({ version: 1, key: JSON.stringify([kind, table.id]), revision: canonicalJson({ board, symbols, table, lookup, cost, bet, wild, cap, scopes: scopes.map(scope => [scope, definitions[scope]]), uses: uses.map(use => [use.key, use.node]), boundDependencies: bound ? { nodes: graph.nodes, edges: graph.edges, mechanics: graph.mechanics, initialState: graph.initialState } : null }),
          kind, tableId: table.id, unit: kind === 'line' ? 'line credits' : 'total-bet multiples', roundCost: cost, scale, columns: kind === 'line' ? columns : cells,
          wild, derivedWild: kind === 'line' && !table.entries.some(entry => entry.symbolId === String(wild)), rows: table.entries.map((entry, row) => ({ symbolId: entry.symbolId, counts: [...entry.counts], values: values[row].map(value => String(value / scale)) })),
          scopes, uses, reads: [['boardConfig'], ['symbols'], ['paytables', tablePosition], ['initialState', kind === 'line' ? 'linePays' : 'scatterPays'], ['initialState', 'roundCost'], ['initialState', 'totalBet'], ...(kind === 'line' ? [['initialState', 'wildSymbol']] : []), ...scopes.map(scope => ['mechanics', scope]), ...uses.map(use => use.path), ...(bound ? [['nodes'], ['edges'], ['mechanics'], ['initialState']] : [])],
          writes: [['paytables', tablePosition, 'entries'], ['initialState', kind === 'line' ? 'linePays' : 'scatterPays'], ...(bound ? [['nodes', graph.nodes!.findIndex(node => node.id === bound.sinkId), 'winCap']] : [])], maximum: values.flat().reduce((maximum, value) => Math.max(maximum, value), 0) / scale, bound, cap });
      } catch { /* Non-integral/unsupported exact units stay outside this proven lookup adapter. */ }
    }
  }
  return bindings;
}

export function changeBoundPayout(graph: ModelGraph, binding: PayBinding, symbolId: string, count: number, value: string, capPolicy?: 'keep' | 'recompute'): ModelGraph {
  return changeBoundPayouts(graph, binding, [{ symbolId, count, value }], capPolicy);
}
export interface PayoutCellEdit { symbolId: string; count: number; value: string }
export function changeBoundPayouts(graph: ModelGraph, binding: PayBinding, edits: PayoutCellEdit[], capPolicy?: 'keep' | 'recompute'): ModelGraph {
  const current = payBindings(graph).find(item => item.key === binding.key);
  if (!current || current.revision !== binding.revision) throw new Error('The payout source, precision table or execution consumer changed. Reconcile this draft.');
  const identities = new Set<string>();
  const bound = computedLineBound(graph);
  if (capPolicy === 'recompute' && !bound) throw new Error('No proven computed upper-bound contract is available. Preserve the active cap or inspect its source.');
  const values = edits.map(edit => {
    const row = current.rows.find(row => row.symbolId === edit.symbolId), position = row?.counts.indexOf(edit.count) ?? -1;
    if (!row || position < 0) throw new Error('This cell is not an authored paying size. A size/eligibility edit needs the dependency transformation.');
    const key = JSON.stringify([edit.symbolId, edit.count]); if (identities.has(key)) throw new Error('A payout cell occurs twice in this batch.'); identities.add(key);
    const unitValue = exactInteger(edit.value), credits = unitValue * current.scale;
    if (!Number.isSafeInteger(credits)) throw new Error('The scaled credits exceed the exact browser integer range.');
    if (unitValue > current.maximum && !bound && !(capPolicy === 'keep' && current.cap)) throw new Error('This change increases the payout bound. Reconcile the computed-bound or game-rule-cap contract before applying.');
    return { ...edit, position, unitValue, credits, changed: unitValue !== Number(row.values[position]) };
  });
  if (!values.some(value => value.changed)) return structuredClone(graph);
  const next = structuredClone(graph), table = (next.paytables as Paytable[]).find(table => table.id === current.tableId)!;
  for (const value of values.filter(value => value.changed)) table.entries.find(entry => entry.symbolId === value.symbolId)!.payouts[value.position] = normalizedCredits(value.credits, current.roundCost);
  const state = next.initialState as Record<string, unknown>;
  if (current.kind === 'line') {
    const pays = Object.fromEntries(table.entries.map(entry => [entry.symbolId, Object.fromEntries(entry.counts.map((count, position) => [count, exactInteger(entry.payouts[position], current.roundCost)]))]));
    state.linePays = linePayTable({ symbolCount: (next.symbols as unknown[]).length, wild: current.wild!, pays, reels: current.columns });
  } else for (const value of values) (state.scatterPays as number[])[value.count] = value.unitValue;
  if (bound && capPolicy !== 'keep') {
    const updated = computedLineBound(next, false); if (!updated) throw new Error('The derived payout bound could not be reconciled. The applied graph is unchanged.');
    next.nodes = next.nodes!.map(node => node.id === bound.sinkId ? { ...node, winCap: updated.cap } : node);
  }
  return next;
}
