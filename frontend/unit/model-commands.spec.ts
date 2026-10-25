import { test, expect } from '@playwright/test';
import { createNineLivesGraph } from '../src/games/nine-lives/graph';
import { weightedSources, changeWeights, validateWeights } from '../src/lib/modelCommands';
import type { ModelGraph } from '../src/lib/modelProjection';
test('Nine Lives table edit changes one source weight and preserves unknown metadata and all nested graph structures', () => {
  const original = createNineLivesGraph() as ModelGraph;
  original.reviewerMetadata = { version: 42, tags: ['retained'] };
  const snapshot = JSON.stringify(original);
  const source = weightedSources(original).find(item => item.scope === 'base-spin')!;
  expect(source.rows.length).toBe(8);
  const changed = changeWeights(original, source.key, source.rows.map((row, i) => String(row.weight + (i === 0 ? 1 : 0))));
  expect(JSON.stringify(original)).toBe(snapshot);
  expect(changed.reviewerMetadata).toEqual(original.reviewerMetadata);
  const restored = changeWeights(changed, source.key, source.rows.map(row => String(row.weight)));
  expect(restored).toEqual(original);

});
test('Weights reject missing, fractional, negative, overflow and all-zero values; dynamic sources stay unsupported', () => {
  for (const values of [[''], ['1.5'], ['-1'], ['0','0'], ['9007199254740991','1'], ['Infinity']]) expect(validateWeights(values)).not.toBeNull();
  expect(validateWeights(['0','4'])).toBeNull();
  const graph: ModelGraph = { nodes: [{ id: 'dynamic', nodeType: 'draw', weightExpressionId: 'weights', drawWeights: [{ outcomeId: 'a', weight: 1, value: 0 }] }] };
  expect(weightedSources(graph)).toEqual([]);
  expect(() => changeWeights(graph, 'missing', ['1'])).toThrow();
});

test('Legal long weights and payloads survive source selection, edits and restoration without an Int32 limit', () => {
  const graph: ModelGraph = { nodes: [{ id: 'wide', nodeType: 'draw', drawWeights: [{ outcomeId: 'a', weight: 10000000000, value: -10000000000 }, { outcomeId: 'b', weight: 10000000000, value: 10000000000 }] }] };
  const source = weightedSources(graph)[0];
  expect(source).toBeDefined(); expect(validateWeights(['30000000000', '10000000000'])).toBeNull();
  const next = changeWeights(graph, source.key, ['30000000000', '10000000000']);
  expect(next.nodes![0].drawWeights).toEqual([{ outcomeId: 'a', weight: 30000000000, value: -10000000000 }, { outcomeId: 'b', weight: 10000000000, value: 10000000000 }]);
  expect(changeWeights(next, source.key, ['10000000000', '10000000000'])).toEqual(graph);
});

test('Exact browser integer bounds reject unsafe rows and unsafe totals rather than rounding them', () => {
  expect(validateWeights([String(Number.MAX_SAFE_INTEGER), '0'])).toBeNull();
  expect(validateWeights([String(Number.MAX_SAFE_INTEGER), '1'])).toContain('total weight');
  expect(validateWeights(['9007199254740992'])).toContain('browser editor');
  for (const row of [{ weight: 9007199254740992, value: 1 }, { weight: 1, value: 9007199254740992 }]) {
    expect(weightedSources({ nodes: [{ id: 'unsafe', nodeType: 'draw', drawWeights: [{ outcomeId: 'a', ...row }] }] })).toEqual([]);
  }
});
