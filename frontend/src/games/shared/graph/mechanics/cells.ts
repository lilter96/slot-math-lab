import {Flow, type Mechanic, type Outcome} from '../flow';

export interface CellBoard {
    /** Subgraph name, e.g. `board`; a game with several kinds of spin names each of its boards. */
    name: string;
    /** How many cells the board has. */
    cells: number;
    /** The symbols a cell can show and their weights; every cell is drawn from the same list. */
    outcomes: readonly Outcome[];
}

/** The board and its positions; scalar cell scratch fields are unnecessary. */
export const cellState = ({cells}: Pick<CellBoard, 'cells'>) => ({
    boardPositions: Array.from({length: cells}, (_, cell) => cell),
    board: Array.from({length: cells}, () => '0'),
});

/**
 * A board without reel strips: every cell draws its own symbol, independently of the others, and the symbols
 * drawn become `board`. Cells are drawn in board order, column by column from the top.
 */
export function spinCells({name, cells, outcomes}: CellBoard): Mechanic {
    return new Flow().drawMany('cells', 'Draw every cell in board order', 'board', cells, outcomes)
        .mechanic(name, `Draws a symbol for each of the ${cells} cells, every cell on its own, and puts them on the board.`);
}
