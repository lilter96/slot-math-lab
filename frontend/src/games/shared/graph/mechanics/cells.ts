import { append, emptyArray, state } from '../expr';
import { Flow, Mechanics, type Mechanic, type Outcome } from '../flow';

export interface CellBoard {
  /** Subgraph name, e.g. `board`; a game with several kinds of spin names each of its boards. */
  name: string;
  /** How many cells the board has. */
  cells: number;
  /** The symbols a cell can show and their weights; every cell is drawn from the same list. */
  outcomes: readonly Outcome[];
}

/** State the `spinCells` subgraph writes: the board, and the symbol of the cell being drawn. */
export const cellState = ({ cells }: Pick<CellBoard, 'cells'>) => ({ board: Array.from({ length: cells }, () => '0'), cellSymbol: '0' });

/**
 * A board without reel strips: every cell draws its own symbol, independently of the others, and the symbols
 * drawn become `board`. Cells are drawn in board order, column by column from the top.
 */
export function spinCells(mechanics: Mechanics, { name, cells, outcomes }: CellBoard): Mechanic {
  const cell = mechanics.add(new Flow()
    .draw('symbol', 'Draw the symbol the cell shows', 'cellSymbol', outcomes)
    .write('place', 'Put it on the board', 'board', append('board', state('cellSymbol')))
    .mechanic(`${name}-cell`, 'One cell of the board: its symbol is drawn and takes the next place on the board.'));
  const flow = new Flow().write('clear', 'Start with an empty board', 'board', emptyArray());
  for (let index = 0; index < cells; index++) flow.use(`cell-${index + 1}`, `Cell ${index + 1}`, cell);
  return flow.mechanic(name, `Draws a symbol for each of the ${cells} cells, every cell on its own, and puts them on the board.`);
}
