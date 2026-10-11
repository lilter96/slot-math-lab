import type { PaylineHit } from '../contracts/slots-core';
import type { WoodlandWhisperPickResult, WoodlandWhisperResult } from '../contracts/woodland-whisper';
import { FREE_SPIN_TRIGGER, PICK_BONUS_AWARDS, SYM_NAMES, Symbols } from '../contracts/woodland-whisper/constants';
import type { WoodlandWhisperSerializedState, WWPickBonusSerialized } from '../contracts/shared-contracts/states';
import { createWoodlandWhisperGraph, woodlandWhisperModel as model } from '../woodland-whisper/graph';
import { numbers, remembered, rowsOf, showing, type Game, type Step } from './game';
import { lineSpins } from './lines';

type State = WoodlandWhisperSerializedState;

const ROWS = 3;
const WILD = Symbols[model.symbols.wild], SCATTER = Symbols[model.symbols.scatter], MYSTERY = Symbols.REPLACEMENT;
const graph = remembered((buy: boolean) => createWoodlandWhisperGraph(undefined, { buy }));

/** The pick bonuses of a round by the spin they follow: the award, and the amounts of the cards turned to find it. */
function pickBonuses(trace: Readonly<Record<string, unknown>>): Map<number, { award: number; turned: number[] }> {
  const reveals = numbers(trace.pickReveals), spinsBefore = numbers(trace.pickSpins);
  const bonuses = new Map<number, { award: number; turned: number[] }>();
  let next = 0;
  numbers(trace.pickAwards).forEach((award, bonus) => {
    // A bonus turns cards until an amount comes up for the second time.
    const turned: number[] = [];
    do turned.push(reveals[next++]); while (turned.indexOf(turned[turned.length - 1]) === turned.length - 1);
    bonuses.set(spinsBefore[bonus] - 1, { award, turned });
  });
  return bonuses;
}

/**
 * The board of a pick bonus as the client shows it: every amount on two cards, and the cards that turn, in order.
 * Which amounts turn is the graph's; where they lie on the board decides nothing and is drawn for the show.
 */
function layOutCards(turned: readonly number[], chance: (below: number) => number): Pick<WWPickBonusSerialized, 'board' | 'pickSequence'> {
  const board = PICK_BONUS_AWARDS.flatMap(amount => [amount, amount]);
  for (let last = board.length - 1; last > 0; last--) {
    const other = chance(last + 1);
    [board[last], board[other]] = [board[other], board[last]];
  }
  const places = new Map(PICK_BONUS_AWARDS.map(amount => {
    const cards = board.flatMap((value, index) => value === amount ? [index] : []);
    return [amount, chance(2) ? cards.reverse() : cards] as const;
  }));
  const shown = new Map<number, number>();
  return { board, pickSequence: turned.map(amount => {
    const card = shown.get(amount) ?? 0;
    shown.set(amount, card + 1);
    return places.get(amount)![card];
  }) };
}

/**
 * One card turned, as the original server answered it: the card that turns is the next of the bonus whatever
 * card the player touched, and the second card of the awarded amount ends the bonus and adds its free spins.
 * `left` is how many free spins the round still plays, which the count shown never exceeds.
 */
function turnCard(state: State, userIndex: number, left: number): { state: State; result: WoodlandWhisperPickResult } {
  const bonus = state.pickBonus!;
  const revealedIndex = bonus.pickSequence[bonus.currentPickIndex], value = bonus.board[revealedIndex];
  const picks = [...bonus.userPicks, userIndex], revealed = [...bonus.revealedValues, value];
  const isMatch = revealed.filter(amount => amount === bonus.winValue).length === 2;
  const freeSpins = !isMatch ? state.freeSpins : {
    triggeringMultiplier: state.freeSpins?.triggeringMultiplier ?? bonus.triggeringMultiplier,
    totalWin: state.freeSpins?.totalWin ?? 0,
    spinsRemaining: Math.min((state.freeSpins?.spinsRemaining ?? 0) + bonus.winValue, left),
  };
  return {
    state: { lastGrid: state.lastGrid, freeSpins,
      pickBonus: isMatch ? null : { ...bonus, currentPickIndex: bonus.currentPickIndex + 1, userPicks: picks, revealedValues: revealed } },
    result: { type: 'PICK', win: 0, pick: { userIndex, revealedIndex, value, isMatch, board: bonus.board, picks },
      state: { freeSpinsLeft: freeSpins?.spinsRemaining ?? 0, totalFreeSpinWin: freeSpins?.totalWin ?? 0 } },
  };
}

/**
 * The screen a new session opens on: reels stopped at random on a board that pays nothing. The first two reels
 * share no symbol and show no Wild, so no line can begin, and the board is known not to pay without evaluating it.
 */
