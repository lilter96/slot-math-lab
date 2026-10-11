import { and, at, countOver, eq, gt, gte, lte, mapOver, mul, num, or, param, state, str, sub, sumOver, toNumber, toText, when, type Expr } from '../expr';
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

/**
 * State the coin subgraphs read and write. `coins[cell]` is the value of the coin held on that cell, in credits,
 * and 0 where there is none; `coinStake` is the number of credits a prize of 1 is worth.
 */
export const coinState = (cells: number, stake: number) => ({
  coins: Array.from({ length: cells }, () => 0), coinStake: stake, coinPrize: '0', coinCount: 0,
});
export const holdAndSpinState = () => ({
  respins: 0, landHit: '0', bonusCoins: 0, ...Object.fromEntries(holdTrace.map(key => [key, []])),
});

const cellNumber = { index: 'place', itemType: 'Number' } as const;
const prizeCredits = () => mul(toNumber(state('coinPrize')), state('coinStake'));
/** `coins` with the cell named by the parameter `cell` set to `value`. */
const coinsWith = (value: Expr) => mapOver('coins', when(eq(state('place'), param('cell')), value, state('item')), cellNumber);

/**
 * The prize of one cell of a spin. Parameters: `cell` and `symbol`, the coin symbol. A prize is drawn for the cell
 * whatever it shows; a cell showing the coin holds that prize × the stake in `coins`, any other cell holds nothing.
 */
function coinPrize(prizes: readonly Outcome[]): Mechanic {
  return new Flow()
    .draw('prize', 'Draw the prize a coin on this cell carries', 'coinPrize', prizes)
    .write('value', 'A coin is worth its prize × the stake; any other symbol is worth nothing', 'coins',
      coinsWith(when(eq(at('board', param('cell')), toText(param('symbol'))), prizeCredits(), num(0))))
    .mechanic('coin-prize', 'Gives one cell its coin value: the drawn prize × the stake if the cell shows a coin, nothing otherwise.');
}

/** Values every coin on `board`: leaves the coin values in `coins` and how many coins there are in `coinCount`. */
export function coinPrizes(mechanics: Mechanics, { cells, symbol, prizes }: { cells: number; symbol: number; prizes: readonly Outcome[] }): Mechanic {
  const prize = mechanics.add(coinPrize(prizes));
  const flow = new Flow();
  for (let cell = 0; cell < cells; cell++) flow.use(`cell-${cell + 1}`, `Cell ${cell + 1}`, prize, { cell, symbol });
  return flow.write('count', 'Count the coins on the board', 'coinCount', heldCoins())
    .mechanic('coin-prizes', `Every coin on the board carries a prize: one prize is drawn per cell, and the ${cells} cells are valued one after another.`);
}

const heldCoins = () => countOver('coins', gt(state('item'), num(0)), { itemType: 'Number' });

/**
 * One cell of a respin. Parameter: `cell`. Whether a coin lands and the prize it would carry are both drawn
 * whatever the cell holds; the coin is held only if the cell was empty.
 */
function landCoin({ hitWeight, missWeight, prizes }: HoldAndSpin): Mechanic {
  return new Flow()
    .draw('hit', 'Does a coin land on this cell?', 'landHit', [[1, hitWeight], [0, missWeight]])
    .draw('prize', 'Draw the prize it would carry', 'coinPrize', prizes)
    .write('hold', 'A coin that lands on an empty cell is held, worth its prize × the stake', 'coins',
      coinsWith(when(and(eq(state('landHit'), str(1)), eq(state('item'), num(0))), prizeCredits(), state('item'))))
    .mechanic('land-coin', 'One cell of a respin: a coin lands with a fixed chance and, if the cell was empty, stays there with a drawn prize.');
}

/**
 * Hold & Spin on the coins already in `coins`. The held coins stay; every respin, each cell may receive a new
 * one. A respin on which a coin lands restores the respins, any other uses one up, and the feature ends when no
 * respins are left or every cell holds a coin. It pays the sum of the held coins, left in `bonusCoins`.
 */
export function holdAndSpin(mechanics: Mechanics, spec: HoldAndSpin): Mechanic {
  const { cells, respins, heldAtStart, afterCoins } = spec;
  const land = mechanics.add(landCoin(spec));
  const respin = new Flow();
  for (let cell = 0; cell < cells; cell++) respin.use(`cell-${cell + 1}`, `Cell ${cell + 1}`, land, { cell });
  respin.write('respins', `More coins than before restore the ${respins} respins; a respin without a new coin uses one up`, 'respins',
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
