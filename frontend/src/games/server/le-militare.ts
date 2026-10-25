import { DEFAULT_MODE, MODE_IDS, SYM_NAMES, type CombatCascadeStep, type LeMilitareResult, type ModeId } from '../contracts/le-militare';
import type { LeMilitareSerializedState } from '../contracts/shared-contracts/states';
import { createLeMilitareGraph, leMilitareModel as model, type LeMilitareFeature } from '../le-militare/graph';
import { lists, number, numbers, reels, remembered, rowsOf, type Game, type Payload, type Step } from './game';

const ROWS = model.board.rows;
const PLANE = SYM_NAMES.indexOf(model.symbols.plane);
/** The graphs count in credits of a bet of this many; the client counts in bets. */
const BET = model.game_metadata.cost_to_play;
const TIERS = ['standard', 'elite', 'super'] as const;
type Tier = typeof TIERS[number];

const graph = remembered((key: string) => {
  const [mode, feature] = key.split(' ') as [string, LeMilitareFeature];
  return createLeMilitareGraph(undefined, { mode, feature });
});

/** The mode a round is played in: the one its request names, or else the one the session was last played in. */
const modeOf = (state: LeMilitareSerializedState, payload: Payload): ModeId => (payload.mode as ModeId | undefined) || state.mode || DEFAULT_MODE;

/** What kind of round a request starts; null for a request that continues one. */
function featureOf(action: string, payload: Payload): LeMilitareFeature | null {
  switch (action) {
    case 'spin': return 'spin';
    case 'chancespin': return 'chance';
    case 'airraidspin': return 'air-raid';
    case 'buybonus': return (payload.option as Tier | undefined) ?? 'standard';
    default: return null;
  }
}

/** The cells a list of marks marks: the graphs keep a set of cells as one mark per cell of the board. */
const marked = (marks: readonly number[]) => marks.flatMap((mark, cell) => mark ? [cell] : []);
/** A board the graphs keep reel after reel, as the rows the client shows. */
const shown = (cells: readonly number[]) => rowsOf(reels(cells, ROWS));
const cellOf = (cell: number) => ({ reel: Math.floor(cell / ROWS), row: cell % ROWS });

/**
 * Le Militare as its original server presented it: a paid spin, a bought spin or bought free spins, then one
 * request per free spin. A spin is answered whole: every board it looked at, with its combat and its groups.
 * The client counts in bets and the graphs in credits, so every amount is divided by the bet.
 */
