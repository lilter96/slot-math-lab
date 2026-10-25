import model from '../../x7-club/model.json' with { type: 'json' };
import { BetConfiguration, type PaylineHit, type SpinResult } from '../slots-core';

/** Symbol names in id order. */
export const SYMBOLS: readonly string[] = model.symbols.regular;

/** The row every payline crosses on every reel. */
export const PAYLINES: readonly (readonly number[])[] = model.paylines;

const pays: Record<string, Record<string, number>> = model.paytable;

/** The tables of the game as the client shows them. */
export const config = {
  baseCost: model.game_metadata.cost_to_play,
  buyCost: model.buy.cost_multiplier,
  maxWinX: model.max_win,
  symbols: SYMBOLS,
  weights: SYMBOLS.map(name => (model.cell_weights as Record<string, number>)[name]),
  /** One row per paying symbol in id order: what three, four and five of it pay. */
  paytable: SYMBOLS.filter(name => pays[name]).map(name => [3, 4, 5].map(count => pays[name][count])),
  prizes: model.prizes.map(({ tier, bets, weight }) => [tier, bets, weight] as [PrizeTier, number, number]),
  buyEntryCredits: model.buy.entry_credits,
};

export const BET_CONFIG = BetConfiguration.fromLineCount(config.baseCost);

export type PrizeTier = 'CREDIT' | 'MINI' | 'MAJOR' | 'MEGA';
export type BoostKind = 'STOP' | 'PLUS1' | 'PLUS2' | 'X7';

export interface ClubCoin {
  position: number;
  value: number;
  tier: PrizeTier;
}

/** The Hold & Spin bonus: the coins held, the respins left, and the columns whose boosts are done or waiting. */
export interface ClubBonus {
  coins: ClubCoin[];
  respins: number;
  boostedColumns: number[];
  pendingColumns: number[];
  boostPulls: number;
}

export interface ClubState {
  phase: 'BASE' | 'HOLD' | 'BOOST';
  triggeringMultiplier: number;
  roundWin: number;
  bonus: ClubBonus | null;
  lastGrid: number[][];
}

export interface ClubResult extends SpinResult {
  type: 'BASE' | 'BUY' | 'RESPIN';
  grid: number[][];
  hits: readonly PaylineHit[];
  coins: ClubCoin[];
  newCoins: ClubCoin[];
  respins: number;
  bonusTriggered: boolean;
  bonusEnded: boolean;
  bonusTotal: number;
  boost?: { column: number; kind: BoostKind; finished: boolean };
  capped: boolean;
}

export interface ClubResponse {
  sessionId: string;
  balance: number;
  revision: number;
  state: ClubState;
  result?: ClubResult;
}

export type ClubAction = 'state' | 'spin' | 'buybonus' | 'next';

/** The state of a session nobody has played in yet. */
export function initialState(): ClubState {
  return { phase: 'BASE', triggeringMultiplier: 1, roundWin: 0, bonus: null, lastGrid: Array.from({ length: 5 }, () => [0, 1, 2]) };
}
