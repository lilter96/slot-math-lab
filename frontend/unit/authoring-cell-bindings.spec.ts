import { test, expect } from '@playwright/test';
import { createNineLivesGraph } from '../src/games/nine-lives-tgslots/graph';
import { createX7ClubGraph } from '../src/games/x7-club/graph';
import { createAncientDragonGraph } from '../src/games/ancient-dragon/graph';
import { cellBindings, changeBoundCells } from '../src/lib/authoring/cellBindings';
import { weightBindings } from '../src/lib/authoring/weightBindings';
import type { ModelGraph } from '../src/lib/modelProjection';

test('TGSlots paid/free boards contain independent cell Draws; editing one or selected cells preserves other stages and refill', () => {
  const graph = createNineLivesGraph() as ModelGraph, bindings = cellBindings(graph); expect(bindings).toHaveLength(2);
  const paid = bindings.find(binding => binding.scope === 'paid-board')!, free = bindings.find(binding => binding.scope === 'free-board')!;
  expect(paid.cells).toHaveLength(30); expect(free.cells).toHaveLength(30); expect(new Set(paid.cells.map(cell => cell.source.key)).size).toBe(30);
  const first = paid.cells[0].source, second = paid.cells[1].source, values = new Map(first.rows.map((row, position) => [row.outcomeId, position ? String(row.weight) : String(row.weight + 1)]));
  const before = structuredClone(graph), changed = changeBoundCells(graph, paid, new Map([[first.key, values]]));
  expect(weightBindings(changed).find(source => source.key === first.key)!.rows[0].weight).toBe(first.rows[0].weight + 1);
  expect(weightBindings(changed).find(source => source.key === second.key)!.rows).toEqual(second.rows);
  expect(changed.mechanics!['free-board']).toEqual(graph.mechanics!['free-board']); expect(changed.mechanics!['drop-symbol']).toEqual(graph.mechanics!['drop-symbol']); expect(graph).toEqual(before);
  const both = changeBoundCells(graph, paid, new Map([[first.key, values], [second.key, values]]));
  expect(weightBindings(both).find(source => source.key === second.key)!.rows[0].weight).toBe(first.rows[0].weight + 1);
  expect(() => changeBoundCells(graph, paid, new Map([[free.cells[0].source.key, values]]))).toThrow('only cells');
});
test('X7 has fifteen column-major cells and no physical strips; geometry and stale assignments refuse candidate mutation', () => {
  const graph = createX7ClubGraph() as ModelGraph, binding = cellBindings(graph)[0]; expect(binding.cells).toHaveLength(15); expect(binding.rows).toBe(3); expect(binding.columns).toBe(5);
  expect(binding.cells.map(cell => cell.position)).toEqual(Array.from({ length: 15 }, (_, position) => position));
  const changed = structuredClone(graph); (changed.initialState as { boardPositions: number[] }).boardPositions.reverse();
  expect(cellBindings(changed)).toEqual([]); expect(() => changeBoundCells(changed, binding, new Map())).toThrow('assignment');
  expect(cellBindings(createAncientDragonGraph() as ModelGraph)).toEqual([]);
});
