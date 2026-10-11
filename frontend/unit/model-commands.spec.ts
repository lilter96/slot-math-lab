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
  for (const values of [[''], ['1.5'], ['-1'], ['0','0'], ['2147483647','1'], ['Infinity']]) expect(validateWeights(values)).not.toBeNull();
  expect(validateWeights(['0','4'])).toBeNull();
  const graph: ModelGraph = { nodes: [{ id: 'dynamic', nodeType: 'draw', weightExpressionId: 'weights', drawWeights: [{ outcomeId: 'a', weight: 1, value: 0 }] }] };
  expect(weightedSources(graph)).toEqual([]);
  expect(() => changeWeights(graph, 'missing', ['1'])).toThrow();
});
