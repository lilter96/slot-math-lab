import { test, expect, type APIRequestContext } from './fixtures';
import { Flow, Mechanics, gameGraph } from '../src/games/shared/graph/flow';
import { reelState, spinReels } from '../src/games/shared/graph/mechanics/reels';
import { and, at, eq, num, str, when } from '../src/games/shared/graph/expr';
import { changeReels, reelSources } from '../src/lib/reelCommands';
import { changeWeights, weightedSources } from '../src/lib/modelCommands';
import type { ModelGraph } from '../src/lib/modelProjection';
async function exact(api: APIRequestContext, graph: ModelGraph, rtp: number) {
  const validation = await api.post('/api/validate', { data: { config: graph } }); expect(validation.ok()).toBe(true); expect((await validation.json()).isValid).toBe(true);
  const response = await api.post('/api/evaluate/light', { data: { config: graph, maxBranches: 10000, sampleSize: 5000 } }); expect(response.ok()).toBe(true);
  const result = await response.json(); expect(result.strategy).toBe('Exact'); expect(result.rtp).toBeCloseTo(rtp, 12);
}
test('Foundation: ordered physical-reel edit changes the real window payout and no-op keeps seeded state', async ({ request }) => {
  const spec = { name: 'ordered-custom-source', stripKey: 'customStrip', rows: 2, strips: [['A', 'B', 'C'], ['C']] };
  const mechanics = new Mechanics();
  const root = new Flow().use('spin', 'Custom ordered window', mechanics.add(spinReels(spec)))
    .write('award', 'A then B on reel one pays one', 'coins', when(and(eq(at('board', num(0)), str('A')), eq(at('board', num(1)), str('B'))), num(1), num(0)))
    .sink('sink', 'Round payout', 'coins', 1);
  const graph = gameGraph({ id: 'custom-reel-proof', name: 'Ordered reel proof', description: 'Three physical stops have windows AB, BC, CA. Only AB pays.', root, mechanics,
    initialState: { coins: 0, ...reelState(spec) }, tables: { symbols: ['A', 'B', 'C'].map(id => ({ id, name: id, kind: 'Standard' })), boardConfig: { rows: 2, columns: 2 }, opaque: { retained: true } } }) as ModelGraph;
  const before = JSON.stringify(graph), source = reelSources(graph)[0]; expect(source).toBeDefined();
  const noOp = changeReels(graph, source.key, source.strips); expect(noOp).toEqual(graph);
  await exact(request, graph, 1 / 3);
  const changed = changeReels(graph, source.key, [['A', 'C', 'B'], ['C']]); await exact(request, changed, 0);
  expect((changed.initialState as Record<string, unknown>).customStrip0).toEqual(['A', 'C', 'B', 'A']); expect(changed.opaque).toEqual({ retained: true }); expect(JSON.stringify(graph)).toBe(before);
  for (const seed of [7, 42]) {
    const responses = [];
    for (const config of [graph, noOp]) { const response = await request.post('/api/play/round', { data: { config, seed, roundIndex: 0 } }); expect(response.ok()).toBe(true); responses.push(await response.json()); }
    expect(responses[1]).toEqual(responses[0]);
  }
});
test('Foundation: independent outcome weights change real exact probability without altering payload or unrelated source', async ({ request }) => {
  const graph: ModelGraph = { schemaVersion: '1.0.0', id: 'custom-weight-proof', name: 'Independent draw proof', opaque: { keep: true }, nodes: [
    { id: 'draw', nodeType: 'draw', drawWeights: [{ outcomeId: '001', weight: 3, value: 1, custom: 'keep' }, { outcomeId: 'zero', weight: 1, value: 0 }], outputs: { out: { name: 'out', type: 'Wins' } } },
    { id: 'sink', nodeType: 'metricsSink', winCap: 10, inputs: { in: { name: 'in', type: 'Wins' } } },
  ], edges: [{ id: 'edge', sourceNodeId: 'draw', sourcePort: 'out', targetNodeId: 'sink', targetPort: 'in' }] };
  const before = JSON.stringify(graph), source = weightedSources(graph)[0];
  const changed = changeWeights(graph, source.key, ['1', '3']);
  await exact(request, graph, 0.75); await exact(request, changed, 0.25);
  expect(changed.nodes![0].drawWeights).toEqual([{ outcomeId: '001', weight: 1, value: 1, custom: 'keep' }, { outcomeId: 'zero', weight: 3, value: 0 }]);
  expect(changed.edges).toEqual(graph.edges); expect(changed.opaque).toEqual(graph.opaque); expect(JSON.stringify(graph)).toBe(before);
});
