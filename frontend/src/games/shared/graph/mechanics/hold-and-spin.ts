import { and, choose, countOver, eq, gt, gte, lte, mapOver, mul, num, or, state, str, sub, sumOver, toNumber, when, type Expr } from '../expr';
import { Flow, Mechanics, type Mechanic, type Outcome } from '../flow';

export interface HoldAndSpin {
  /** How many cells the board has. */
  cells: number;
  /** Respins the feature starts with, and is given again whenever a coin lands. */
  respins: number;
  /** The fewest coins the feature can start with; together with `respins` it bounds how long the feature lasts. */
  heldAtStart: number;
  /** How likely a coin is to land on a cell in one respin, as weights. */
  hitWeight: number;
  missWeight: number;
  /** The prizes a coin can carry, in stakes, and their weights. */
  prizes: readonly Outcome[];
  /** Subgraph run when the feature starts and again after every respin, e.g. one that boosts full columns. */
  afterCoins?: { mechanic: string; label: string };
}

/**
 * What the feature leaves in the trace of a played round, one entry per step in the order played: what happened
 * (`start`, `respin`, or what a subgraph placed in `afterCoins` records), the column it concerned (−1 for none),
 * and the coins held and the respins left once it had happened.
 */
export const holdTrace = ['holdEvents', 'holdColumns', 'holdCoins', 'holdRespins'] as const;

/** Adds the steps that remember one event of the feature in the trace. */
export function recordHoldEvent(flow: Flow, id: string, event: Expr, column: Expr = num(-1)): Flow {
  return flow.record(`${id}-event`, 'Remember what happened', 'holdEvents', event)
    .record(`${id}-column`, 'Remember the column it concerned', 'holdColumns', column)
    .record(`${id}-coins`, 'Remember the coins held', 'holdCoins', state('coins'))
    .record(`${id}-respins`, 'Remember the respins left', 'holdRespins', state('respins'));
}

/** One state key per cell, in board order: `prize1`, `prize2` and so on. */
const cellKeys = (name: string, cells: number) => Array.from({ length: cells }, (_, cell) => `${name}${cell + 1}`);

/**
 * State the coin subgraphs read and write. `coins[cell]` is the value of the coin held on that cell, in credits,
 * and 0 where there is none; `coinStake` is the number of credits a prize of 1 is worth, and `prize1`, `prize2`
 * and so on are the prizes drawn for the cells.
 */
export const coinState = (cells: number, stake: number) => ({
  coins: Array.from({ length: cells }, () => 0), coinStake: stake, coinCount: 0,
  ...Object.fromEntries(cellKeys('prize', cells).map(key => [key, '0'])),
});
/** State of the respins: how many are left, and for every cell whether a coin landed on it in the last one. */
export const holdAndSpinState = (cells: number) => ({
  respins: 0, bonusCoins: 0, ...Object.fromEntries(cellKeys('landed', cells).map(key => [key, '0'])),
  ...Object.fromEntries(holdTrace.map(key => [key, []])),
});

const cellNumber = { index: 'place', itemType: 'Number' } as const;
/** What is drawn into `keys` for the cell `place`. */
const drawnFor = (keys: string[]) => choose(state('place'), keys.map(state));
/** What the prize drawn for the cell `place` is worth in credits. */
const prizeCredits = (prizes: string[]) => mul(toNumber(drawnFor(prizes)), state('coinStake'));

/**
 * Values every coin on `board`: leaves the coin values in `coins` and how many coins there are in `coinCount`.
 * A prize is drawn for every cell whatever it shows; a cell showing the coin `symbol` holds that prize × the
 * stake, any other cell holds nothing.
 */
export function coinPrizes({ cells, symbol, prizes }: { cells: number; symbol: number; prizes: readonly Outcome[] }): Mechanic {
  const flow = new Flow(), keys = cellKeys('prize', cells);
  keys.forEach((key, cell) => flow.draw(`cell-${cell + 1}`, `Draw the prize a coin on cell ${cell + 1} carries`, key, prizes));
  return flow
    .write('values', 'A coin is worth its prize × the stake; any other symbol is worth nothing', 'coins',
      mapOver('board', when(eq(state('item'), str(symbol)), prizeCredits(keys), num(0)), { index: 'place' }))
    .write('count', 'Count the coins on the board', 'coinCount', heldCoins())
    .mechanic('coin-prizes', `Every coin on the board carries a prize: one prize is drawn per cell, for each of the ${cells} cells.`);
}

const heldCoins = () => countOver('coins', gt(state('item'), num(0)), { itemType: 'Number' });

/**
 * Hold & Spin on the coins already in `coins`. The held coins stay; every respin, each cell may receive a new
 * one. A respin on which a coin lands restores the respins, any other uses one up, and the feature ends when no
 * respins are left or every cell holds a coin. It pays the sum of the held coins, left in `bonusCoins`.
 */
export function holdAndSpin(mechanics: Mechanics, spec: HoldAndSpin): Mechanic {
  const { cells, respins, heldAtStart, hitWeight, missWeight, prizes, afterCoins } = spec;
  const landed = cellKeys('landed', cells), prize = cellKeys('prize', cells);
  // Whether a coin lands on a cell and the prize it would carry are both drawn whatever the cell holds.
  const respin = new Flow();
  for (let cell = 0; cell < cells; cell++)
    respin.draw(`land-${cell + 1}`, `Does a coin land on cell ${cell + 1}?`, landed[cell], [[1, hitWeight], [0, missWeight]])
      .draw(`prize-${cell + 1}`, 'Draw the prize it would carry', prize[cell], prizes);
  respin
    .write('hold', 'A coin that lands on an empty cell is held, worth its prize × the stake', 'coins',
      mapOver('coins', when(and(eq(drawnFor(landed), str(1)), eq(state('item'), num(0))), prizeCredits(prize), state('item')), cellNumber))
    .write('respins', `More coins than before restore the ${respins} respins; a respin without a new coin uses one up`, 'respins',
      when(gt(heldCoins(), state('coinCount')), num(respins), sub(state('respins'), num(1))))
    .write('count', 'Count the coins held now', 'coinCount', heldCoins());
  recordHoldEvent(respin, 'trace', str('respin'));
  if (afterCoins) respin.use('after', afterCoins.label, afterCoins.mechanic);

  const flow = new Flow()
    .write('respins', `Start with ${respins} respins`, 'respins', num(respins))
    .write('count', 'Count the coins held at the start', 'coinCount', heldCoins());
  recordHoldEvent(flow, 'trace', str('start'));
  if (afterCoins) flow.use('after', afterCoins.label, afterCoins.mechanic);
  // Every coin that lands is followed by at most `respins` respins, so the feature cannot outlast this many;
  // the loop is given one more, so that reaching its limit would mean a fault and never a played-out feature.
  const longest = (cells - heldAtStart) * respins;
  return flow.repeat('respin', 'Respin while respins are left and a cell is empty', longest + 1,
    mechanics.add(respin.mechanic('respin', `One respin: each of the ${cells} cells may receive a coin, and the respins are restored or used up.`)),
    { until: or(lte(state('respins'), num(0)), gte(state('coinCount'), num(cells))), bodyLabel: 'Respin' })
    .write('collect', 'The feature pays the sum of the held coins', 'bonusCoins', sumOver('coins', state('item'), { itemType: 'Number' }))
    .mechanic('hold-and-spin', `Hold & Spin: held coins stay while the other cells respin. A new coin restores the ${respins} respins; the feature ends when they run out or the board is full, and pays every held coin.`);
}
