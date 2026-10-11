import fullModel from './model.json' with { type: 'json' };
import { add, and, bool, div, eq, gte, length, lt, lte, mapOver, min, mul, num, or, sizeOf, state, str, sub, sumOver, when } from '../shared/graph/expr';
import { Flow, gameGraph, Mechanics, type Outcome } from '../shared/graph/flow';
import { cascade, cascadeState, tumble } from '../shared/graph/mechanics/cascade';
import { cellState, spinCells, type CellBoard } from '../shared/graph/mechanics/cells';
import { clusterPays, clusterState, clusterTrace, type ClusterBoard } from '../shared/graph/mechanics/clusters';
import { coinPrizes, coinState } from '../shared/graph/mechanics/hold-and-spin';
import { gameTables } from '../shared/graph/tables';
import { cellsShowing } from '../shared/graph/mechanics/symbol-cells';

type Weights = Record<string, number>;
export type NineLivesModel = Omit<typeof fullModel, 'cell_weights' | 'cluster_paytable'> & {
  cell_weights: { paid_spin: Weights; free_spin: Weights; refill: Weights };
  /** `cluster_paytable[symbol][size]`, in credits. */
  cluster_paytable: Record<string, Record<string, number>>;
};
export interface NineLivesOptions {
  graphId?: string;
  /** The bought bonus instead of a paid spin: the round is the free spins. */
  buy?: boolean;
}

/** What a played round leaves in its trace about every spin and about every step of its cascades that paid. */
const spinTrace = ['spinBoards', 'spinCoins', 'spinMultipliers', 'spinCollected', 'spinFinalBoards'] as const;
const stepTrace = ['stepSpins', 'stepMultipliers', 'stepWins'] as const;
/** The groups that paid in a step, under the names the cluster subgraphs trace them by: `hitSymbols` is kept as `stepHitSymbols`. */
const stepHits = (key: string) => `stepH${key.slice(1)}`;

/**
 * Nine Lives as a graph: a board whose cells are drawn one by one, cluster pays and cascades. Every step of a
 * cascade pays more than the one before, scatters start free spins, and in the free spins every coin is collected
 * for its prize and leaves a wild, while the multiplier the steps have built is kept from spin to spin.
 * Everything specific to the game is read from `model`; the default is the released game. With `buy` the graph
 * is the bought bonus, whose payout is reported per credit of its price.
 *
 * The lab demo (`../nine-lives/graph.ts`) is another graph of the same game, written independently. This one is
 * built from the subgraphs the other ported games share, and leaves the trace the ported client is served from.
 */
