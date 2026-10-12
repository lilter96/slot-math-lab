import { test, expect } from '@playwright/test';
import { createWoodlandWhisperGraph } from '../src/games/woodland-whisper/graph';
import { changeReels, reelSources } from '../src/lib/reelCommands';
import { conditionedReels } from '../src/lib/reelConditioning';
import { Flow, Mechanics, gameGraph } from '../src/games/shared/graph/flow';
import { reelState, showingState, spinReelsShowing } from '../src/games/shared/graph/mechanics/reels';
import type { ModelGraph } from '../src/lib/modelProjection';

test('Bought feature rebuilds all coupled domains, preserves source metadata and retains its rule in canonical AST', () => {
  const graph = createWoodlandWhisperGraph(undefined, { buy: true }) as ModelGraph;
  const source = reelSources(graph)[0], condition = source.conditions![0], before = structuredClone(graph);
  graph.reviewerMetadata = { keep: true };
  graph.mechanics![condition.scope].reviewerMetadata = { scope: true };
  const strips = source.strips.map((strip, index) => index ? strip : [...strip, condition.symbol]);
  const changed = changeReels(graph, source.key, strips);
  expect(graph.initialState).toEqual(before.initialState);
  expect(changed.reviewerMetadata).toEqual({ keep: true });
  expect(changed.mechanics![condition.scope].reviewerMetadata).toEqual({ scope: true });
  expect(reelSources(changed)[0].conditions).toEqual(source.conditions);
  const expected = conditionedReels(source, condition, strips);
  expect((changed.initialState as Record<string, unknown>).showPatterns).toEqual(expected.state.showPatterns);
  expect(changed.mechanics![condition.scope].nodes).toEqual(expected.definition.nodes);
  expect((changed.mechanics![condition.scope].expressions as Record<string, unknown>).__reelEditorAtLeast).toEqual({ exprType: 'constant', kind: 'Integer', value: '3' });
});

test('Condition is not guessed when materialized outputs are ambiguous; declared constants survive an all-symbol strip edit', () => {
  const reels = { name: 'ordinary', stripKey: 'strip', strips: [['0', '1'], ['0', '1']], rows: 1 };
  const showing = { name: 'conditioned', symbol: '1', symbolName: 'One', atLeast: 1 }, mechanics = new Mechanics();
  const graph = gameGraph({ id: 'conditional-toy', name: 'Conditional toy', mechanics,
    root: new Flow().use('spin', 'Conditioned draw', mechanics.add(spinReelsShowing(reels, showing))),
    initialState: { ...reelState(reels), ...showingState(reels, showing) }, tables: { boardConfig: { columns: 2, rows: 1 }, symbols: [{ id: '0', name: 'Zero' }, { id: '1', name: 'One' }] } }) as ModelGraph;
  const source = reelSources(graph)[0];
  expect(source.conditions?.[0].atLeast).toBe(1);
  const changed = changeReels(graph, source.key, [['1'], ['1']]);
  expect(reelSources(JSON.parse(JSON.stringify(changed)))[0].conditions?.[0].atLeast).toBe(1);
  const unannotated = structuredClone(changed);
  delete (unannotated.mechanics!.conditioned.expressions as Record<string, unknown>).__reelEditorSymbol;
  delete (unannotated.mechanics!.conditioned.expressions as Record<string, unknown>).__reelEditorAtLeast;
  expect(reelSources(unannotated)).toEqual([]);
  const invalid = structuredClone(changed);
  (invalid.mechanics!.conditioned.nodes.find(node => node.nodeType === 'draw')!.drawWeights as { weight: number }[])[0].weight = 2;
  expect(reelSources(invalid)).toEqual([]);
  const overridden = structuredClone(changed);
  overridden.mechanics!.conditioned.initialState = { showPatterns: [[0, 0]] };
  expect(reelSources(overridden)).toEqual([]);
});

test('Conditioned changes reject impossible events, integer-budget overflow and loss of metadata on removed nodes', () => {
  const graph = createWoodlandWhisperGraph(undefined, { buy: true }) as ModelGraph, source = reelSources(graph)[0];
  expect(() => changeReels(graph, source.key, source.strips.map(strip => strip.map(() => '0')))).toThrow('No reel stops');
  expect(conditionedReels(source, source.conditions![0], Array.from({ length: 5 }, () => Array(100).fill('11'))).distribution.patterns[0].weight).toBe(10000000000);
  expect(() => conditionedReels(source, source.conditions![0], Array.from({ length: 5 }, () => Array(4096).fill('11')))).toThrow('integer range');
  const node = graph.mechanics!['spin-reels-triggering'].nodes.find(node => node.nodeType === 'branch')!;
  node.opaqueReviewerNote = 'keep';
  expect(() => changeReels(graph, source.key, source.strips.map(() => ['11']))).toThrow('authored metadata');
});
