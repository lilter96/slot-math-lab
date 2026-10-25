import { ROWS, SYMBOLS, config, initialState, type LivesResult, type LivesState, type LivesStep } from '../contracts/nine-lives';
import { createNineLivesGraph, nineLivesModel as model } from '../nine-lives-tgslots/graph';
import { credits, lists, number, numbers, reels, remembered, type Game, type Step } from './game';

const SCATTER = SYMBOLS.indexOf(model.symbols.scatter);
const graph = remembered((buy: boolean) => createNineLivesGraph(undefined, { buy }));

/** The cells a list of marks marks: the graphs keep a set of cells as one mark per cell of the board. */
const marked = (marks: readonly number[]) => marks.flatMap((mark, cell) => mark ? [cell] : []);

/**
 * What the client is shown when the bonus is bought. Nothing is drawn for it: the resting board, with the
 * scatters that would have started the free spins on its four corners.
 */
function boughtGrid(): number[][] {
  const cells = initialState().lastGrid.flat();
  for (const corner of [0, ROWS * 2 - 1, cells.length - ROWS * 2, cells.length - 1]) cells[corner] = SCATTER;
  return reels(cells, ROWS);
}

/**
 * Nine Lives as its original server presented it: a paid spin or a bought bonus, then one request per free spin.
 * A spin is answered whole: the board it landed on, the coins collected and every step of its cascade.
 */
export const nineLives: Game<LivesState, LivesResult> = {
  id: 'nine-lives',
  wallet: {
    initialBalance: 1_000_000,
    cost: (action, _, payload) => action === 'next' || action === 'state' ? 0
      : config.baseCost * (payload.multiplier as number) * (action === 'buybonus' ? config.buyCost : 1),
    award: result => result.win,
    identified: (requestId, expectedRevision) => requestId !== undefined && requestId.length <= 128 && Number.isSafeInteger(expectedRevision) && expectedRevision! >= 0,
    fingerprint: (action, payload, expectedRevision) => [action, payload, expectedRevision],
    messages: {
      noSession: 'Create a session first',
      expired: 'Session not found or expired',
      identity: 'Request identity and revision are required',
      reused: 'Request identity was reused for a different command',
      stale: 'Session revision changed; refresh before continuing',
    },
  },
  initialState,

  validate(state, action, payload) {
    if (action === 'state') return null;
    if (action === 'next') return state.phase === 'FREE' ? null : 'No free spins remaining';
    if (action !== 'spin' && action !== 'buybonus') return `Unknown action: ${action}`;
    const multiplier = payload.multiplier;
    if (!Number.isSafeInteger(multiplier) || (multiplier as number) < 1 || (multiplier as number) > 10000) return 'Bet multiplier must be an integer from 1 to 10000';
    return state.phase === 'BASE' ? null : 'Complete the nine lives first';
  },

  graph: ({ action }) => action === 'spin' ? graph(false) : action === 'buybonus' ? graph(true) : null,

  steps(round, { action, payload }) {
    const { trace } = round;
    const multiplier = payload.multiplier as number, buy = action === 'buybonus';
    // Every spin of the round: the board it landed on and the one it ended on, its coins, and the multiplier it started with.
    const boards = lists(trace.spinBoards), finals = lists(trace.spinFinalBoards), coinValues = lists(trace.spinCoins);
    const collected = numbers(trace.spinCollected), starts = numbers(trace.spinMultipliers);
    // Every step of a cascade that paid: its spin, the board before and after, the cells removed and the groups.
    const stepSpins = numbers(trace.stepSpins), stepMultipliers = numbers(trace.stepMultipliers), stepWins = numbers(trace.stepWins);
    const before = lists(trace.cascadeBoards), after = lists(trace.cascadeAfter), removed = lists(trace.cascadeRemoved);
    const hitSymbols = lists(trace.stepHitSymbols), hitSizes = lists(trace.stepHitSizes), hitPays = lists(trace.stepHitPays);
    const hitCells = (trace.stepHitCells as unknown[]).map(lists);
    const most = number(trace.capCoins) * multiplier, total = credits(round, multiplier);

    /** One spin as the client is told it. A round pays what the graph paid for it, so what is past that is not paid. */
    const spun = (spin: number, type: 'BASE' | 'FREE', state: LivesState) => {
      let roundWin = state.roundWin;
      const paid = (amount: number) => {
        const awarded = Math.min(amount, total - roundWin);
        roundWin += awarded;
        return awarded;
      };
      const coins = marked(coinValues[spin]).map(position => ({ position, value: coinValues[spin][position] * multiplier }));
      const collectionWin = paid(collected[spin] * multiplier);
      const steps: LivesStep[] = stepSpins.flatMap((of, step) => of === spin ? [step] : []).map(step => ({
        before: reels(before[step], ROWS), after: reels(after[step], ROWS),
        hits: hitSymbols[step].map((symbolId, hit) => ({ symbolId, symbolName: SYMBOLS[symbolId], size: hitSizes[step][hit],
          basePayout: hitPays[step][hit], totalPayout: hitPays[step][hit], positions: marked(hitCells[step][hit]) })),
        vanished: marked(removed[step]), multiplier: stepMultipliers[step], win: paid(stepWins[step] * multiplier),
      }));
      const clusterWin = steps.reduce((sum, step) => sum + step.win, 0), win = collectionWin + clusterWin;
      const last = spin === boards.length - 1, finalGrid = reels(finals[spin], ROWS);
      // The spins a paid spin triggers are the rest of the round its graph played.
      const triggered = type === 'BASE' && !last, ended = type === 'FREE' && last;
      const next: LivesState = type === 'BASE'
        ? { ...state, phase: triggered ? 'FREE' : 'BASE', remaining: triggered ? config.freeSpins : 0, multiplier: 1, roundWin, lastGrid: finalGrid }
        : { ...state, phase: ended ? 'BASE' : 'FREE', remaining: ended ? 0 : state.remaining - 1, roundWin, bonusWin: state.bonusWin + win, lastGrid: finalGrid,
          // The multiplier the steps of this spin have built is the one the next spin starts with.
          multiplier: last ? number(trace.multiplier) : starts[spin + 1] };
      const result: LivesResult = { type, win, grid: reels(boards[spin], ROWS), finalGrid, steps, coins, collectionWin, clusterWin,
        scatters: boards[spin].filter(symbol => symbol === SCATTER).length, bonusTriggered: triggered, bonusEnded: ended,
        remaining: next.remaining, multiplier: next.multiplier, roundWin, capped: roundWin >= most };
      return { result, state: next };
    };

    const steps: Step<LivesState, LivesResult>[] = [{
      action,
      reply: (_, state) => {
        if (!buy) return spun(0, 'BASE', { ...state, triggeringMultiplier: multiplier, roundWin: 0, bonusWin: 0, multiplier: 1 });
        const grid = boughtGrid();
        return {
          result: { type: 'BUY', win: 0, grid, finalGrid: grid, steps: [], coins: [], collectionWin: 0, clusterWin: 0, scatters: model.free_spins.trigger,
            bonusTriggered: true, bonusEnded: false, remaining: config.freeSpins, multiplier: 1, roundWin: 0, capped: false },
          state: { ...initialState(), triggeringMultiplier: multiplier, phase: 'FREE', remaining: config.freeSpins, lastGrid: grid },
        };
      },
    }];
    for (let spin = buy ? 0 : 1; spin < boards.length; spin++) steps.push({ action: 'next', reply: (_, state) => spun(spin, 'FREE', state) });
    return steps;
  },
};
