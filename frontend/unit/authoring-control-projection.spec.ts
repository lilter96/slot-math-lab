import { test, expect } from '@playwright/test';
import { graphStudiosCorpus } from './fixtures/graphStudiosCorpus';
import { projectControl, controlRevision } from '../src/lib/authoring/controlProjection';
import { changeControlSource } from '../src/lib/authoring/controlEdits';
import { respinContracts, changeRespinCount } from '../src/lib/authoring/respinContract';
import { sourceKey } from '../src/lib/authoring/sourceIndex';
import { num } from '../src/games/shared/graph/expr';
test('All 65 corpus graphs project their actual nodes/edges without altering source, versions, prices or random draw order', () => {
  for (const { graph, caseId } of graphStudiosCorpus().graphs) { const original = structuredClone(graph); for (const scope of [null, ...Object.keys(graph.mechanics ?? {})]) { const view = projectControl(graph, scope); expect(view.regions.flatMap(region => region.actions.map(action => action.id)).sort(), `${caseId}/${scope}`).toEqual((scope === null ? graph.nodes! : graph.mechanics![scope].nodes).map(node => node.id).sort()); } expect(graph, caseId).toEqual(original); }
});
test('Projected expression edits retain opaque fields/topology and reject stale or different scope bindings', () => {
  const graph = graphStudiosCorpus().graphs.find(value => value.caseId === 'ancient-dragon/ordinary')!.graph, scope = Object.keys(graph.mechanics!).find(scope => graph.mechanics![scope].nodes.some(node => node.nodeType === 'modifyState'))!;
  const source = graph.mechanics![scope].nodes.find(node => node.nodeType === 'modifyState')!, revision = controlRevision(graph, scope), original = structuredClone(graph), next = changeControlSource(graph, scope, revision, [{ kind: 'expression', nodeKey: sourceKey(scope, source.id), field: 'expressionId', value: num(0) }], 'keep');
  expect(next.nodes).toEqual(graph.nodes); expect(next.mechanics![scope].nodes).toEqual(graph.mechanics![scope].nodes); expect(next.mechanics![scope].edges).toEqual(graph.mechanics![scope].edges); expect(graph).toEqual(original);
  expect(() => changeControlSource(next, scope, revision, [], 'keep')).toThrow('source or its callers changed');
});
test('X7 ordinary/bought respin count edits synchronize initialization, restoration and the proven bound while prizes and held state stay intact', () => {
  for (const { graph } of graphStudiosCorpus().graphs.filter(value => value.caseId.startsWith('x7-club/'))) {
    const binding = respinContracts(graph)[0]; expect(binding).toBeDefined(); const original = structuredClone(graph), next = changeRespinCount(graph, binding, 4), updated = respinContracts(next)[0]; expect(updated.count).toBe(4); expect(updated.heldAtStart).toBe(binding.heldAtStart); expect(updated.cells).toBe(binding.cells); expect(next.initialState).toEqual(graph.initialState); expect(next.nodes).toEqual(graph.nodes); expect(next.paytables).toEqual(graph.paytables); expect(graph).toEqual(original);
    const beforeDraws = Object.values(graph.mechanics!).flatMap(scope => scope.nodes.filter(node => node.nodeType === 'draw')), afterDraws = Object.values(next.mechanics!).flatMap(scope => scope.nodes.filter(node => node.nodeType === 'draw')); expect(afterDraws).toEqual(beforeDraws);
  }
});
test('A custom after-coins hook cannot retain a proven respin bound if it clears held cells or makes boost multipliers zero', () => {
  const graph = graphStudiosCorpus().graphs.find(value => value.caseId === 'x7-club/bought')!.graph, unsafe = structuredClone(graph);
  const owner = Object.values(unsafe.mechanics!).find(definition => JSON.stringify(definition.expressions).includes('boostTimes') && definition.nodes.some(node => node.nodeType === 'modifyState' && node.outputKey === 'coins'))!, node = owner.nodes.find(node => node.nodeType === 'modifyState' && node.outputKey === 'coins')!;
  (owner.expressions as Record<string, unknown>)[String(node.expressionId)] = num(0); expect(respinContracts(unsafe)).toEqual([]);
  const zero = structuredClone(graph); (zero.initialState as Record<string, unknown>).boostTimes = ((zero.initialState as Record<string, unknown>).boostTimes as number[]).map(() => 0); expect(respinContracts(zero)).toEqual([]); expect(respinContracts(graph)).toHaveLength(1);
});
test('An unconditional empty bought entry or a weaker trigger cannot borrow the held-cell assumption of another entry', () => {
  const bought = graphStudiosCorpus().graphs.find(value => value.caseId === 'x7-club/bought')!.graph, next = structuredClone(bought); (next.initialState as Record<string, unknown>).boughtCoins = Array.from({ length: 15 }, () => 0); expect(respinContracts(next)).toEqual([]);
  const ordinary = graphStudiosCorpus().graphs.find(value => value.caseId === 'x7-club/ordinary')!.graph, weak = structuredClone(ordinary); (weak.initialState as Record<string, unknown>).holdTrigger = 1; expect(respinContracts(weak)).toEqual([]); expect(respinContracts(ordinary)).toHaveLength(1);
});
