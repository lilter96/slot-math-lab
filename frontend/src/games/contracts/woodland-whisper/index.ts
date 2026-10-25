import type { PaylineHit, SpinResult } from '../slots-core';

export { BET_CONFIG, BUY_BONUS_COST_MULTIPLIER, PAYLINE_DATA, SYM_NAMES, Symbols } from './constants';
export type { PaylineHit };

interface FreeSpinSummary {
  freeSpinsLeft: number;
  totalFreeSpinWin: number;
}

interface WoodlandWhisperSpin extends SpinResult {
  sc: number;
  scatterWin: number;
  grid: number[][];
  hits: PaylineHit[];
  /** The free spins the pick bonus of this spin awards; 0 without one. */
  pickedBonus: number;
  state: FreeSpinSummary;
}

export interface WoodlandWhisperBaseResult extends WoodlandWhisperSpin {
  type: 'BASE';
  triggeredPickBonus: boolean;
}

export interface WoodlandWhisperFreeResult extends WoodlandWhisperSpin {
  type: 'FREE';
  retriggeredPickBonus: boolean;
}

export interface WoodlandWhisperBuyResult extends WoodlandWhisperSpin {
  type: 'BUY';
  triggeredPickBonus: true;
}

export interface WoodlandWhisperPickResult extends SpinResult {
  type: 'PICK';
  pick: {
    userIndex: number;
    revealedIndex: number;
    value: number;
    isMatch: boolean;
    board: number[];
    picks: number[];
  };
  state: FreeSpinSummary;
}

export type WoodlandWhisperResult =
  | WoodlandWhisperBaseResult
  | WoodlandWhisperFreeResult
  | WoodlandWhisperPickResult
  | WoodlandWhisperBuyResult;
