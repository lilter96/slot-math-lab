import { test, expect } from '@playwright/test';
import { createNineLivesGraph } from '../src/games/nine-lives/graph';
import { freeSpinCapability, changeFreeSpinCount } from '../src/lib/featureCommands';
import type { ModelGraph } from '../src/lib/modelProjection';
test('Guarded Nine Lives form updates all four authored count sources without replacing any other rule', () => {
  const graph = createNineLivesGraph('buy') as ModelGraph;
  expect(freeSpinCapability(graph)).toBe(9);
  const next = changeFreeSpinCount(graph, '1');
  expect(freeSpinCapability(next)).toBe(1);
  expect(changeFreeSpinCount(next, '9')).toEqual(graph);
  expect((next.initialState as Record<string, unknown>).entryCost).toBe((graph.initialState as Record<string, unknown>).entryCost);
});
test('A modified/unmapped count source and invalid limits do not allow form writes', () => {
  const graph = createNineLivesGraph() as ModelGraph;
  for (const raw of ['0','-1','1.5','100001','']) expect(() => changeFreeSpinCount(graph, raw)).toThrow();
  const modified = { ...graph, initialState: { ...(graph.initialState as object), freeSpins: 8 } };
  expect(freeSpinCapability(modified)).toBeNull();
  expect(() => changeFreeSpinCount(modified, '1')).toThrow();
  expect(freeSpinCapability({ nodes: [] })).toBeNull();
});
