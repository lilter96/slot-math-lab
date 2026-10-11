import fullModel from './model.json' with { type: 'json' };
import { add, and, div, length, lte, min, not, num, state, sub } from '../shared/graph/expr';
import { Flow, gameGraph, Mechanics } from '../shared/graph/flow';
import { lineSpin, lineSpinState } from '../shared/graph/mechanics/line-spin';
import { linePayTable, paylines, paylineState } from '../shared/graph/mechanics/paylines';
import { pickBonus, pickBonusState, type PickCard } from '../shared/graph/mechanics/pick-bonus';
import { reelState, showingState, spinReels, spinReelsShowing, type ReelSet, type Showing } from '../shared/graph/mechanics/reels';
import { scatterPay, scatterPayTable } from '../shared/graph/mechanics/scatter';
import { gameTables } from '../shared/graph/tables';

export type WoodlandWhisperModel = typeof fullModel;
export interface WoodlandWhisperOptions {
  graphId?: string;
  /** The bought feature instead of a paid spin: the round opens with a spin that is sure to trigger the pick bonus. */
  buy?: boolean;
}

const ROWS = 3;

/**
 * Woodland Whisper as a graph: 5 × 3 reels, 30 paylines, a mystery symbol, scatter pays, and a pick bonus that
 * awards free spins paying double. Everything specific to the game is read from `model`; the default is the
 * released game. With `buy` the graph is the bought feature, whose payout is reported per credit of its price.
 */
