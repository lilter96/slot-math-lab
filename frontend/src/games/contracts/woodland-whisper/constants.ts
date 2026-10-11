import model from '../../woodland-whisper/model.json' with { type: 'json' };
import { BetConfiguration } from '../slots-core';

export const BET_CONFIG = BetConfiguration.fromLineCount(model.game_metadata.lines);

/** Symbol names in id order. */
export const SYM_NAMES: readonly string[] = model.symbols.regular;

export const Symbols: Record<string, number> = Object.fromEntries(SYM_NAMES.map((name, id) => [name, id]));

/** The row every payline crosses on every reel, line after line. */
export const PAYLINE_DATA = new Uint8Array(model.paylines.flat());

export const BUY_BONUS_COST_MULTIPLIER: number = model.feature.buy_bonus_cost_multiplier;
export const FREE_SPIN_TRIGGER: number = model.feature.trigger.min_count;

/** The free spin awards of the pick bonus; the board shows every one of them twice. */
export const PICK_BONUS_AWARDS: readonly number[] = model.feature.pick_bonus.map(([award]) => award);
