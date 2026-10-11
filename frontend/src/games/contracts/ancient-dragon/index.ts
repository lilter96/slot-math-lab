import type { PaylineHit, SpinResult } from '../slots-core';

export { BET_CONFIG } from './constants';

export interface AncientDragonBaseResult extends SpinResult {
  type: 'BASE';
  sc: number;
  triggeredFreeSpins: boolean;
  grid: number[][];
  hits: readonly PaylineHit[];
}

export interface AncientDragonFreeResult extends SpinResult {
  type: 'FREE';
  sc: number;
  retriggeredFreeSpins: boolean;
  grid: number[][];
  hits: readonly PaylineHit[];
}

export type AncientDragonResult = AncientDragonBaseResult | AncientDragonFreeResult;
