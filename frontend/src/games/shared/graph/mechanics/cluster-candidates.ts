import { cellSet, except, intersect, moved, state, union, type Expr } from '../expr';
import { Flow, type Mechanic } from '../flow';

export interface CandidateBoard { columns: number; rows: number; symbolCount: number }

export const candidateState = () => ({
  matchingAbove: 0, matchingBelow: 0, matchingLeft: 0, matchingRight: 0,
  internalCells: 0, junctionCells: 0, squareEnds: 0, candidateCells: 0, useCandidates: false,
});

/**
 * Selects seeds for every equal-symbol component of at least five cells in one pass over the bit planes.
 * Requires read-board first, and unpaidCells to contain only paying symbols. No wild substitution is made.
 * The caller must use ordinary first-cell traversal when wilds are present or ordered traces are requested.
 */
export function clusterCandidates({ columns, rows, symbolCount }: CandidateBoard): Mechanic {
  const cells = Array.from({ length: columns * rows }, (_, cell) => cell);
  const digits = Math.max(1, Math.ceil(Math.log2(symbolCount)));
  const sameNeighbor = (distance: number, boundary: Expr) => intersect(state('unpaidCells'), except(boundary,
    union(...Array.from({ length: digits }, (_, digit) => {
      const plane = state(`symbolDigit${digit}`), neighbor = moved(plane, distance);
      return union(except(plane, neighbor), except(neighbor, plane));
    }))));
  const pairs = (above: Expr, below: Expr, left: Expr, right: Expr) => ({
    vertical: union(above, below), horizontal: union(left, right),
    bothVertical: intersect(above, below), bothHorizontal: intersect(left, right),
  });
  const neighbors = pairs(state('matchingAbove'), state('matchingBelow'), state('matchingLeft'), state('matchingRight'));
  const internal = pairs(...[1, -1, rows, -rows].map((distance, direction) => intersect(
    state(['matchingAbove', 'matchingBelow', 'matchingLeft', 'matchingRight'][direction]), moved(state('internalCells'), distance),
  )) as [Expr, Expr, Expr, Expr]);
  const squareEnd = intersect(intersect(state('matchingAbove'), state('matchingLeft')),
    intersect(moved(state('matchingLeft'), 1), moved(state('matchingAbove'), rows)));
  const squareCells = union(state('squareEnds'), ...[1, rows, rows + 1].map(distance => moved(state('squareEnds'), -distance)));

  return new Flow()
    .write('above', 'Cells equal to the cell above them', 'matchingAbove', sameNeighbor(1, cellSet(cells.filter(cell => cell % rows > 0))))
    .write('left', 'Cells equal to the cell to their left', 'matchingLeft', sameNeighbor(rows, cellSet(cells.filter(cell => cell >= rows))))
    .write('below', 'The other ends of the vertical matching edges', 'matchingBelow', moved(state('matchingAbove'), -1))
    .write('right', 'The other ends of the horizontal matching edges', 'matchingRight', moved(state('matchingLeft'), -rows))
    .write('internal', 'Cells with at least two matching neighbors', 'internalCells',
      union(neighbors.bothVertical, neighbors.bothHorizontal, intersect(neighbors.vertical, neighbors.horizontal)))
    .write('junctions', 'Cells with at least three matching neighbors', 'junctionCells',
      union(intersect(neighbors.bothVertical, neighbors.horizontal), intersect(neighbors.bothHorizontal, neighbors.vertical)))
    .write('squares', 'Four-cell squares that have no connection beyond their square', 'squareEnds', except(squareEnd,
      union(state('junctionCells'), ...[1, rows, rows + 1].map(distance => moved(state('junctionCells'), distance)))))
    .write('candidates', 'Retain a seed in every component with five or more cells; discard smaller components', 'candidateCells', except(
      union(intersect(neighbors.bothVertical, neighbors.bothHorizontal),
        intersect(state('junctionCells'), union(internal.vertical, internal.horizontal)),
        internal.bothVertical, internal.bothHorizontal, intersect(internal.vertical, internal.horizontal)), squareCells))
    .mechanic('cluster-candidates', 'For boards without wild substitution: find components of at least five cells before flood fill. Binary symbol planes compare all neighboring symbols together. Isolated stars and squares are excluded.');
}
