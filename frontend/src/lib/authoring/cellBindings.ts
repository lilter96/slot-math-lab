import { spinCells } from '../../games/shared/graph/mechanics/cells';
import { canonicalJson } from '../hash';
import type { ModelGraph } from '../modelProjection';
import { reelObject } from '../reelCommands';
import { matchReelShape } from './reelShape';
import { indexGraphSources, sourceKey, type NodeSource, type SourcePath } from './sourceIndex';
import { changeBoundWeights, weightBindings, type WeightBinding } from './weightBindings';

export interface CellBinding {
  version: 1;
  key: string;
  revision: string;
  scope: string;
  rows: number;
  columns: number;
  encoding: 'column-major';
  cells: { position: number; source: WeightBinding }[];
  uses: NodeSource[];
  reads: SourcePath[];
  writes: SourcePath[];
}

/** Group proven board-cell assignments. Equal numerical Draws remain independent mathematical sources. */
export function cellBindings(graph: ModelGraph): CellBinding[] {
  const board = reelObject(graph.boardConfig), state = reelObject(graph.initialState), rows = Number(board?.rows), columns = Number(board?.columns), cells = rows * columns;
  if (!Number.isSafeInteger(rows) || rows < 1 || !Number.isSafeInteger(columns) || columns < 1 || cells > 64 || canonicalJson(state?.boardPositions) !== canonicalJson(Array.from({ length: cells }, (_, cell) => cell))) return [];
  const index = indexGraphSources(graph), weights = weightBindings(graph), bindings: CellBinding[] = [];
  for (const [scope, definition] of Object.entries(graph.mechanics ?? {})) {
    const first = weights.find(weight => weight.source.scope === scope && weight.capability === 'editable');
    if (!first) continue;
    const template = spinCells({ name: scope, cells, outcomes: first.rows.map(row => [row.outcomeId, Math.max(1, row.weight)]) }) as unknown as ModelGraph;
    const shape = matchReelShape(definition, template); if (!shape) continue;
    const assignments = Array.from({ length: cells }, (_, position) => ({ position, source: weights.find(weight => weight.key === sourceKey(scope, shape.nodes.get(`cell-${position + 1}`)!.id))! }));
    if (assignments.some(cell => !cell.source || cell.source.capability !== 'editable')) continue;
    const uses = index.libraryUses.filter(use => use.definition === scope).map(use => use.source);
    bindings.push({ version: 1, key: JSON.stringify(['cells', scope]), revision: canonicalJson({ board, positions: state?.boardPositions, definition, uses: uses.map(use => [use.key, use.node]) }),
      scope, rows, columns, encoding: 'column-major', cells: assignments, uses, reads: [['boardConfig'], ['initialState', 'boardPositions'], ['mechanics', scope], ...uses.map(use => use.path)],
      writes: assignments.map(cell => [...cell.source.source.path, 'drawWeights']) });
  }
  return bindings;
}

export function changeBoundCells(graph: ModelGraph, binding: CellBinding, edits: ReadonlyMap<string, ReadonlyMap<string, string>>): ModelGraph {
  const current = cellBindings(graph).find(source => source.key === binding.key);
  if (!current || current.revision !== binding.revision) throw new Error('The cell assignment or a shared use changed. Reconcile this draft.');
  let candidate = structuredClone(graph);
  for (const [key, values] of edits) {
    const cell = current.cells.find(cell => cell.source.key === key);
    if (!cell) throw new Error('Choose only cells assigned to this board source.');
    candidate = changeBoundWeights(candidate, cell.source, values);
  }
  return candidate;
}
