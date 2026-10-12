import { test, expect } from '@playwright/test';
import { createAncientDragonGraph } from '../src/games/ancient-dragon/graph';
import { createWoodlandWhisperGraph } from '../src/games/woodland-whisper/graph';
import { createX7ClubGraph } from '../src/games/x7-club/graph';
import { parseExactAmount, exactInteger, normalizedCredits } from '../src/lib/authoring/exactAmounts';
import { payBindings, changeBoundPayout } from '../src/lib/authoring/payBindings';
import type { ModelGraph } from '../src/lib/modelProjection';

test('Exact payout text preserves long decimals/fractions and rejects incomplete or non-integral executable credits', () => {
  expect(parseExactAmount('0.000000000000000001')).toEqual({ numerator: 1n, denominator: 1000000000000000000n });
  expect(exactInteger('1/6', 30)).toBe(5); expect(normalizedCredits(5, 30)).toBe('1/6'); expect(exactInteger('0.200000000', 25)).toBe(5);
  for (const value of ['', ' ', '1e3', 'NaN', '-1', '1/0', '1.2.3']) expect(() => parseExactAmount(value)).toThrow();
  for (const value of ['1/3', '0.5', '9007199254740992']) expect(() => exactInteger(value)).toThrow('whole credits');
});
for (const [name, create] of [['Ancient Dragon', createAncientDragonGraph], ['Woodland Whisper', createWoodlandWhisperGraph], ['X7 Club', createX7ClubGraph]] as const) {
  test(`${name}: line edit synchronizes exact declared precision, runtime payout and derived wild while keeping other source`, () => {
    const graph = create() as ModelGraph, binding = payBindings(graph).find(binding => binding.kind === 'line')!;
    expect(binding).toBeDefined(); expect(binding.derivedWild).toBe(true);
    const before = structuredClone(graph), row = binding.rows[0], count = row.counts[0], amount = Math.max(0, Number(row.values[0]) - 1);
    const next = changeBoundPayout(graph, binding, row.symbolId, count, String(amount)), state = next.initialState as { linePays: number[][] };
    expect(state.linePays[Number(row.symbolId)][count]).toBe(amount);
    expect(state.linePays[binding.wild!][count]).toBe(Math.max(...binding.rows.map(entry => entry.symbolId === row.symbolId ? amount : Number(entry.values[entry.counts.indexOf(count)] ?? 0))));
    const table = (next.paytables as { id: string; entries: { symbolId: string; payouts: string[] }[] }[]).find(table => table.id === binding.tableId)!;
    expect(table.entries.find(entry => entry.symbolId === row.symbolId)!.payouts[0]).toBe(normalizedCredits(amount, binding.roundCost));
    expect(next.mechanics).toEqual(before.mechanics); expect(next.nodes).toEqual(before.nodes); expect(next.edges).toEqual(before.edges); expect(graph).toEqual(before);
    expect(payBindings(next).find(binding => binding.kind === 'line')!.rows[0].values[0]).toBe(String(amount));
    expect(changeBoundPayout(graph, binding, row.symbolId, count, row.values[0])).toEqual(graph);
    if (name === 'X7 Club') expect(() => changeBoundPayout(graph, binding, row.symbolId, count, String(binding.maximum + 1))).toThrow('payout bound');
    else expect(changeBoundPayout(graph, binding, row.symbolId, count, String(binding.maximum + 1)).nodes).not.toEqual(graph.nodes);
  });
}
for (const [name, create] of [['Ancient Dragon', createAncientDragonGraph], ['Woodland Whisper bought', () => createWoodlandWhisperGraph(undefined, { buy: true })]] as const) {
  test(`${name}: scatter total-bet units normalize to the actual round price`, () => {
    const graph = create() as ModelGraph, binding = payBindings(graph).find(binding => binding.kind === 'scatter')!; expect(binding).toBeDefined();
    const row = binding.rows[0], count = row.counts[0], amount = Math.max(0, Number(row.values[0]) - 1), next = changeBoundPayout(graph, binding, row.symbolId, count, String(amount));
    expect((next.initialState as { scatterPays: number[] }).scatterPays[count]).toBe(amount);
    const table = (next.paytables as { id: string; entries: { payouts: string[] }[] }[]).find(table => table.id === binding.tableId)!;
    expect(table.entries[0].payouts[0]).toBe(normalizedCredits(amount * binding.scale, binding.roundCost));
    expect(next.mechanics).toEqual(graph.mechanics); expect(next.nodes).toEqual(graph.nodes); expect((next.initialState as Record<string, unknown>).roundCost).toBe(binding.roundCost);
  });
}
test('Explicit wild payouts remain authored; renamed tables are recognized and stale precision does not overwrite source', () => {
  const graph = createX7ClubGraph() as ModelGraph, first = payBindings(graph).find(binding => binding.kind === 'line')!;
  const table = (graph.paytables as { id: string; entries: { symbolId: string; counts: number[]; payouts: string[] }[] }[]).find(table => table.id === first.tableId)!;
  table.id = 'my-private-payouts'; table.entries.push({ symbolId: String(first.wild), counts: [3], payouts: [normalizedCredits(2, first.roundCost)] });
  (graph.initialState as { linePays: number[][] }).linePays[first.wild!] = Array.from({ length: first.columns + 1 }, (_, count) => count === 3 ? 2 : 0);
  const binding = payBindings(graph).find(binding => binding.kind === 'line')!; expect(binding.tableId).toBe('my-private-payouts'); expect(binding.derivedWild).toBe(false);
  const row = binding.rows.find(row => row.symbolId !== String(binding.wild))!, next = changeBoundPayout(graph, binding, row.symbolId, row.counts[0], '0');
  expect((next.initialState as { linePays: number[][] }).linePays[binding.wild!]).toEqual((graph.initialState as { linePays: number[][] }).linePays[binding.wild!]);
  const stale = structuredClone(graph); (stale.paytables as typeof graph.paytables as { id: string; entries: { payouts: string[] }[] }[])[0].entries[0].payouts[0] = '999';
  expect(() => changeBoundPayout(stale, binding, row.symbolId, row.counts[0], '0')).toThrow('source');
});
test('Runtime Data/PutState writes and mutable wild rules do not expose an initial-state lookup as an editable static paytable', () => {
  for (const [nodeType, stateKey] of [['data', 'linePays'], ['putState', 'wildSymbol']] as const) {
    const graph = createAncientDragonGraph() as ModelGraph, before = structuredClone(graph);
    graph.nodes!.push({ id: 'runtime-override', nodeType, stateKey, rows: ['1'] });
    expect(payBindings(graph)).toEqual([]); expect(graph.nodes!.slice(0, -1)).toEqual(before.nodes);
  }
});
