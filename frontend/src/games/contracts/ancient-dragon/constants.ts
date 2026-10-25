import model from '../../ancient-dragon/model.json' with { type: 'json' };
import { BetConfiguration } from '../slots-core';

export const BET_CONFIG = BetConfiguration.fromLineCount(model.game_metadata.lines);

/** Symbol names in id order. */
export const SYM_NAMES: readonly string[] = model.symbols.regular;

export const Symbols: Record<string, number> = Object.fromEntries(SYM_NAMES.map((name, id) => [name, id]));

/** The row every payline crosses on every reel, line after line. */
export const PAYLINE_DATA = new Uint8Array(model.paylines.flat());

export const FREE_SPINS_PER_TRIGGER: number = model.feature.free_spins_per_trigger;
export const FREE_SPIN_TRIGGER: number = model.feature.trigger.min_count;
