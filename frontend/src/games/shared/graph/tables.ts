import { shareOf } from './amounts';

/** Amounts a symbol pays other than by lines or scatters, for example the prizes a coin can carry. */
export interface PrizeTable {
  /** Paytable id, e.g. `coin-prizes`. */
  id: string;
  symbol: number;
  /** Every amount, in credits. The table numbers them 1, 2, 3… in this order. */
  credits: readonly number[];
}

export interface GameTables {
  /** Symbol names in id order. */
  names: readonly string[];
  wild: number;
  scatter?: number;
  /** A symbol that carries a prize of its own, such as a coin. */
  money?: number;
  /** `linePays[name][count]`, in line credits. */
  linePays?: Record<string, Record<string, number>>;
  /** `clusterPays[name][size]`, in credits: what a group of that many touching symbols pays. */
  clusterPays?: Record<string, Record<string, number>>;
  /** `scatterPays[count]`, in total bets. */
  scatterPays?: Record<string, number>;
  prizes?: readonly PrizeTable[];
  totalBet: number;
  /** What the round costs in credits: the total bet, or the price of a bought feature. */
  roundCost: number;
  /** `paylines[k][reel]` is the row line k crosses. */
  paylines?: readonly (readonly number[])[];
  rows: number;
  columns: number;
}

/**
 * The tables of a game: its symbols, board and paylines, and its paytables. A paytable lists every payout as an
 * exact share of what the round costs, which is the unit the round's win is reported in; the paytables are what
 * sets the precision of every win.
 */
export function gameTables({ names, wild, scatter, money, linePays, clusterPays, scatterPays, prizes = [], totalBet, roundCost, paylines, rows, columns }: GameTables) {
  const share = shareOf(roundCost);
  const entry = (symbol: number, counts: number[], credits: number[]) => ({ symbolId: String(symbol), counts, payouts: credits.map(share) });
  const counted = (symbol: number, pays: Record<string, number>, credits: (pay: number) => number) =>
    entry(symbol, Object.keys(pays).map(Number), Object.values(pays).map(credits));
  return {
    symbols: names.map((name, index) => ({ id: String(index), name,
      kind: index === wild ? 'Wild' : index === scatter ? 'Scatter' : index === money ? 'Money' : 'Standard' })),
    paytables: [
      ...(linePays ? [{ id: 'lines', entries: Object.entries(linePays).map(([name, pays]) => counted(names.indexOf(name), pays, pay => pay)) }] : []),
      ...(clusterPays ? [{ id: 'clusters', entries: Object.entries(clusterPays).map(([name, pays]) => counted(names.indexOf(name), pays, pay => pay)) }] : []),
      ...(scatter !== undefined && scatterPays ? [{ id: 'scatters', entries: [counted(scatter, scatterPays, pay => pay * totalBet)] }] : []),
      ...prizes.map(({ id, symbol, credits }) => ({ id, entries: [entry(symbol, credits.map((_, index) => index + 1), [...credits])] })),
    ],
    ...(paylines ? { paylineSets: [{ id: 'lines', paylines: paylines.map(positions => ({ positions })) }] } : {}),
    boardConfig: { rows, columns },
  };
}
