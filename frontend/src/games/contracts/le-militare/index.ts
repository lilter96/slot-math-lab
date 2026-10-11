import model from '../../le-militare/model.json' with { type: 'json' };
import { BetConfiguration, type ClusterHit, type SpinResult } from '../slots-core';

/** A bet multiplier of 1 is a bet of 1 credit; prices and wins are fractions of it. */
export const BET_CONFIG = BetConfiguration.fromBaseCost(1);

/** Symbol names in id order. */
export const SYM_NAMES: readonly string[] = model.symbols.regular;

export const Symbols: Record<string, number> = Object.fromEntries(SYM_NAMES.map((name, id) => [name, id]));

export type ModeId = 'recon' | 'assault' | 'siege';
export const MODE_IDS = ['recon', 'assault', 'siege'] as const;
export const DEFAULT_MODE = model.default_mode as ModeId;

export type BuyOptionId = 'standard' | 'elite' | 'super';
export type FeatureOptionId = BuyOptionId | 'chance' | 'airraid';

/** What a bought round costs in the mode, in bets: the menu price and the amount the client deducts. */
export function getFeatureBuyCost(mode: ModeId, option: string): number {
  const prices: Readonly<Record<string, number>> = model.modes[mode].prices;
  const price = prices[option === 'airraid' ? 'air_raid' : option];
  if (price === undefined) throw new Error(`Unknown Le Militare feature: ${option}`);
  return price / model.game_metadata.cost_to_play;
}

export const BUY_BONUS_COST_MULTIPLIER: number = getFeatureBuyCost(DEFAULT_MODE, 'standard');

/** A plane shot down by an armed reel: where it was, and the multiplier it left. */
export interface ShootdownEvent {
  readonly reel: number;
  readonly row: number;
  readonly multiplier: number;
}

/** A wild with a multiplier that an air raid dropped on a cell. */
export interface AirRaidPlacement {
  readonly reel: number;
  readonly row: number;
  readonly multiplier: number;
}

/** The air raid of an opening spin: its wilds, and the board as it was before they landed. */
export interface AirRaidPresentation {
  readonly squadronSize: number;
  readonly placements: readonly AirRaidPlacement[];
  readonly preRaidGrid: number[][];
}

/** A reel that armed on a launcher and turned wild. */
export interface ActivationEvent {
  readonly reel: number;
  readonly convertedCells: number;
}

export interface CombatCascadeStep {
  readonly preCombatGrid: number[][];
  readonly postCombatGrid: number[][];
  readonly hits: readonly ClusterHit[];
  readonly vanishedPositions: readonly number[];
  readonly stickyWildPositions: readonly number[];
  readonly stepWin: number;
  readonly activations: readonly ActivationEvent[];
  readonly shootdowns: readonly ShootdownEvent[];
}

interface FreeSpinSummary {
  freeSpinsLeft: number;
  totalFreeSpinWin: number;
  sessionMultiplierSum: number;
}

interface LeMilitareSpin extends SpinResult {
  scatterCount: number;
  freeSpinsAwarded: number;
  steps: CombatCascadeStep[];
  multiplierSum: number;
  finalWin: number;
  state: FreeSpinSummary;
}

export interface LeMilitareBaseResult extends LeMilitareSpin {
  type: 'BASE';
  triggeredFreeSpins: boolean;
  airRaid: AirRaidPresentation | null;
}

export interface LeMilitareFreeResult extends LeMilitareSpin {
  type: 'FREE';
  retriggered: boolean;
}

export interface LeMilitareBuyResult extends LeMilitareSpin {
  type: 'BUY';
  triggeredFreeSpins: true;
}

export type LeMilitareResult = LeMilitareBaseResult | LeMilitareFreeResult | LeMilitareBuyResult;
