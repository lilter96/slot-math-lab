import { test, expect } from '@playwright/test';
import { createAncientDragonGraph } from '../src/games/ancient-dragon/graph';
import type { ModelGraph } from '../src/lib/modelProjection';
import { reelBindings } from '../src/lib/authoring/reelBindings';
import { weightBindings } from '../src/lib/authoring/weightBindings';
import { payBindings } from '../src/lib/authoring/payBindings';
import { applyStudioPatches, replaceStudioPatch, studioCandidate, type StudioPatch } from '../src/lib/authoring/studioEdits';

test('A Studio draft composes source-local reel and weight edits into one untouched-base candidate', () => {
  const graph = createAncientDragonGraph() as ModelGraph, reel = reelBindings(graph)[0], weight = weightBindings(graph).find(source => source.source.node.stateWriteKey === 'mysterySymbol')!;
  expect(weight).toBeDefined(); const stops = structuredClone(reel.stops); [stops[0][0], stops[0][1]] = [stops[0][1], stops[0][0]];
  const patches: StudioPatch[] = [{ kind: 'reels', key: reel.key, stops }, { kind: 'weights', key: weight.key, values: weight.rows.map((row, index) => [row.outcomeId, String(row.weight + (index === 0 ? 1 : 0))]) }];
  const before = structuredClone(graph), result = studioCandidate(graph, patches); expect(result.error).toBeNull(); expect(result.changed).toBe(true); expect(graph).toEqual(before);
  expect(reelBindings(result.graph!)[0].source.strips[0].slice(0, 2)).toEqual(stops[0].slice(0, 2).map(stop => stop.symbol));
  expect(weightBindings(result.graph!).find(source => source.key === weight.key)!.rows[0].weight).toBe(weight.rows[0].weight + 1);
  expect(result.graph!.paytables).toEqual(graph.paytables); expect(result.graph!.nodes).toEqual(graph.nodes);
});
test('Batch payout edits share the original bound and regenerate the wild row once without order-dependent rejection', () => {
  const graph = createAncientDragonGraph() as ModelGraph, binding = payBindings(graph).find(binding => binding.kind === 'line')!;
  const largest = binding.rows.flatMap(row => row.counts.map((count, position) => ({ row, count, value: Number(row.values[position]) }))).sort((left, right) => right.value - left.value)[0];
  const other = binding.rows.find(row => row.symbolId !== largest.row.symbolId)!, count = other.counts.at(-1)!;
  const patches: StudioPatch[] = [{ kind: 'payout', key: binding.key, symbolId: largest.row.symbolId, count: largest.count, value: '0' }, { kind: 'payout', key: binding.key, symbolId: other.symbolId, count, value: String(binding.maximum) }];
  const first = applyStudioPatches(graph, patches), reversed = applyStudioPatches(graph, [...patches].reverse()); expect(first).toEqual(reversed);
  expect((first.initialState as { linePays: number[][] }).linePays[Number(other.symbolId)][count]).toBe(binding.maximum);
  expect(first.nodes).toEqual(graph.nodes); expect(first.mechanics).toEqual(graph.mechanics);
});
test('Incomplete inputs stay in a draft, invalid candidates leave source untouched and replacing one coordinate retains other edits', () => {
  const graph = createAncientDragonGraph() as ModelGraph, binding = payBindings(graph).find(binding => binding.kind === 'line')!, row = binding.rows[0];
  const first: StudioPatch = { kind: 'payout', key: binding.key, symbolId: row.symbolId, count: row.counts[0], value: '' }, other: StudioPatch = { kind: 'symbol', key: row.symbolId, name: 'A private name' };
  const before = structuredClone(graph); expect(studioCandidate(graph, [first, other]).error).toContain('incomplete'); expect(graph).toEqual(before);
  expect(replaceStudioPatch([first, other], { ...first, value: '0' })).toEqual([{ ...first, value: '0' }, other]);
});