export function createNineLivesGraph(model: NineLivesModel = fullModel,
  { buy = false, graphId = buy ? 'nine-lives-buy' : 'nine-lives' }: NineLivesOptions = {}): Record<string, unknown> {
  const names: readonly string[] = model.symbols.regular;
  const id = (name: string) => names.indexOf(name);
  const WILD = id(model.symbols.wild), COIN = id(model.symbols.coin), SCATTER = id(model.symbols.scatter);
  const { columns: COLUMNS, rows: ROWS } = model.board, CELLS = COLUMNS * ROWS;
  const TOTAL_BET = model.game_metadata.cost_to_play;
  const ROUND_COST = buy ? TOTAL_BET * model.buy.cost_multiplier : TOTAL_BET;
  // The most a round may pay, a rule of the game: a multiple of the bet. The graph applies it in credits; the
  // sink is told the same limit in the unit it reports, rounded up where a bought round does not divide it.
  const CAP_COINS = model.max_win * TOTAL_BET;
  const { trigger: TRIGGER, spins: SPINS, max_multiplier: MAX_MULTIPLIER } = model.free_spins;
  const outcomes = (weights: Weights): Outcome[] => names.map((name, symbol) => [symbol, weights[name] ?? 0]);

  const mechanics = new Mechanics();
  const clusters: ClusterBoard = { columns: COLUMNS, rows: ROWS, wild: WILD, symbolCount: names.length,
    pays: Object.fromEntries(Object.entries(model.cluster_paytable).map(([name, row]) => [id(name), row])) };
  const paidCells: CellBoard = { name: 'paid-board', cells: CELLS, outcomes: outcomes(model.cell_weights.paid_spin) };
  const freeCells: CellBoard = { name: 'free-board', cells: CELLS, outcomes: outcomes(model.cell_weights.free_spin) };

  const payStep = new Flow()
    .write('multiplier', `A step pays × the multiplier of the spin, plus 1 for every step before it, at most × ${MAX_MULTIPLIER}`, 'stepMultiplier',
      min(num(MAX_MULTIPLIER), add(state('multiplier'), state('cascadeSteps'))))
    .write('step', 'The step pays its groups × the multiplier', 'stepCoins', mul(state('clusterCoins'), state('stepMultiplier')))
    .write('round', 'Add it to the round', 'roundCoins', add(state('roundCoins'), state('stepCoins')))
    .record('trace-spin', 'Remember which spin the step belongs to', 'stepSpins', sub(length('spinBoards'), num(1)))
    .record('trace-multiplier', 'Remember the multiplier', 'stepMultipliers', state('stepMultiplier'))
    .record('trace-win', 'Remember what the step paid', 'stepWins', state('stepCoins'));
  for (const key of clusterTrace) payStep.record(`trace-${key}`, 'Remember the groups that paid', stepHits(key), state(key));
  const captureScatters = mechanics.add(new Flow()
    .onlyIf('capture', 'Count scatters only on the initial paid board', state('captureScatters'), mechanics.add(new Flow()
      .write('count', 'Count the prepared scatter set', 'scatterCount', sizeOf(cellsShowing({ cells: CELLS, symbolCount: names.length }, SCATTER)))
      .write('captured', 'Later cascade boards do not change the trigger', 'captureScatters', bool(false))
      .mechanic('capture-scatters', 'Use the already prepared symbol set for the paid-spin trigger.')))
    .mechanic('observe-initial-board', 'An observation hook after reading the board; no extra cell scan.'));
  const cascading = mechanics.add(cascade(mechanics, {
    maxSteps: model.cascade.max_steps,
    pays: { mechanic: mechanics.add(clusterPays(mechanics, clusters, { mechanic: captureScatters, label: 'Observe the initial board' })), label: 'Pay the groups on the board', coins: 'clusterCoins' },
    onStep: { mechanic: mechanics.add(payStep.mechanic('pay-step', 'Pays one step of a cascade: its groups × a multiplier that grows with every step.')), label: 'Pay the step' },
    tumble: { mechanic: mechanics.add(tumble(mechanics, { columns: COLUMNS, rows: ROWS, retainSurvivors: false, refill: { cells: outcomes(model.cell_weights.refill) } })), label: 'Remove what paid and fill the board' },
  }));
  /** Adds the steps that remember a spin as it landed. */
  const rememberSpin = (flow: Flow) => flow
    .record('trace-board', 'Remember the board', 'spinBoards', state('board'))
    .record('trace-coins', 'Remember the coins', 'spinCoins', state('coins'))
    .record('trace-multiplier', 'Remember the multiplier the spin starts with', 'spinMultipliers', state('multiplier'));

  const freeSpin = rememberSpin(new Flow()
    .write('use', 'Use up one free spin', 'freeSpins', sub(state('freeSpins'), num(1)))
    .use('cells', 'Every cell shows a symbol', mechanics.add(spinCells(freeCells)))
    .use('prizes', `Every ${model.symbols.coin} carries a prize`, mechanics.add(coinPrizes({ cells: CELLS, symbol: COIN,
      prizes: model.prizes.map(({ bets, weight }) => [bets, weight]) }))))
    .write('collect', `Every ${model.symbols.coin} is collected once: its prize × the multiplier of the spin`, 'collectedCoins',
      mul(sumOver('coins', state('item'), { itemType: 'Number' }), state('multiplier')))
    .write('round', 'Add it to the round', 'roundCoins', add(state('roundCoins'), state('collectedCoins')))
    .record('trace-collected', 'Remember what was collected', 'spinCollected', state('collectedCoins'))
    .write('wilds', `A collected ${model.symbols.coin} leaves a ${model.symbols.wild}`, 'board',
      mapOver('board', when(eq(state('item'), str(COIN)), str(WILD), state('item'))))
    .use('cascade', 'Cascade', cascading)
    .record('trace-final', 'Remember the board the spin ends on', 'spinFinalBoards', state('board'))
    .write('multiplier', `Every step that paid raises the multiplier of the spins to come, to at most × ${MAX_MULTIPLIER}`, 'multiplier',
      min(num(MAX_MULTIPLIER), add(state('multiplier'), state('cascadeSteps'))))
    .mechanic('free-spin', `One free spin: every ${model.symbols.coin} is collected and leaves a ${model.symbols.wild}, the board cascades, and the multiplier grows by the steps that paid.`);
  const capped = gte(state('roundCoins'), state('capCoins'));
  const freeSpins = mechanics.add(new Flow()
    .write('award', `${SPINS} free spins`, 'freeSpins', num(SPINS))
    .write('multiplier', 'The free spins start at × 1', 'multiplier', num(1))
    // The spins are counted down and never awarded again; one pass of slack keeps the limit from ever ending the loop.
    .repeat('free-spin', 'Free spins until none are left or the round has reached its limit', SPINS + 1, mechanics.add(freeSpin),
      { until: or(lte(state('freeSpins'), num(0)), capped), bodyLabel: 'Free spin' })
    .mechanic('free-spins', `${SPINS} free spins whose multiplier is kept from spin to spin. They end early only when the round reaches the most it may pay.`));

  const root = new Flow();
  if (buy) root.use('bonus', `Bought bonus · ${SPINS} free spins`, freeSpins);
  else
    root.use('spin', `Paid spin · ${CELLS} cells`, mechanics.add(rememberSpin(new Flow()
      .use('cells', 'Every cell shows a symbol', mechanics.add(spinCells(paidCells))))
      .record('trace-collected', 'Nothing is collected in a paid spin', 'spinCollected', num(0))
      .write('scatters', `Capture the initial ${model.symbols.scatter} set when the board is read`, 'captureScatters', bool(true))
      .use('cascade', 'Cascade', cascading)
      .record('trace-final', 'Remember the board the spin ends on', 'spinFinalBoards', state('board'))
      .mechanic('paid-spin', 'One paid spin: the cells are drawn and the board cascades, every step paying more than the one before.')))
      .onlyIf('trigger', `${TRIGGER}+ ${model.symbols.scatter}s start the free spins`, and(gte(state('scatterCount'), num(TRIGGER)), lt(state('roundCoins'), state('capCoins'))), freeSpins);
  root.write('credits', `A round pays at most ${model.max_win} bets; its win as a multiple of ${buy ? 'the price paid' : 'the total bet'}`, 'winCredits',
    div(min(state('roundCoins'), state('capCoins')), state('roundCost')))
    .sink('round', `Round payout & RTP · at most ${model.max_win} bets`, 'winCredits', Math.ceil(CAP_COINS / ROUND_COST));

  return gameGraph({
    id: graphId, name: buy ? `${model.game_metadata.name} · bought bonus` : model.game_metadata.name,
    description: `${model.game_metadata.name}${buy ? `, the bonus bought for ${model.buy.cost_multiplier} bets` : ''}: ${model.source}.`,
    root, mechanics,
    initialState: {
      totalBet: TOTAL_BET, roundCost: ROUND_COST, capCoins: CAP_COINS, winCredits: 0, roundCoins: 0,
      multiplier: 1, stepMultiplier: 1, stepCoins: 0, freeSpins: 0, scatterCount: 0, captureScatters: false, collectedCoins: 0,
      ...cellState(paidCells), ...coinState(CELLS, TOTAL_BET), ...clusterState(clusters), ...cascadeState({ columns: COLUMNS, rows: ROWS }),
      ...Object.fromEntries([...spinTrace, ...stepTrace, ...clusterTrace.map(stepHits)].map(key => [key, []])),
    },
    tables: gameTables({
      names, wild: WILD, scatter: SCATTER, money: COIN, totalBet: TOTAL_BET, roundCost: ROUND_COST, rows: ROWS, columns: COLUMNS,
      clusterPays: model.cluster_paytable,
      prizes: [{ id: 'coin-prizes', symbol: COIN, credits: model.prizes.map(({ bets }) => bets * TOTAL_BET) }],
    }),
  });
}

export { fullModel as nineLivesModel };
