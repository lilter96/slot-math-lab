import { createNineLivesGraph } from '../games/nine-lives/graph';
import { canonicalJson } from './hash';
import type { ModelGraph } from './modelProjection';
interface PayEntry { symbolId: string; counts: number[]; payouts: string[]; [key: string]: unknown }
interface Paytable { id: string; entries: PayEntry[]; [key: string]: unknown }
const reference = createNineLivesGraph() as ModelGraph;
export function clusterPayoutCapability(graph: ModelGraph): Paytable | null {
  if (!String(graph.id ?? '').startsWith('nine-lives')) return null;
  const state = graph.initialState as Record<string, unknown> | undefined;
  const tables = graph.paytables as Paytable[] | undefined;
  const table = Array.isArray(tables) ? tables.find(item => item.id === 'nine-lives-paytable') : undefined;
  if (!state || state.baseCost !== 20 || !Array.isArray(state.clusterPaytable) || state.clusterPaytable.length !== 130 || !Array.isArray(table?.entries) || table.entries.length !== 5) return null;
  if (canonicalJson((graph.expressions as Record<string, unknown> | undefined)?.credits) !== canonicalJson((reference.expressions as Record<string, unknown>).credits)) return null;
  for (const id of ['connected-component', 'mask-component']) {
    const definition = graph.mechanics?.[id];
    const expression = (definition?.expressions as Record<string, unknown> | undefined)?.pay;
    if (canonicalJson(expression) !== canonicalJson((reference.mechanics![id].expressions as Record<string, unknown>).pay) || !definition?.nodes.some(node => node.id === 'pay' && node.nodeType === 'modifyState' && node.outputKey === 'componentPay' && node.expressionId === 'pay')) return null;
  }
  for (let symbol = 0; symbol < 5; symbol++) {
    const entry = table.entries[symbol];
    if (entry.symbolId !== String(symbol + 1) || entry.counts?.length !== 26 || entry.payouts?.length !== 26) return null;
    for (let offset = 0; offset < 26; offset++) {
      const coin = state.clusterPaytable[symbol * 26 + offset];
      if (entry.counts[offset] !== offset + 5 || typeof coin !== 'string' || !/^\d+$/.test(coin) || !Number.isSafeInteger(Number(coin)) || Number(coin) > 2147483647 || Number(entry.payouts[offset]) !== Number(coin) / 20) return null;
    }
  }
  return table;
}
export function changeClusterPayouts(graph: ModelGraph, symbolId: string, values: string[]): ModelGraph {
  const table = clusterPayoutCapability(graph);
  if (!table) throw new Error('The executable cluster lookup and metadata do not match this editor. Use the graph.');
  const symbolIndex = table.entries.findIndex(entry => entry.symbolId === symbolId);
  if (symbolIndex < 0 || values.length !== 26 || values.some(value => !/^\d+$/.test(value) || !Number.isSafeInteger(Number(value)) || Number(value) > 2147483647)) throw new Error('Use 26 non-negative whole coin amounts no greater than 2,147,483,647.');
  const state = graph.initialState as Record<string, unknown>;
  const coins = (state.clusterPaytable as string[]).map((value, index) => Math.floor(index / 26) === symbolIndex ? String(Number(values[index % 26])) : value);
  const paytables = (graph.paytables as Paytable[]).map(item => item === table ? { ...item, entries: item.entries.map((entry, index) => index === symbolIndex ? { ...entry, payouts: values.map(value => String(Number(value) / 20)) } : entry) } : item);
  return { ...graph, initialState: { ...state, clusterPaytable: coins }, paytables };
}
