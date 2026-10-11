import { at, cellSet, choose, except, foldOver, intersect, moved, num, state, toNumber, union, type Expr } from '../expr';
import { Flow, type Mechanic } from '../flow';

export interface SymbolBoard {
  /** How many cells the board has, at most 52. */
  cells: number;
  /** How many symbols the game has; symbol ids run from 0. */
  symbolCount: number;
}

// The board is read in one pass. A symbol number is written in binary digits, and what the pass keeps is, for
// every digit, the set of cells whose symbol has it. The cells of a symbol are then the cells that have exactly
// its digits. A pass reads a part of the board: one whole number holds the sets of all the digits for that part.
const digitsOf = ({ symbolCount }: SymbolBoard) => Math.max(1, Math.ceil(Math.log2(symbolCount)));
const partSize = (board: SymbolBoard) => Math.min(board.cells, Math.floor(52 / digitsOf(board)));
const partsOf = (board: SymbolBoard) => Math.ceil(board.cells / partSize(board));
const digitKey = (digit: number) => `symbolDigit${digit}`;
const partKey = (part: number) => `boardPart${part}`;
const all = ({ cells }: SymbolBoard) => Array.from({ length: cells }, (_, cell) => cell);

/** State the `read-board` subgraph reads and writes. */
export function symbolCellState(board: SymbolBoard) {
  const digits = digitsOf(board), size = partSize(board);
  return {
    // What a symbol adds for the first cell of a part: digit d of its number, placed `d` parts of bits on.
    symbolDigits: Array.from({ length: board.symbolCount }, (_, symbol) =>
      Array.from({ length: digits }, (_, digit) => (symbol >> digit & 1) * 2 ** (digit * size)).reduce((sum, bit) => sum + bit, 0)),
    ...Object.fromEntries(Array.from({ length: partsOf(board) }, (_, part) => [partKey(part), all(board).slice(part * size, (part + 1) * size)])),
    ...Object.fromEntries(Array.from({ length: partsOf(board) }, (_, part) => [`${partKey(part)}Digits`, 0])),
    ...Object.fromEntries(Array.from({ length: digits }, (_, digit) => [digitKey(digit), 0])),
  };
}

/**
 * Reads `board` once, so that the cells of any symbol can be found without looking at the board again:
 * `cellsShowing` gives them. It has to run again whenever the board has changed.
 */
export function readBoard(board: SymbolBoard): Mechanic {
  const digits = digitsOf(board), size = partSize(board), parts = partsOf(board);
  const flow = new Flow();
  for (let part = 0; part < parts; part++)
    flow.write(`part-${part + 1}`, `Read cells ${part * size + 1} to ${Math.min(board.cells, (part + 1) * size)}: the digits of their symbol numbers`, `${partKey(part)}Digits`,
      foldOver(partKey(part), num(0), union(state('acc'), moved(at('symbolDigits', toNumber(at('board', state('item')))), state('place'))), { index: 'place', itemType: 'Number' }));
  for (let digit = 0; digit < digits; digit++)
    flow.write(`digit-${digit + 1}`, `The cells whose symbol number has binary digit ${digit + 1}`, digitKey(digit),
      union(...Array.from({ length: parts }, (_, part) =>
        moved(intersect(moved(state(`${partKey(part)}Digits`), -digit * size), num(2 ** size - 1)), part * size))));
  return flow.mechanic('read-board', 'Reads the board once and notes which cells show which symbol, so that the cells of a symbol are found without reading the board again.');
}

/** The set of cells that showed the symbol with this id when the board was last read. */
function cellsOfSymbol(board: SymbolBoard, symbol: number): Expr {
  return Array.from({ length: digitsOf(board) }, (_, digit) => digit).reduce<Expr>((cells, digit) =>
    (symbol >> digit & 1 ? intersect : except)(cells, state(digitKey(digit))), cellSet(all(board)));
}

/** The set of cells that showed `symbol` when the board was last read: a symbol id, or an expression that gives one as text. */
export const cellsShowing = (board: SymbolBoard, symbol: number | Expr): Expr => typeof symbol === 'number'
  ? cellsOfSymbol(board, symbol)
  : choose(toNumber(symbol), Array.from({ length: board.symbolCount }, (_, id) => cellsOfSymbol(board, id)));
