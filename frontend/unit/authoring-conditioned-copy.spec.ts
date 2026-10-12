import { test, expect } from '@playwright/test';
import { conditionedSourceProof } from './fixtures/conditionedSourceProof';
import { copySourceUse, definitionCopyPlan } from '../src/lib/authoring/sourceLifecycle';
import { reelBindings, changeBoundReels } from '../src/lib/authoring/reelBindings';
import { weightBindings } from '../src/lib/authoring/weightBindings';
import { sourceKey, indexGraphSources } from '../src/lib/authoring/sourceIndex';
import { createWoodlandWhisperGraph } from '../src/games/woodland-whisper/graph';
import type { ModelGraph } from '../src/lib/modelProjection';
test('A private conditioned call has independent strips, pattern table and scratch field; old calls and opaque fields stay intact', () => {
  const graph = conditionedSourceProof() as ModelGraph, original = structuredClone(graph), scope = graph.mechanics!['conditional-source'];
  scope.opaque = { exprType: 'fieldAccess', target: 'state', path: ['showPatterns'] }; // Non-executable extension must not be renamed.
  expect(definitionCopyPlan(graph, sourceKey(null, 'second')).privateConditionFields).toEqual(['showPatterns', 'showPattern']);
  const copied = copySourceUse(graph, sourceKey(null, 'second'), 'private-copy');
  expect(copied.mechanics!['conditional-source']).toEqual(scope); expect(copied.mechanics!['private-copy'].opaque).toEqual(scope.opaque); expect(copied.nodes!.find(node => node.id === 'first')).toEqual(graph.nodes!.find(node => node.id === 'first'));
  const bindings = reelBindings(copied); expect(bindings).toHaveLength(2); const privateSource = bindings.find(binding => binding.source.label.startsWith('private-copy'))!;
  expect(privateSource.source.conditions![0]).toMatchObject({ patternsKey: 'private-copy_showPatterns', patternKey: 'private-copy_showPattern' });
  expect(weightBindings(copied).filter(binding => binding.source.scope === 'private-copy').every(binding => binding.capability === 'computed')).toBe(true);
  const stops = structuredClone(privateSource.stops); stops[0][1].symbol = 'S'; const changed = changeBoundReels(copied, privateSource, stops);
  expect(reelBindings(changed)).toHaveLength(2); expect(changed.mechanics!['conditional-source']).toEqual(scope); expect((changed.initialState as Record<string, unknown>).showPatterns).toEqual((original.initialState as Record<string, unknown>).showPatterns);
  expect((changed.initialState as Record<string, unknown>)['private-copy_showPatterns']).toEqual([[1, 0], [1, 1]]);
  expect(() => changeBoundReels(copied, privateSource, privateSource.stops.map(strip => strip.map(stop => ({ ...stop, symbol: 'A' }))))).toThrow('No reel stops'); expect(graph.initialState).toEqual(original.initialState);
});
test('Bundled Woodland bought entry retains two editable bindings after one conditioned use is copied', () => {
  const graph = createWoodlandWhisperGraph(undefined, { buy: true }) as ModelGraph, condition = reelBindings(graph)[0].source.conditions![0];
  const use = indexGraphSources(graph).libraryUses.find(use => use.definition === condition.scope)!;
  const next = copySourceUse(graph, use.source.key, 'private-bought'); expect(reelBindings(next)).toHaveLength(2);
  const privateSource = reelBindings(next).find(binding => binding.source.conditions?.some(condition => condition.scope === 'private-bought'))!;
  const stops = structuredClone(privateSource.stops); [stops[0][0], stops[0][1]] = [stops[0][1], stops[0][0]];
  const changed = changeBoundReels(next, privateSource, stops); expect(reelBindings(changed)).toHaveLength(2); expect(changed.mechanics![condition.scope]).toEqual(graph.mechanics![condition.scope]);
});
test('Renamed conditional node/expression/edge identities remain editable and opaque retained metadata survives reconciliation', () => {
  const graph = conditionedSourceProof() as ModelGraph, scope = graph.mechanics!['conditional-source'];
  const ids = new Map(scope.nodes.map((node, index) => [node.id, `authored-${index}`])), expressions = new Map(Object.keys(scope.expressions).map((id, index) => [id, `formula-${index}`]));
  scope.nodes.forEach(node => { node.id = ids.get(node.id)!; for (const key of ['expressionId', 'conditionId']) if (typeof node[key] === 'string') node[key] = expressions.get(String(node[key])); });
  scope.edges.forEach(edge => { edge.sourceNodeId = ids.get(edge.sourceNodeId)!; edge.targetNodeId = ids.get(edge.targetNodeId)!; edge.id = `authored-${edge.id}`; });
  scope.expressions = Object.fromEntries(Object.entries(scope.expressions).map(([id, ast]) => [expressions.get(id)!, ast])); scope.nodes[0].opaque = { retain: true };
  const binding = reelBindings(graph)[0], stops = structuredClone(binding.stops); stops[0][1].symbol = 'S';
  const changed = changeBoundReels(graph, binding, stops); expect(reelBindings(changed)).toHaveLength(1); expect(changed.mechanics!['conditional-source'].nodes[0]).toMatchObject({ id: 'authored-0', opaque: { retain: true } });
});
