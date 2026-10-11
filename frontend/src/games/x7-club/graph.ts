import fullModel from './model.json' with { type: 'json' };
import { add, div, gte, state } from '../shared/graph/expr';
import { Flow, gameGraph, Mechanics, type Outcome } from '../shared/graph/flow';
import { cellState, spinCells, type CellBoard } from '../shared/graph/mechanics/cells';
import { columnBoosts, columnBoostState, type ColumnBoost } from '../shared/graph/mechanics/column-boost';
import { coinPrizes, coinState, holdAndSpin, holdAndSpinState } from '../shared/graph/mechanics/hold-and-spin';
import { linePayTable, paylines, paylineState } from '../shared/graph/mechanics/paylines';
import { gameTables } from '../shared/graph/tables';

export type X7ClubModel = Omit<typeof fullModel, 'paytable' | 'cell_weights'> & {
  /** `paytable[symbol][count]`, in line credits. */
  paytable: Record<string, Record<string, number>>;
  cell_weights: Record<string, number>;
};
export interface X7ClubOptions {
  graphId?: string;
  /** The bought bonus instead of a paid spin: the round is a Hold & Spin that starts with fixed coins. */
  buy?: boolean;
}

/**
 * X7 Club as a graph: a board of five columns whose cells are drawn one by one, paylines, and coins that carry prizes.
 * Six or more coins start a Hold & Spin, in which every column that fills with coins gets a boost. Everything
 * specific to the game is read from `model`; the default is the released game. With `buy` the graph is the
 * bought bonus, whose payout is reported per credit of its price.
 */
export function createX7ClubGraph(model: X7ClubModel = fullModel,
  { buy = false, graphId = buy ? 'x7-club-buy' : 'x7-club' }: X7ClubOptions = {}): Record<string, unknown> {
  const names: readonly string[] = model.symbols.regular;
  const id = (name: string) => names.indexOf(name);
  const WILD = id(model.symbols.wild), COIN = id(model.symbols.coin);
  const COLUMNS = model.paylines[0].length, ROWS = model.hold.rows, CELLS = COLUMNS * ROWS;
  const TOTAL_BET = model.game_metadata.cost_to_play;
  // The paylines pay in line credits, and one line costs one credit.
  if (TOTAL_BET !== model.paylines.length) throw new Error(`${model.game_metadata.name} is played at one credit a line`);
  const ROUND_COST = buy ? TOTAL_BET * model.buy.cost_multiplier : TOTAL_BET;
  // The most a round may pay, a rule of the game: a multiple of the bet, reported here per credit of the round's cost.
  const roundCap = model.max_win * TOTAL_BET / ROUND_COST;
  if (!Number.isInteger(roundCap)) throw new Error(`A win of ${model.max_win} bets is not a whole multiple of a round costing ${ROUND_COST}`);
  const prizes: Outcome[] = model.prizes.map(({ bets, weight }) => [bets, weight]);
  const boughtCoins = Array.from({ length: CELLS }, (_, cell) => model.buy.cells.includes(cell) ? model.buy.entry_credits : 0);

  const mechanics = new Mechanics();
  const board: CellBoard = { name: 'board', cells: CELLS, outcomes: names.map((name, symbol) => [symbol, model.cell_weights[name] ?? 0]) };
  const boost: ColumnBoost = { columns: COLUMNS, rows: ROWS, maxPulls: model.column_boost.max_pulls, kinds: model.column_boost.kinds };
  const pays = Object.fromEntries(Object.entries(model.paytable).map(([name, row]) => [id(name), row]));
  const bonus = mechanics.add(holdAndSpin(mechanics, {
    cells: CELLS, respins: model.hold.respins, heldAtStart: Math.min(model.hold.trigger, model.buy.cells.length),
    hitWeight: model.hold.hit_weight, missWeight: model.hold.miss_weight, prizes,
    afterCoins: { mechanic: mechanics.add(columnBoosts(mechanics, boost)), label: 'Boost every column that has just filled' },
  }));

  const root = new Flow();
  if (buy)
    root.write('bought-coins', `The bought bonus starts with ${model.buy.cells.length} coins of ${model.buy.entry_credits} credits`, 'coins', state('boughtCoins'))
      .use('bonus', 'Hold & Spin', bonus);
  else
    root.use('spin', `Paid spin · ${CELLS} cells, ${model.paylines.length} lines`, mechanics.add(new Flow()
      .use('cells', 'Every cell shows a symbol', mechanics.add(spinCells(mechanics, board)))
      .use('prizes', `Every ${model.symbols.coin} carries a prize`, mechanics.add(coinPrizes(mechanics, { cells: CELLS, symbol: COIN, prizes })))
      .use('lines', 'Pay the lines', mechanics.add(paylines(mechanics, model.paylines, ROWS)))
      .record('trace-board', 'Remember the board', 'spinBoards', state('board'))
      .record('trace-coins', 'Remember the coins', 'spinCoins', state('coins'))
      .mechanic('spin', `One paid spin: the cells are drawn, the lines are paid, and every ${model.symbols.coin} shows the prize it carries.`)))
      .onlyIf('trigger', `${model.hold.trigger}+ ${model.symbols.coin}s start Hold & Spin with those coins held`, gte(state('coinCount'), state('holdTrigger')), bonus);
  root.write('credits', buy ? 'Round win as a multiple of the price paid' : 'Round win as a multiple of the total bet', 'winCredits',
    div(buy ? state('bonusCoins') : add(state('lineCoins'), state('bonusCoins')), state('roundCost')))
    .sink('round', `Round payout & RTP · at most ${model.max_win} bets`, 'winCredits', roundCap);

  return gameGraph({
    id: graphId, name: buy ? `${model.game_metadata.name} · bought bonus` : model.game_metadata.name,
    description: `${model.game_metadata.name}${buy ? `, the bonus bought for ${model.buy.cost_multiplier} bets` : ''}: ${model.source}.`,
    root, mechanics,
    initialState: {
      totalBet: TOTAL_BET, roundCost: ROUND_COST, winCredits: 0, holdTrigger: model.hold.trigger,
      ...coinState(CELLS, TOTAL_BET), ...holdAndSpinState(), ...columnBoostState(boost),
      ...(buy ? { boughtCoins } : { spinBoards: [], spinCoins: [], ...cellState(board), ...paylineState(WILD, linePayTable({ symbolCount: names.length, wild: WILD, pays, reels: COLUMNS })) }),
    },
    tables: gameTables({
      names, wild: WILD, money: COIN, totalBet: TOTAL_BET, roundCost: ROUND_COST, rows: ROWS, columns: COLUMNS,
      ...(buy ? {} : { linePays: model.paytable, paylines: model.paylines }),
      prizes: [{ id: 'coin-prizes', symbol: COIN, credits: model.prizes.map(({ bets }) => bets * TOTAL_BET) },
        ...(buy ? [{ id: 'bought-coins', symbol: COIN, credits: [model.buy.entry_credits] }] : [])],
    }),
  });
}

export { fullModel as x7ClubModel };
