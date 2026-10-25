import type { AncientDragonResult } from '../contracts/ancient-dragon';
import { FREE_SPIN_TRIGGER, FREE_SPINS_PER_TRIGGER, SYM_NAMES, Symbols } from '../contracts/ancient-dragon/constants';
import type { AncientDragonSerializedState } from '../contracts/shared-contracts/states';
import { ancientDragonModel, createAncientDragonGraph } from '../ancient-dragon/graph';
import { remembered, type Game } from './game';
import { lineSpins } from './lines';

const graph = remembered(() => createAncientDragonGraph());

/** Ancient Dragon as its original server presented it: a paid spin, then the free spins it triggers, one per request. */
export const ancientDragon: Game<AncientDragonSerializedState, AncientDragonResult> = {
  id: 'ancient-dragon',
  initialState: () => ({ freeSpins: null }),

  validate(state, action, payload) {
    switch (action) {
      case 'spin':
        return typeof payload.multiplier !== 'number' || payload.multiplier < 1 ? 'multiplier must be a positive integer' : null;
      case 'freespin':
        return !state.freeSpins || state.freeSpins.spinsRemaining <= 0 ? 'No free spins remaining' : null;
      case 'state':
        return null;
      default:
        return `Unknown action: ${action}`;
    }
  },

  graph: ({ action }) => action === 'spin' ? graph(null) : null,

  steps({ trace }, { payload }) {
    const multiplier = payload.multiplier as number;
    const spins = lineSpins(trace, { rows: 3, names: SYM_NAMES, wild: Symbols[ancientDragonModel.symbols.wild], paylines: ancientDragonModel.paylines });
    return spins.map((spin, index) => {
      const shown = { win: spin.win * multiplier, sc: spin.scatters, grid: spin.grid, hits: spin.hits };
      const triggered = spin.scatters >= FREE_SPIN_TRIGGER;
      if (index === 0) return {
        action: 'spin',
        reply: () => ({
          result: { type: 'BASE' as const, win: shown.win, sc: shown.sc, triggeredFreeSpins: triggered, grid: shown.grid, hits: shown.hits },
          state: { freeSpins: triggered ? { triggeringMultiplier: multiplier, totalWin: 0, spinsRemaining: FREE_SPINS_PER_TRIGGER } : null },
        }),
      };
      return {
        action: 'freespin',
        reply: (_, state) => ({
          result: { type: 'FREE' as const, win: shown.win, sc: shown.sc, retriggeredFreeSpins: triggered, grid: shown.grid, hits: shown.hits },
          state: { freeSpins: {
            triggeringMultiplier: state.freeSpins!.triggeringMultiplier,
            totalWin: state.freeSpins!.totalWin + shown.win,
            // The graph ends a round at its free spin limit, which the original game did not have.
            spinsRemaining: index === spins.length - 1 ? 0 : state.freeSpins!.spinsRemaining - 1 + (triggered ? FREE_SPINS_PER_TRIGGER : 0),
          } },
        }),
      };
    });
  },
};
