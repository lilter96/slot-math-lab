import { test, expect } from '@playwright/test';
import { graphStudiosCorpus } from './fixtures/graphStudiosCorpus';
import { computedLineBoundProof } from './fixtures/computedLineBoundProof';
import { lineBindings } from '../src/lib/authoring/lineBindings';
import { changeLineDomain, lineStakePreview } from '../src/lib/authoring/lineLifecycle';
import { payBindings } from '../src/lib/authoring/payBindings';
import { computedLineBound } from '../src/lib/authoring/payoutBounds';
import type { ModelGraph } from '../src/lib/modelProjection';
test('Bundled line families/entries add/remove lines while preserving per-line pricing, bought ratio, native lookups and precision', () => {
  const corpus = graphStudiosCorpus().graphs.filter(({ caseId }) => /^(ancient-dragon\/ordinary|woodland-whisper\/|x7-club\/ordinary)/.test(caseId)); expect(corpus).toHaveLength(4);
  for (const { caseId, graph } of corpus) {
    const before = structuredClone(graph), binding = lineBindings(graph)[0], count = binding.lines.length, old = graph.initialState as Record<string, number>;
    const next = changeLineDomain(graph, binding, [...binding.lines.map(path => [...path]), [...binding.lines[0]]], [...binding.lines.map((_, index) => String(index)), null], { mode: 'per-line', cap: computedLineBound(graph) ? 'recompute' : 'keep' });
    expect(graph, caseId).toEqual(before); expect(lineBindings(next)[0].lines).toHaveLength(count + 1); const initial = next.initialState as typeof old; expect(initial.totalBet).toBe(count + 1); expect(initial.roundCost / initial.totalBet).toBe(old.roundCost / old.totalBet);
    expect(initial.linePays).toEqual(old.linePays); expect(initial.scatterPays).toEqual(old.scatterPays); expect(payBindings(next).map(binding => binding.tableId).sort()).toEqual(payBindings(graph).map(binding => binding.tableId).sort());
    const retained = changeLineDomain(next, lineBindings(next)[0], binding.lines.map(path => [...path]), binding.lines.map((_, index) => String(index)), { mode: 'per-line', cap: computedLineBound(next) ? 'recompute' : 'keep' }); expect((retained.initialState as typeof old).totalBet).toBe(old.totalBet); expect(lineBindings(retained)[0].lines).toHaveLength(count);
  }
});
test('Fixed total is an explicit changed contract; incomplete pricing and non-integral per-line scaling reject without changing source', () => {
  const graph = computedLineBoundProof() as ModelGraph, binding = lineBindings(graph)[0];
  expect(() => lineStakePreview(graph, binding, 2, { mode: 'fixed-total', cap: 'recompute' })).toThrow('Confirm changing');
  const next = changeLineDomain(graph, binding, [[0, 0], [0, 0]], ['0', null], { mode: 'fixed-total', confirmFixed: true, cap: 'recompute' }); expect((next.initialState as Record<string, unknown>).totalBet).toBe((graph.initialState as Record<string, unknown>).totalBet); expect(next.nodes!.find(node => node.id === 'sink')!.winCap).toBe(25);
  const custom = structuredClone(graph); (custom.initialState as Record<string, unknown>).roundCost = 3; (custom.initialState as Record<string, unknown>).totalBet = 2;
  expect(() => lineStakePreview(custom, { ...binding, lines: [[0, 0], [0, 0], [0, 0]] }, 4, { mode: 'per-line', cap: 'keep' })).toThrow('whole total credits');
});
test('Retained line IDs and opaque metadata/node/edge fields survive topology changes', () => {
  const graph = computedLineBoundProof() as ModelGraph, binding = lineBindings(graph)[0], scope = graph.mechanics![binding.scope];
  scope.nodes.find(node => node.nodeType === 'library')!.opaque = { retained: true }; (scope.edges as Record<string, unknown>[])[0].opaque = { retained: true };
  const set = (graph.paylineSets as { paylines: { opaque?: unknown }[] }[])[0]; set.paylines[0].opaque = { retained: true };
  const current = lineBindings(graph)[0], original = structuredClone(graph), next = changeLineDomain(graph, current, [[0, 0], [0, 0]], ['0', null], { mode: 'per-line', cap: 'recompute' });
  expect(graph).toEqual(original); expect((next.paylineSets as typeof graph.paylineSets as { paylines: { opaque?: unknown }[] }[])[0].paylines[0].opaque).toEqual({ retained: true }); expect(next.mechanics![current.scope].nodes.find(node => node.id === scope.nodes.find(node => node.nodeType === 'library')!.id)!.opaque).toEqual({ retained: true }); expect((next.mechanics![current.scope].edges as Record<string, unknown>[])[0].opaque).toEqual({ retained: true });
});
