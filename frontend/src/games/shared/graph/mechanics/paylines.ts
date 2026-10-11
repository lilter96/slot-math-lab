import { add, at, eq, itemAt, neq, num, or, param, state, toNumber, when, type Expr } from '../expr';
import { Flow, Mechanics, type Mechanic } from '../flow';

export interface LinePays {
  /** Symbol ids in id order. */
  symbolCount: number;
  wild: number;
  /** `pays[symbol][count]`, in line credits. A missing entry pays nothing. */
  pays: Record<number, Record<number, number>>;
  /** How many reels a line crosses. */
  reels?: number;
}

/**
 * The paytable for `linePays`: one row per symbol id, one column per matched count (0 to every reel).
 * A line made only of Wilds has no symbol of its own: it is paid as the best-paying symbol of that
 * length, which is what the Wild's row holds.
 */
export function linePayTable({ symbolCount, wild, pays, reels = 5 }: LinePays): number[][] {
  const table = Array.from({ length: symbolCount }, () => Array.from({ length: reels + 1 }, () => 0));
  for (const [symbol, row] of Object.entries(pays))
    for (const [count, pay] of Object.entries(row)) if (Number(count) <= reels) table[Number(symbol)][Number(count)] = pay;
  if (!pays[wild])
    for (let count = 1; count <= reels; count++)
      table[wild][count] = Math.max(0, ...Object.values(pays).map(row => row[count] ?? 0));
  return table;
}

/**
 * One payline, read left to right. Parameters `p0`, `p1`, … are the board cells the line crosses, one per reel
 * (five unless `reels` says otherwise). Reads `board`, `wildSymbol` and `linePays`; adds the line's payout to `lineCoins`.
 */
export function payline(reels = 5): Mechanic {
  const cell = (reel: number) => at('board', param(`p${reel}`));
  const wild = state('wildSymbol');
  // The line pays as its first symbol that is not a Wild; a line of Wilds pays as a Wild line.
  const symbol = Array.from({ length: reels }, (_, reel) => reel).reduceRight<Expr>(
    (rest, reel) => reel === reels - 1 ? cell(reel) : when(neq(cell(reel), wild), cell(reel), rest), cell(reels - 1));
  const continues = (reel: number) => or(eq(cell(reel), state('lineSymbol')), eq(cell(reel), wild));
  const count = Array.from({ length: reels - 1 }, (_, i) => reels - 1 - i).reduce<Expr>(
    (longer, reel) => when(continues(reel), longer, num(reel)), num(reels));
  return new Flow()
    .write('symbol', 'Which symbol the line pays as', 'lineSymbol', symbol)
    .write('count', 'How many reels match from the left', 'lineCount', count)
    .write('pay', 'Look the payout up: the symbol\'s row, the count\'s column', 'linePay', itemAt(at('linePays', toNumber(state('lineSymbol'))), state('lineCount')))
    .write('total', 'Add it to the spin\'s line win', 'lineCoins', add(state('lineCoins'), state('linePay')))
    .record('trace-pay', 'Remember this line\'s payout', 'linePayouts', state('linePay'))
    .record('trace-symbol', 'Remember this line\'s symbol', 'lineSymbols', state('lineSymbol'))
    .record('trace-count', 'Remember this line\'s length', 'lineCounts', state('lineCount'))
    .mechanic('payline', 'One payline, left to right: the first symbol that is not a Wild sets what the line pays as, Wilds stand in for it, and the longest run from the first reel is looked up in the paytable.');
}

/** Evaluates every payline of the game on `board` and leaves their sum in `lineCoins`. `lines[k][reel]` is the row line k crosses. */
export function paylines(mechanics: Mechanics, lines: readonly (readonly number[])[], rows: number): Mechanic {
  const line = mechanics.add(payline(lines[0].length));
  const flow = new Flow().write('reset', 'Start the line win at zero', 'lineCoins', num(0));
  for (const key of ['linePayouts', 'lineSymbols', 'lineCounts']) flow.write(`clear-${key}`, `Clear ${key}`, key, state('emptyArray'));
  lines.forEach((path, index) => flow.use(`line-${index + 1}`, `Payline ${index + 1}`, line,
    Object.fromEntries(path.map((row, reel) => [`p${reel}`, reel * rows + row]))));
  return flow.mechanic('paylines', `All ${lines.length} paylines of the game, each one the same payline subgraph placed on different board cells.`);
}

/** State the payline subgraphs read and write. */
export const paylineState = (wild: number, table: number[][]) => ({
  wildSymbol: String(wild), linePays: table, lineSymbol: '', lineCount: 0, linePay: 0, lineCoins: 0, linePayouts: [], lineSymbols: [], lineCounts: [],
});