export const leMilitare: Game<LeMilitareSerializedState, LeMilitareResult> = {
  id: 'le-militare',
  initialState: () => ({ lastGrid: null, freeSpins: null, roundWin: 0 }),

  validate(state, action, payload) {
    if (action === 'state') return null;
    if (action === 'freespin') return !state.freeSpins || state.freeSpins.spinsRemaining <= 0 ? 'No free spins remaining' : null;
    if (!featureOf(action, payload)) return `Unknown action: ${action}`;
    if (typeof payload.multiplier !== 'number' || payload.multiplier < 1) return 'multiplier must be a positive integer';
    if (action === 'buybonus' && payload.option !== undefined && !TIERS.includes(payload.option as Tier)) return `option must be one of: ${TIERS.join(', ')}`;
    if (payload.mode !== undefined && !MODE_IDS.includes(payload.mode as ModeId)) return `mode must be one of: ${MODE_IDS.join(', ')}`;
    return state.freeSpins && state.freeSpins.spinsRemaining > 0 ? 'Complete free spins before starting a new round' : null;
  },

  graph({ action, payload, state }) {
    const feature = featureOf(action, payload);
    return feature && graph(`${modeOf(state, payload)} ${feature}`);
  },

  steps({ trace }, { action, payload, state: opened }) {
    const multiplier = payload.multiplier as number, mode = modeOf(opened, payload), feature = featureOf(action, payload)!;
    const tier = TIERS.includes(feature as Tier) ? model.buy[feature as Tier] : null;
    const bets = (coins: number) => coins * multiplier / BET;
    // Every spin of the round: the board it landed on, and what it ended with.
    const boards = lists(trace.spinBoards), scatters = numbers(trace.spinScatters), multipliers = numbers(trace.spinMultipliers), totals = numbers(trace.spinTotals);
    // Every board a spin looked at, around its combat; and for the boards that paid, their groups and what left the board.
    const lookSpins = numbers(trace.lookSpins), before = lists(trace.lookBefore), after = lists(trace.lookAfter), shots = lists(trace.lookShots);
    const lookArmed = lists(trace.lookArmed), lookHeld = lists(trace.lookHeld);
    const stepSpins = numbers(trace.stepSpins), stepWins = numbers(trace.stepWins), stepHeld = lists(trace.stepHeld), removed = lists(trace.cascadeRemoved);
    const hitSymbols = lists(trace.stepHitSymbols), hitSizes = lists(trace.stepHitSizes), hitPays = lists(trace.stepHitPays);
    const hitCells = (trace.stepHitCells as unknown[]).map(lists);
    const awards = numbers(trace.freeSpinAwards), trigger = model.free_spins.trigger;
    const awardFor = (count: number) => awards[Math.min(count, awards.length - 1)];
    const capped = (spin: number) => totals[spin] >= number(trace.capCoins);
    const looksOf = (spin: number) => lookSpins.flatMap((of, look) => of === spin ? [look] : []);

    /** The boards one spin looked at. The boards that paid come first, and a cascade that ran out of steps has no other. */
    const cascadeOf = (spin: number): CombatCascadeStep[] => {
      const paid = stepSpins.flatMap((of, step) => of === spin ? [step] : []);
      return looksOf(spin).map((look, index): CombatCascadeStep => {
        const step: number | undefined = paid[index];
        return {
          preCombatGrid: shown(before[look]), postCombatGrid: shown(after[look]),
          hits: step === undefined ? [] : hitSymbols[step].map((symbolId, hit) => ({ symbolId, symbolName: SYM_NAMES[symbolId], size: hitSizes[step][hit],
            basePayout: hitPays[step][hit] / BET, totalPayout: hitPays[step][hit] / BET, positions: marked(hitCells[step][hit]) })),
          vanishedPositions: step === undefined ? [] : marked(removed[step]),
          // What is held once the symbols of a step that paid have fallen; on a board that paid nothing, what its combat left held.
          stickyWildPositions: marked(step === undefined ? lookHeld[look] : stepHeld[step]),
          stepWin: step === undefined ? 0 : stepWins[step] / BET,
          activations: marked(lookArmed[look]).map(reel => ({ reel, convertedCells: ROWS })),
          // While a reel is armed every plane on the board is shot down, and the multipliers were drawn in board order.
          shootdowns: shots[look].length ? marked(before[look].map(symbol => symbol === PLANE ? 1 : 0)).map((cell, plane) => ({ ...cellOf(cell), multiplier: shots[look][plane] })) : [],
        };
      });
    };
    const summary = (free: LeMilitareSerializedState['freeSpins']) =>
      ({ freeSpinsLeft: free?.spinsRemaining ?? 0, totalFreeSpinWin: free?.totalWin ?? 0, sessionMultiplierSum: free?.multiplierSum ?? 0 });

    const steps: Step<LeMilitareSerializedState, LeMilitareResult>[] = [{
      action,
      reply: () => {
        const win = bets(totals[0]), bonus = boards.length > 1;
        const spin = { win, components: { total: win }, scatterCount: scatters[0], steps: cascadeOf(0), multiplierSum: multipliers[0], finalWin: win };
        if (tier) {
          // Free spins bought outright are the spins of their tier, however many scatters the opening spin ends with.
          const bought = awardFor(tier.scatters);
          const freeSpins = { triggeringMultiplier: multiplier, spinsRemaining: bonus ? bought : 0, totalWin: 0,
            armedReels: marked(numbers(trace.startArmed)), multiplierSum: number(trace.startMultiplier) };
          return { result: { type: 'BUY', ...spin, triggeredFreeSpins: true, freeSpinsAwarded: bought, state: summary(freeSpins) },
            state: { lastGrid: shown(boards[0]), freeSpins, mode, roundWin: win } };
        }
        const triggered = scatters[0] >= trigger, raided = 'raidBoards' in trace && lists(trace.raidBoards);
        // Scatters that come with a spin that has already paid the most a round may pay start nothing.
        const freeSpins = bonus ? { triggeringMultiplier: multiplier, spinsRemaining: awardFor(scatters[0]), totalWin: 0, armedReels: [], multiplierSum: 0 } : null;
        return {
          result: { type: 'BASE', ...spin, triggeredFreeSpins: triggered, freeSpinsAwarded: triggered ? awardFor(scatters[0]) : 0, state: summary(freeSpins),
            airRaid: raided && raided.length ? { squadronSize: numbers(trace.raidSquadrons)[0], preRaidGrid: shown(raided[0]),
              placements: numbers(trace.raidCells).map((cell, plane) => ({ ...cellOf(cell), multiplier: numbers(trace.raidMultipliers)[plane] })) } : null },
          state: { lastGrid: shown(boards[0]), freeSpins, mode, roundWin: win },
        };
      },
    }];
    for (let spin = 1; spin < boards.length; spin++) steps.push({
      action: 'freespin',
      reply: (_, state) => {
        const free = state.freeSpins!, win = bets(totals[spin] - totals[spin - 1]), cascade = cascadeOf(spin);
        const retriggered = !capped(spin) && scatters[spin] >= trigger, added = retriggered ? awardFor(scatters[spin]) : 0;
        // Reels stay armed in the order they armed.
        const armedReels = [...free.armedReels];
        for (const { activations } of cascade) for (const { reel } of activations) if (!armedReels.includes(reel)) armedReels.push(reel);
        const freeSpins = { ...free, armedReels, totalWin: free.totalWin + win, multiplierSum: multipliers[spin],
          // The free spins end with the round its graph played: when none are left, or when the round has paid the most it may.
          spinsRemaining: spin === boards.length - 1 ? 0 : free.spinsRemaining - 1 + added };
        return {
          result: { type: 'FREE', win, components: { total: win }, scatterCount: scatters[spin], retriggered, freeSpinsAwarded: added, steps: cascade,
            multiplierSum: multipliers[spin] - free.multiplierSum, finalWin: win, state: summary(freeSpins) },
          state: { lastGrid: shown(boards[spin]), freeSpins, mode: state.mode ?? DEFAULT_MODE, roundWin: state.roundWin + win },
        };
      },
    });
    return steps;
  },
};
