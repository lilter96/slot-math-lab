import type { PaylineHit } from '../contracts/slots-core';
import { lists, numbers, reels, type Trace } from './game';

/** One spin of a payline game as its graph traced it. Amounts are in credits at a bet multiplier of 1. */
export interface LineSpin {
  grid: number[][];
  hits: PaylineHit[];
  scatters: number;
  /** How much of `win` the scatters paid. */
  scatterWin: number;
  win: number;
}

export interface LineGame {
  rows: number;
  /** Symbol names in id order. */
  names: readonly string[];
  wild: number;
  /** The row every payline crosses on every reel. */
  paylines: readonly (readonly number[])[];
}

/**
 * The winning lines of one spin, from what the shared `paylines` subgraph leaves for every line. They come in
 * the order the original evaluator reports them: by the rows a line crosses, reel after reel.
 */
export function lineHits(payouts: readonly number[], symbols: readonly number[], counts: readonly number[], { names, wild, paylines }: Omit<LineGame, 'rows'>): PaylineHit[] {
  const order = paylines.map((_, line) => line).sort((a, b) => {
    const reel = paylines[a].findIndex((row, index) => row !== paylines[b][index]);
    return reel < 0 ? a - b : paylines[a][reel] - paylines[b][reel];
  });
  return order.filter(line => payouts[line] > 0).map(line => ({
    lineIndex: line,
    // A line of nothing but Wilds has no symbol of its own.
    symbolName: symbols[line] === wild ? 'WILD' : names[symbols[line]],
    matchCount: counts[line],
    basePayout: payouts[line],
    wildMultiplier: 1,
    totalPayout: payouts[line],
  }));
}

/** The spins of a round of a payline game, from the trace the shared `lineSpin` subgraph leaves. */
export function lineSpins(trace: Trace, game: LineGame): LineSpin[] {
  const payouts = lists(trace.spinLinePayouts), symbols = lists(trace.spinLineSymbols), counts = lists(trace.spinLineCounts);
  const scatters = numbers(trace.spinScatters), scatterWins = numbers(trace.spinScatterWins), wins = numbers(trace.spinWins);
  return lists(trace.spinBoards).map((board, spin) => ({
    grid: reels(board, game.rows),
    hits: lineHits(payouts[spin], symbols[spin], counts[spin], game),
    scatters: scatters[spin],
    scatterWin: scatterWins[spin],
    win: wins[spin],
  }));
}
