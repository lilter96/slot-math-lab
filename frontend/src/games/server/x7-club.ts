import { config, initialState, PAYLINES, SYMBOLS, type BoostKind, type ClubCoin, type ClubResult, type ClubState } from '../contracts/x7-club';
import { createX7ClubGraph, x7ClubModel as model } from '../x7-club/graph';
import { credits, lists, number, numbers, reels, remembered, type Game, type Step } from './game';
import { lineHits } from './lines';

const ROWS = model.hold.rows;
const COIN = SYMBOLS.indexOf(model.symbols.coin), WILD = SYMBOLS.indexOf(model.symbols.wild);
const graph = remembered((buy: boolean) => createX7ClubGraph(undefined, { buy }));

/** The most a round may pay, as the client is told when a round reaches it. */
const mostPaid = (multiplier: number) => config.maxWinX * config.baseCost * multiplier;

/** The tier a coin shows, which is the tier of the prize it landed with; a boost changes its value and not its tier. */
const tierOf = (prizeInBets: number) => config.prizes.find(([, bets]) => bets === prizeInBets)![0];

/**
 * X7 Club as its original server presented it: a paid spin or a bought bonus, then one request per step of the
 * Hold & Spin, which is a respin or one pull of the boost of a column that has just filled.
 */
export const x7Club: Game<ClubState, ClubResult> = {
  id: 'x7-club',
  wallet: {
    initialBalance: 1_000_000,
    cost: (action, _, payload) => action === 'next' ? 0 : config.baseCost * (payload.multiplier as number) * (action === 'buybonus' ? config.buyCost : 1),
    award: result => result.win,
    identified: (requestId, expectedRevision) => requestId !== undefined && requestId.length >= 8 && requestId.length <= 128 && expectedRevision !== undefined,
    fingerprint: (action, payload, expectedRevision) => [action, payload.multiplier ?? 0, expectedRevision],
    messages: {
      noSession: 'Create a session with state first',
      expired: 'Session expired; reload to start a new demo',
      identity: 'requestId and expectedRevision are required',
      reused: 'requestId was already used for another command',
      stale: 'Stale revision; refresh state',
    },
  },
  initialState,

  validate(state, action, payload) {
    if (action === 'state') return null;
    if (action !== 'spin' && action !== 'buybonus' && action !== 'next') return 'Unknown game action';
    if (action === 'next') return state.phase === 'BASE' ? 'No active bonus' : null;
    if (state.phase !== 'BASE') return 'Finish the active bonus first';
    const multiplier = payload.multiplier ?? 0;
    return !Number.isInteger(multiplier) || (multiplier as number) < 1 || (multiplier as number) > 10000 ? 'Multiplier must be 1..10000' : null;
  },

  graph: ({ action }) => action === 'spin' ? graph(false) : action === 'buybonus' ? graph(true) : null,

  steps(round, { action, payload }) {
    const { trace } = round;
    const multiplier = payload.multiplier as number, buy = action === 'buybonus';
    // What the Hold & Spin left, one entry per step: what happened, to which column, the coins held and the respins left.
    const events = (trace.holdEvents as string[]), columns = numbers(trace.holdColumns), respins = numbers(trace.holdRespins);
    const held = lists(trace.holdCoins).map(cells => cells.map(value => value * multiplier));
    const triggered = events.length > 0;
    const spinCoins = buy ? [] : numbers((trace.spinCoins as unknown[])[0]).map(value => value * multiplier);
    const coinsOf = (cells: readonly number[], tier: (position: number) => ClubCoin['tier']): ClubCoin[] =>
      cells.flatMap((value, position) => value > 0 ? [{ position, value, tier: tier(position) }] : []);
    const landed = (cells: readonly number[]) => coinsOf(cells, position => tierOf(cells[position] / (config.baseCost * multiplier)));
    const first = buy ? coinsOf(held[0], () => 'CREDIT') : landed(spinCoins);
    const grid = buy ? reels(held[0].map(value => value > 0 ? COIN : 0), ROWS) : reels(numbers((trace.spinBoards as unknown[])[0]), ROWS);
    // The round pays what the graph paid for it: the lines at once, and the rest when the bonus ends.
    const lineWin = buy ? 0 : Math.min(number(trace.lineCoins) * multiplier, credits(round, multiplier));
    const total = (coins: readonly ClubCoin[]) => coins.reduce((sum, coin) => sum + coin.value, 0);
    const pull = (step: number) => step < events.length && columns[step] >= 0;
    /** The columns whose boosts are still to come or running once step `step` has happened, in the order they are boosted. */
    const pending = (step: number) => {
      const waiting: number[] = [];
      for (let later = step + 1; pull(later); later++) if (!waiting.includes(columns[later])) waiting.push(columns[later]);
      // A pull that did not end the boost of its column is followed by another pull of it, so the column stays first.
      return waiting;
    };
    /** Every column boosted or queued for its boost by step `step`: a column is queued the moment it fills. */
    const boosted = (step: number) => {
      const queued: number[] = [];
      for (let at = 1; at < events.length && (at <= step || pull(at)); at++) if (pull(at) && !queued.includes(columns[at])) queued.push(columns[at]);
      return queued;
    };
    const pullsSoFar = (step: number) => {
      let pulls = 0;
      for (let at = step; pull(at) && columns[at] === columns[step]; at--) pulls++;
      return pulls;
    };
    const bonusAfter = (step: number, coins: ClubCoin[]) => {
      const waiting = pending(step);
      return { coins, respins: respins[step], boostedColumns: boosted(step), pendingColumns: waiting,
        boostPulls: pull(step) && waiting[0] === columns[step] ? pullsSoFar(step) : 0 };
    };

    const steps: Step<ClubState, ClubResult>[] = [{
      action,
      reply: () => {
        const bonus = triggered ? bonusAfter(0, first) : null;
        return {
          result: { type: buy ? 'BUY' : 'BASE', win: lineWin, grid, coins: first, newCoins: first, respins: bonus?.respins ?? 0,
            hits: buy ? [] : lineHits(numbers(trace.linePayouts), numbers(trace.lineSymbols), numbers(trace.lineCounts), { names: SYMBOLS, wild: WILD, paylines: PAYLINES }),
            bonusTriggered: triggered, bonusEnded: false, bonusTotal: total(first), capped: lineWin === mostPaid(multiplier) },
          state: { phase: !bonus ? 'BASE' : bonus.pendingColumns.length ? 'BOOST' : 'HOLD', triggeringMultiplier: multiplier, roundWin: lineWin, bonus, lastGrid: grid },
        };
      },
    }];
    for (let step = 1; step < events.length; step++) steps.push({
      action: 'next',
      reply: (_, state) => {
        const before = state.bonus!.coins, cells = held[step];
        // Held coins keep their place in the list and their tier; the coins of a respin join it in board order.
        const newCoins = pull(step) ? [] : landed(cells.map((value, position) => before.some(coin => coin.position === position) ? 0 : value));
        const coins = [...before.map(coin => ({ ...coin, value: cells[coin.position] })), ...newCoins];
        const bonus = bonusAfter(step, coins), ended = step === events.length - 1;
        const win = ended ? credits(round, multiplier) - state.roundWin : 0, roundWin = state.roundWin + win;
        return {
          result: { type: 'RESPIN', win, grid: state.lastGrid, hits: [], coins, newCoins, respins: bonus.respins, bonusTriggered: false, bonusEnded: ended,
            bonusTotal: total(coins), capped: roundWin === mostPaid(multiplier),
            ...(pull(step) ? { boost: { column: columns[step], kind: events[step] as BoostKind, finished: bonus.pendingColumns[0] !== columns[step] } } : {}) },
          state: { phase: ended ? 'BASE' : bonus.pendingColumns.length ? 'BOOST' : 'HOLD', triggeringMultiplier: state.triggeringMultiplier, roundWin,
            bonus: ended ? null : bonus, lastGrid: state.lastGrid },
        };
      },
    });
    return steps;
  },
};
