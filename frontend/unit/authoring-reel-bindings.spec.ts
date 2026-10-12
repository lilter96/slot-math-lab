import { test, expect } from '@playwright/test';
import { createAncientDragonGraph } from '../src/games/ancient-dragon/graph';
import { createLeMilitareGraph } from '../src/games/le-militare/graph';
import { createWoodlandWhisperGraph } from '../src/games/woodland-whisper/graph';
import type { ModelGraph } from '../src/lib/modelProjection';
import { reelBindings, changeBoundReels } from '../src/lib/authoring/reelBindings';
import { reelSources } from '../src/lib/reelCommands';

function rename(graph: ModelGraph) {
  const definitions = Object.keys(graph.mechanics ?? {}), scopes = new Map(definitions.map((scope, index) => [scope, `custom-definition-${index}`]));
  const all = [graph, ...Object.values(graph.mechanics ?? {})];
  for (const definition of all) {
    const ids = new Map((definition.nodes ?? []).map((node, index) => [node.id, `custom-node-${index}`]));
    const expressions = new Map(Object.keys(definition.expressions ?? {}).map((id, index) => [id, `custom-expression-${index}`]));
    for (const node of definition.nodes ?? []) {
      node.id = ids.get(node.id)!;
      if (node.mechanicName) node.mechanicName = scopes.get(String(node.mechanicName)) ?? node.mechanicName;
      for (const field of ['expressionId', 'conditionId', 'stopConditionId']) if (node[field]) node[field] = expressions.get(String(node[field])) ?? node[field];
    }
    for (const edge of definition.edges ?? []) { edge.id = `custom-${edge.id}`; edge.sourceNodeId = ids.get(String(edge.sourceNodeId))!; edge.targetNodeId = ids.get(String(edge.targetNodeId))!; }
    if (definition.expressions) definition.expressions = Object.fromEntries(Object.entries(definition.expressions).map(([id, value]) => [expressions.get(id)!, value]));
  }
  graph.mechanics = Object.fromEntries(Object.entries(graph.mechanics ?? {}).map(([scope, definition]) => [scopes.get(scope)!, definition]));
}
test('Renamed weighted physical sources preserve retained stop identity, exact weights, payload metadata and untouched graph', () => {
  const graph = createAncientDragonGraph() as ModelGraph;
  const original = reelSources(graph)[0], ref = original.stops[0];
  const draw = graph.mechanics![ref.scope].nodes.find(node => node.id === ref.nodeId)!;
  const rows = draw.drawWeights as { weight: number; opaque?: unknown }[];
  rows[0].weight = 7; rows[0].opaque = { keep: true }; rows[1].weight = 0;
  rename(graph); graph.opaque = { keep: true };
  const binding = reelBindings(graph)[0]; expect(binding).toBeDefined(); expect(binding.uses.length).toBeGreaterThan(0);
  const before = structuredClone(graph), stops = structuredClone(binding.stops);
  [stops[0][0], stops[0][1]] = [stops[0][1], stops[0][0]];
  const changed = changeBoundReels(graph, binding, stops), target = binding.source.stops[0];
  const weights = changed.mechanics![target.scope].nodes.find(node => node.id === target.nodeId)!.drawWeights;
  expect(weights[0]).toMatchObject({ outcomeId: '0', weight: 0, value: 0 });
  expect(weights[1]).toMatchObject({ outcomeId: '1', weight: 7, value: 0, opaque: { keep: true } });
  expect(graph).toEqual(before); expect(changed.opaque).toEqual(before.opaque);
  const rebound = reelBindings(changed)[0]; expect(rebound.source.strips[0][0]).toBe(stops[0][0].symbol);
  expect(() => changeBoundReels(changed, binding, stops)).toThrow('dependencies changed');
});
test('Shared Le Militare refill stays bound and insert/delete policies are explicit before mutation', () => {
  const graph = createLeMilitareGraph() as ModelGraph, binding = reelBindings(graph)[0], stops = structuredClone(binding.stops);
  expect(binding.uses.some(use => String(use.node.label).toLowerCase().includes('refill')) || binding.uses.length > binding.source.stops.length).toBe(true);
  const before = structuredClone(graph); stops[0].push({ identity: null, symbol: stops[0][0].symbol });
  expect(() => changeBoundReels(graph, binding, stops)).toThrow('exact weight');
  const changed = changeBoundReels(graph, binding, stops, { addedWeight: '3' });
  const stop = binding.source.stops[0], weights = changed.mechanics![stop.scope].nodes.find(node => node.id === stop.nodeId)!.drawWeights;
  expect(weights.at(-1).weight).toBe(3); expect(graph).toEqual(before);
  stops[0].shift(); expect(() => changeBoundReels(graph, binding, stops, { addedWeight: '3' })).toThrow('discarding');
  expect(() => changeBoundReels(graph, binding, stops, { addedWeight: '3', removedStops: 'discard' })).not.toThrow();
});
test('Bought Woodland no-op retains AST and strip edits reject impossible conditioning without mutation', () => {
  const graph = createWoodlandWhisperGraph(undefined, { buy: true }) as ModelGraph, binding = reelBindings(graph)[0];
  const before = structuredClone(graph); expect(changeBoundReels(graph, binding, binding.stops)).toEqual(before);
  const stops = binding.stops.map(strip => strip.map(stop => ({ ...stop, symbol: stop.symbol === '11' ? '1' : stop.symbol })));
  expect(() => changeBoundReels(graph, binding, stops)).toThrow('No reel stops'); expect(graph).toEqual(before);
  const weighted = structuredClone(graph), ordinary = binding.source.stops.find(stop => !binding.source.conditions?.some(condition => condition.scope === stop.scope))!;
  weighted.mechanics![ordinary.scope].nodes.find(node => node.id === ordinary.nodeId)!.drawWeights[0].weight = 2;
  expect(changeBoundReels(weighted, reelBindings(weighted)[0], reelBindings(weighted)[0].stops)).toEqual(weighted);
  const reordered = structuredClone(reelBindings(weighted)[0].stops); [reordered[0][0], reordered[0][1]] = [reordered[0][1], reordered[0][0]];
  expect(() => changeBoundReels(weighted, reelBindings(weighted)[0], reordered)).toThrow('conditioning contract');
});
