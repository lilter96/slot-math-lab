import config from './model.json' with { type: 'json' };

export { config };
export const SYMBOLS = ['WILD', 'BONE', 'FISH', 'YARN', 'CANDLE', 'CAT', 'COIN', 'SCATTER'] as const;
export interface LivesState {
  phase: 'BASE' | 'FREE';
  triggeringMultiplier: number;
  remaining: number;
  multiplier: number;
  roundWin: number;
  bonusWin: number;
  lastGrid: number[][];
}
export interface LivesStep {
  before: number[][];
  after: number[][];
  hits: { symbolId: number; symbolName: string; size: number; basePayout: number; totalPayout: number; positions: number[] }[];
  vanished: number[];
  multiplier: number;
  win: number;
}
export interface LivesResult {
  type: 'BASE' | 'BUY' | 'FREE';
  win: number;
  grid: number[][];
  finalGrid: number[][];
  steps: LivesStep[];
  coins: { position: number; value: number }[];
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
export interface Response {
  balance: number;
  revision: number;
  state: LivesState;
  result?: LivesResult;
  replay?: { configHash: string; seed: number; index: number; buy: boolean };
}