function quietGrid(): number[][] {
  const strips = Object.values(model.reel_strips), mystery = model.inner_reel_strip.reel1;
  for (;;) {
    const revealed = Symbols[mystery[Math.floor(Math.random() * mystery.length)]];
    const reels = strips.map(strip => {
      const stop = Math.floor(Math.random() * strip.length);
      return Array.from({ length: ROWS }, (_, row) => Symbols[strip[(stop + row) % strip.length]]).map(symbol => symbol === MYSTERY ? revealed : symbol);
    });
    const opening = [...reels[0], ...reels[1]];
    if (!opening.includes(WILD) && !reels[0].some(symbol => reels[1].includes(symbol))
      && reels.flat().filter(symbol => symbol === SCATTER).length < FREE_SPIN_TRIGGER - 1) return rowsOf(reels);
  }
}

/**
 * Woodland Whisper as its original server presented it: a paid or bought spin, a pick bonus of one request per
 * card after every spin that earned one, and the free spins it awards, one per request.
 */
export const woodlandWhisper: Game<State, WoodlandWhisperResult> = {
  id: 'woodland-whisper',
  initialState: () => ({ lastGrid: quietGrid(), freeSpins: null, pickBonus: null }),

  validate(state, action, payload) {
    switch (action) {
      case 'spin':
      case 'buybonus':
        if (typeof payload.multiplier !== 'number' || payload.multiplier < 1) return 'multiplier must be a positive integer';
        if (state.freeSpins && state.freeSpins.spinsRemaining > 0) return 'Complete free spins before starting a new round';
        return state.pickBonus ? 'Complete pick bonus before starting a new round' : null;
      case 'freespin':
        if (!state.freeSpins || state.freeSpins.spinsRemaining <= 0) return 'No free spins remaining';
        return state.pickBonus ? 'Pick bonus must be completed before playing free spins' : null;
      case 'pick':
        if (!state.pickBonus) return 'No active pick bonus';
        if (typeof payload.userIndex !== 'number') return 'userIndex required';
        return payload.userIndex < 0 || payload.userIndex >= state.pickBonus.board.length
          ? `userIndex must be between 0 and ${state.pickBonus.board.length - 1}` : null;
      case 'state':
        return null;
      default:
        return `Unknown action: ${action}`;
    }
  },

  graph: ({ action }) => action === 'spin' ? graph(false) : action === 'buybonus' ? graph(true) : null,

  steps({ trace }, { action, payload }) {
    const multiplier = payload.multiplier as number;
    const spins = lineSpins(trace, { rows: ROWS, names: SYM_NAMES, wild: WILD, paylines: model.paylines });
    const bonuses = pickBonuses(trace);
    const steps: Step<State, WoodlandWhisperResult>[] = [];
    spins.forEach((spin, index) => {
      const bonus = bonuses.get(index), left = spins.length - 1 - index;
      const grid = rowsOf(spin.grid), win = spin.win * multiplier, scatterWin = spin.scatterWin * multiplier;
      const shown: { win: number; components: { total: number; scatter: number }; scatterWin: number; sc: number; grid: number[][]; hits: PaylineHit[]; pickedBonus: number } =
        { win, components: { total: win, scatter: scatterWin }, scatterWin, sc: spin.scatters, grid, hits: spin.hits, pickedBonus: bonus?.award ?? 0 };
      const pickBonus = (triggeringMultiplier: number): WWPickBonusSerialized | null => bonus ? {
        ...layOutCards(bonus.turned, showing(index, spin.grid, bonus.turned)),
        currentPickIndex: 0, userPicks: [], revealedValues: [], winValue: bonus.award, triggeringMultiplier,
      } : null;
      if (index === 0) steps.push({
        action,
        reply: () => ({
          result: action === 'buybonus'
            ? { type: 'BUY', ...shown, triggeredPickBonus: true, state: { freeSpinsLeft: 0, totalFreeSpinWin: 0 } }
            : { type: 'BASE', ...shown, triggeredPickBonus: bonus !== undefined, state: { freeSpinsLeft: 0, totalFreeSpinWin: 0 } },
          state: { lastGrid: grid, freeSpins: null, pickBonus: pickBonus(multiplier) },
        }),
      });
      else steps.push({
        action: 'freespin',
        reply: (_, state) => {
          const freeSpins = { triggeringMultiplier: state.freeSpins!.triggeringMultiplier, totalWin: state.freeSpins!.totalWin + win,
            spinsRemaining: Math.min(state.freeSpins!.spinsRemaining - 1, left) };
          return {
            result: { type: 'FREE', ...shown, retriggeredPickBonus: bonus !== undefined,
              state: { freeSpinsLeft: freeSpins.spinsRemaining, totalFreeSpinWin: freeSpins.totalWin } },
            state: { lastGrid: grid, freeSpins, pickBonus: pickBonus(freeSpins.triggeringMultiplier) },
          };
        },
      });
      bonus?.turned.forEach(() => steps.push({ action: 'pick', reply: (picked, state) => turnCard(state, picked.userIndex as number, left) }));
    });
    return steps;
  },
};
