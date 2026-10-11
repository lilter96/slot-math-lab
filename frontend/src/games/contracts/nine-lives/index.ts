import model from '../../nine-lives-tgslots/model.json' with { type: 'json' };
import { BetConfiguration, type ClusterHit, type SpinResult } from '../slots-core';

/** Symbol names in id order. */
export const SYMBOLS: readonly string[] = model.symbols.regular;

export const COLUMNS: number = model.board.columns;
export const ROWS: number = model.board.rows;

const weights = (table: Record<string, number>) => SYMBOLS.map(name => table[name] ?? 0);

/** The tables of the game as the client shows them. */
export const config = {
  baseCost: model.game_metadata.cost_to_play,
  freeSpins: model.free_spins.spins,
  maxWinX: model.max_win,
  maxMultiplier: model.free_spins.max_multiplier,
  maxCascades: model.cascade.max_steps,
  buyCost: model.buy.cost_multiplier,
  weights: weights(model.cell_weights.paid_spin),
  bonusWeights: weights(model.cell_weights.free_spin),
  refillWeights: weights(model.cell_weights.refill),
  prizes: model.prizes.map(({ bets, weight }) => [bets, weight]),
  /** What a group of each size pays, by symbol name; a symbol that never pays has an empty row. */
  paytable: Object.fromEntries(SYMBOLS.map(name => [name, (model.cluster_paytable as Record<string, Record<string, number>>)[name] ?? {}])),
};

export const BET_CONFIG = BetConfiguration.fromBaseCost(config.baseCost);

export interface LivesState {
  phase: 'BASE' | 'FREE';
  triggeringMultiplier: number;
  remaining: number;
  multiplier: number;
  roundWin: number;
  bonusWin: number;
  lastGrid: number[][];
}

/** One step of a cascade: the board before and after, the groups that paid and the cells they left. */
export interface LivesStep {
  before: number[][];
  after: number[][];
  hits: readonly ClusterHit[];
  vanished: readonly number[];
  multiplier: number;
  win: number;
}

export interface LivesCoin {
  position: number;
  value: number;
}

export interface LivesResult extends SpinResult {
  type: 'BASE' | 'BUY' | 'FREE';
  grid: number[][];
  finalGrid: number[][];
  steps: LivesStep[];
  coins: LivesCoin[];
  collectionWin: number;
  clusterWin: number;
  scatters: number;
  bonusTriggered: boolean;
  bonusEnded: boolean;
  remaining: number;
  multiplier: number;
  roundWin: number;
  capped: boolean;
}

export type LivesAction = 'state' | 'spin' | 'buybonus' | 'next';

/** The state of a session nobody has played in yet. */
export function initialState(): LivesState {
  return {
    phase: 'BASE', triggeringMultiplier: 1, remaining: 0, multiplier: 1, roundWin: 0, bonusWin: 0,
    lastGrid: Array.from({ length: COLUMNS }, (_, reel) => Array.from({ length: ROWS }, (_, row) => 1 + ((reel * 2 + row) % 5))),
  };
}
