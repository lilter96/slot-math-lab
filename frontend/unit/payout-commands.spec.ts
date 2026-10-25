import { test, expect } from '@playwright/test';
import { createNineLivesGraph } from '../src/games/nine-lives/graph';
import { clusterPayoutCapability, changeClusterPayouts } from '../src/lib/payoutCommands';
import type { ModelGraph } from '../src/lib/modelProjection';
test('Cluster coins and metadata round trip while preserving unknown fields and executable expressions', () => {
  const graph = { ...createNineLivesGraph(), unknown: { keep: true } } as ModelGraph;
  const coins = (graph.initialState as { clusterPaytable: string[] }).clusterPaytable.slice(0, 26);
  const changed = changeClusterPayouts(graph, '1', ['2', ...coins.slice(1)]);
  expect((changed.initialState as { clusterPaytable: string[] }).clusterPaytable[0]).toBe('2');
  expect(clusterPayoutCapability(changed)!.entries[0].payouts[0]).toBe('0.1');
  expect(changed.mechanics).toBe(graph.mechanics); expect(changed.expressions).toBe(graph.expressions);
  expect(changeClusterPayouts(changed, '1', coins)).toEqual(graph);
});
test('Payout forms reject mismatched metadata, changed lookups and invalid amounts', () => {
  const graph = createNineLivesGraph() as ModelGraph;
  expect(clusterPayoutCapability({ ...graph, expressions: {} })).toBeNull();
  expect(clusterPayoutCapability({ ...graph, initialState: { ...(graph.initialState as object), baseCost: 10 } })).toBeNull();
  expect(clusterPayoutCapability({ ...graph, paytables: [{ id: 'nine-lives-paytable', entries: [] }] })).toBeNull();
  expect(() => changeClusterPayouts(graph, '1', Array(26).fill('1.5'))).toThrow();
});