export function createWoodlandWhisperGraph(model: WoodlandWhisperModel = fullModel,
  { buy = false, graphId = buy ? 'woodland-whisper-buy' : 'woodland-whisper' }: WoodlandWhisperOptions = {}): Record<string, unknown> {
  const names: readonly string[] = model.symbols.regular;
  const id = (name: string) => names.indexOf(name);
  const WILD = id(model.symbols.wild), SCATTER = id(model.symbols.scatter), MYSTERY = id('REPLACEMENT');
  const TOTAL_BET = model.game_metadata.cost_to_play;
  const TRIGGER = model.feature.trigger.min_count;
  const FREE_MULTIPLIER = model.feature.free_spin_multiplier;
  const FREE_SPIN_CAP = model.limits.free_spin_cap;
  const ROUND_COST = buy ? TOTAL_BET * model.feature.buy_bonus_cost_multiplier : TOTAL_BET;
  const cards = model.feature.pick_bonus as unknown as PickCard[];
  // How often each symbol appears on the mystery strip is its weight in the mystery draw.
  const mysteryWeights = new Map<number, number>();
  for (const name of model.inner_reel_strip.reel1) mysteryWeights.set(id(name), (mysteryWeights.get(id(name)) ?? 0) + 1);

  const mechanics = new Mechanics();
  const reels: ReelSet = { name: 'spin-reels', stripKey: 'reel', rows: ROWS,
    strips: Object.values(model.reel_strips).map(strip => strip.map(id)) };
  const triggering: Showing = { name: 'spin-reels-triggering', symbol: SCATTER, symbolName: model.symbols.scatter, atLeast: TRIGGER };
  const pays = Object.fromEntries(Object.entries(model.paytable).map(([name, row]) => [id(name), row]));
  const scatterPays = scatterPayTable(model.scatter_paytable.COIN, reels.strips.length * ROWS);
  const evaluation = {
    paylines: mechanics.add(paylines(mechanics, model.paylines, ROWS)), scatterPay: mechanics.add(scatterPay()),
    scatter: SCATTER, mystery: { placeholder: MYSTERY, outcomes: [...mysteryWeights] }, multiplierKey: 'spinMultiplier',
    trigger: { count: TRIGGER, key: 'bonusDue' },
  };
  const spin = mechanics.add(lineSpin({ ...evaluation, reels: mechanics.add(spinReels(reels)) }));

  const awardFreeSpins = mechanics.add(new Flow()
    .use('pick', 'Pick bonus: turn cards until two match', mechanics.add(pickBonus(mechanics, cards)))
    // Holding more free spins than the feature can ever play changes nothing, and keeps the count in a fixed range.
    .write('award', `The matched amount is added to the free spins (at most ${FREE_SPIN_CAP} held)`, 'freeSpins',
      min(add(state('freeSpins'), state('pickAward')), num(FREE_SPIN_CAP)))
    .record('trace-spin', 'Remember after which spin the bonus was played', 'pickSpins', length('spinWins'))
    .mechanic('award-free-spins', 'The pick bonus decides how many free spins are awarded.'));
  const freeSpin = mechanics.add(new Flow()
    .onlyIf('bonus', `The spin before showed ${TRIGGER}+ ${model.symbols.scatter}s: the pick bonus adds free spins`, state('bonusDue'), awardFreeSpins)
    .write('use', 'Use one free spin', 'freeSpins', sub(state('freeSpins'), num(1)))
    .use('spin', 'Free spin', spin)
    .mechanic('free-spin', `One step of the feature: the pick bonus if the spin before earned it, then one free spin, played like a paid spin.`));
  const freeSpins = mechanics.add(new Flow()
    .write('multiplier', `Free spins pay ×${FREE_MULTIPLIER}`, 'spinMultiplier', num(FREE_MULTIPLIER))
    .repeat('play', 'Play while a pick bonus is due or free spins are left', FREE_SPIN_CAP, freeSpin,
      { until: and(not(state('bonusDue')), lte(state('freeSpins'), num(0))), bodyLabel: 'Pick bonus if due, then a free spin' })
    .mechanic('free-spins', `The feature: every spin with ${TRIGGER}+ ${model.symbols.scatter}s earns a pick bonus that awards free spins, and every free spin pays ×${FREE_MULTIPLIER}.`));

  // The original game has no win cap. The sink's cap is the most the round can pay at all, so it never cuts a win.
  const bestSpin = Math.max(...Object.values(pays).flatMap(row => Object.values(row))) * model.paylines.length / TOTAL_BET + Math.max(...scatterPays);
  const roundCap = Math.ceil(bestSpin * (1 + FREE_MULTIPLIER * FREE_SPIN_CAP) * TOTAL_BET / ROUND_COST);
  const root = new Flow();
  if (buy)
    root.use('bought-spin', `Bought spin · ${TRIGGER}+ ${model.symbols.scatter}s are certain`, mechanics.add(lineSpin({ ...evaluation,
      reels: mechanics.add(spinReelsShowing(reels, triggering)), name: 'bought-spin',
      description: `The spin a bought feature opens with: an ordinary spin, given that it shows ${TRIGGER} or more ${model.symbols.scatter}s.` })))
      .use('feature', 'Pick bonus and free spins', freeSpins);
  else
    root.use('base-spin', `Paid spin · 5 reels, ${model.paylines.length} lines`, spin)
      .onlyIf('trigger', `${TRIGGER}+ ${model.symbols.scatter}s start the pick bonus and the free spins`, state('bonusDue'), freeSpins);
  root.write('credits', buy ? 'Round win as a multiple of the price paid' : 'Round win as a multiple of the total bet', 'winCredits', div(state('roundCoins'), state('roundCost')))
    .sink('round', 'Round payout & RTP', 'winCredits', roundCap);

  return gameGraph({
    id: graphId, name: buy ? `${model.game_metadata.name} · bought feature` : model.game_metadata.name,
    description: `${model.game_metadata.name}${buy ? `, the feature bought for ${model.feature.buy_bonus_cost_multiplier} bets` : ''}: ${model.source}.`,
    root, mechanics,
    initialState: {
      totalBet: TOTAL_BET, roundCost: ROUND_COST, scatterPays, freeSpins: 0, bonusDue: false, spinMultiplier: 1, mysterySymbol: '', winCredits: 0, pickSpins: [],
      ...reelState(reels), ...Object.fromEntries(reels.strips.map((_, reel) => [`stop${reel}`, '0'])), ...(buy ? showingState(reels, triggering) : {}),
      ...paylineState(WILD, linePayTable({ symbolCount: names.length, wild: WILD, pays })), ...lineSpinState(), ...pickBonusState(cards),
    },
    tables: gameTables({ names, wild: WILD, scatter: SCATTER, linePays: model.paytable, scatterPays: model.scatter_paytable.COIN,
      totalBet: TOTAL_BET, roundCost: ROUND_COST, paylines: model.paylines, rows: ROWS, columns: reels.strips.length }),
  });
}

export { fullModel as woodlandWhisperModel };
