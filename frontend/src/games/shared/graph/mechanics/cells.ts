import { choose, mapOver, state } from '../expr';
import { Flow, type Mechanic, type Outcome } from '../flow';

export interface CellBoard {
  /** Subgraph name, e.g. `board`; a game with several kinds of spin names each of its boards. */
  name: string;
  /** How many cells the board has. */
  cells: number;
  /** The symbols a cell can show and their weights; every cell is drawn from the same list. */
  outcomes: readonly Outcome[];
}

/** The state keys the cells of a board are drawn into, in board order: `cell1`, `cell2` and so on. */
const cellKeys = (cells: number) => Array.from({ length: cells }, (_, cell) => `cell${cell + 1}`);

/** State the `spinCells` subgraph writes: the symbol drawn for every cell, and the board they make. */
export const cellState = ({ cells }: Pick<CellBoard, 'cells'>) => ({
  boardPositions: Array.from({ length: cells }, (_, cell) => cell),
  board: Array.from({ length: cells }, () => '0'), ...Object.fromEntries(cellKeys(cells).map(key => [key, '0'])),
});

/**
 * A board without reel strips: every cell draws its own symbol, independently of the others, and the symbols
 * drawn become `board`. Cells are drawn in board order, column by column from the top.
 */
export function spinCells({ name, cells, outcomes }: CellBoard): Mechanic {
  const flow = new Flow();
  cellKeys(cells).forEach((key, cell) => flow.draw(`cell-${cell + 1}`, `Draw the symbol of cell ${cell + 1}`, key, outcomes));
  return flow.write('board', 'The symbols drawn are the board', 'board', mapOver('boardPositions', choose(state('item'), cellKeys(cells).map(state)), { itemType: 'Number' }))
    .mechanic(name, `Draws a symbol for each of the ${cells} cells, every cell on its own, and puts them on the board.`);
}
