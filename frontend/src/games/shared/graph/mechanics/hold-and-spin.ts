import {
    add,
    and,
    emptyArray,
    at,
    countOver,
    eq,
    gt,
    gte,
    lte,
    mapOver,
    mul,
    num,
    or,
    state,
    str,
    sub,
    sumOver,
    toNumber,
    when,
    type Expr
} from '../expr';
import {Flow, Mechanics, type Mechanic, type Outcome} from '../flow';

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
 * and 0 where there is none; `coinStake` is the number of credits a prize of 1 is worth. `coinPrizeDraws`
 * retains the complete ordered prize draw, including cells that did not show a coin.
 */
export const coinState = (cells: number, stake: number) => ({
    coins: Array.from({length: cells}, () => 0), coinStake: stake, coinCount: 0,
    coinPrizeDraws: Array.from({length: cells}, () => '0'),
});
/** State of the respins: how many are left, and for every cell whether a coin landed on it in the last one. */
export const holdAndSpinState = (cells: number) => ({
    respins: 0, bonusCoins: 0, respinDraws: Array.from({length: cells * 2}, () => '0'),
    ...Object.fromEntries(holdTrace.map(key => [key, []])),
});

const cellNumber = {index: 'place', itemType: 'Number'} as const;
/** What the prize drawn for the cell `place` is worth in credits. */
const prizeCredits = (array: string, index: Expr) => mul(toNumber(at(array, index)), state('coinStake'));

/**
 * Values every coin on `board`: leaves the coin values in `coins` and how many coins there are in `coinCount`.
 * A prize is drawn for every cell whatever it shows; a cell showing the coin `symbol` holds that prize × the
 * stake, any other cell holds nothing.
 */
export function coinPrizes({cells, symbol, prizes}: {
    cells: number;
    symbol: number;
    prizes: readonly Outcome[]
}): Mechanic {
    return new Flow().drawMany('draws', 'Draw a prize for every cell in board order', 'coinPrizeDraws', cells, prizes)
        .write('values', 'A coin is worth its prize × the stake; any other symbol is worth nothing', 'coins',
            mapOver('board', when(eq(state('item'), str(symbol)), prizeCredits('coinPrizeDraws', state('place')), num(0)), {index: 'place'}))
        .write('count', 'Count the coins on the board', 'coinCount', heldCoins())
        .mechanic('coin-prizes', `Every coin on the board carries a prize: one prize is drawn per cell, for each of the ${cells} cells.`);
}

const heldCoins = () => countOver('coins', gt(state('item'), num(0)), {itemType: 'Number'});

/**
 * Hold & Spin on the coins already in `coins`. The held coins stay; every respin, each cell may receive a new
 * one. A respin on which a coin lands restores the respins, any other uses one up, and the feature ends when no
 * respins are left or every cell holds a coin. It pays the sum of the held coins, left in `bonusCoins`.
 */
export function holdAndSpin(mechanics: Mechanics, spec: HoldAndSpin): Mechanic {
    const {cells, respins, heldAtStart, hitWeight, missWeight, prizes, afterCoins} = spec;
    // Whether a coin lands on a cell and the prize it would carry are both drawn whatever the cell holds.
    const landing: readonly Outcome[] = [[1, hitWeight], [0, missWeight]];
    const respin = new Flow()
        .write('release-draws', 'The previous draw buffer is dead before the next ordered draw', 'respinDraws', emptyArray())
        .drawManySchedule('draws', 'Land, prize, then the next cell: keep the scalar draw order', 'respinDraws',
            Array.from({length: cells}, () => [landing, prizes]).flat());
    const landIndex = mul(state('place'), num(2)), prizeIndex = add(landIndex, num(1));
    respin
        .write('hold', 'A coin that lands on an empty cell is held, worth its prize × the stake', 'coins',
            mapOver('coins', when(and(eq(at('respinDraws', landIndex), str(1)), eq(state('item'), num(0))), prizeCredits('respinDraws', prizeIndex), state('item')), cellNumber))
        .write('respins', `More coins than before restore the ${respins} respins; a respin without a new coin uses one up`, 'respins',
            when(gt(heldCoins(), state('coinCount')), num(respins), sub(state('respins'), num(1))))
        .write('count', 'Count the coins held now', 'coinCount', heldCoins());
    recordHoldEvent(respin, 'trace', str('respin'));
    if (afterCoins) respin.use('after', afterCoins.label, afterCoins.mechanic);

    const flow = new Flow()
        .write('release-prize-draws', 'The held coin values retain the prizes; their old draw buffer is no longer needed', 'coinPrizeDraws', emptyArray())
        .write('respins', `Start with ${respins} respins`, 'respins', num(respins))
        .write('count', 'Count the coins held at the start', 'coinCount', heldCoins());
    recordHoldEvent(flow, 'trace', str('start'));
    if (afterCoins) flow.use('after', afterCoins.label, afterCoins.mechanic);
    // Every coin that lands is followed by at most `respins` respins, so the feature cannot outlast this many;
    // the loop is given one more, so that reaching its limit would mean a fault and never a played-out feature.
    const longest = (cells - heldAtStart) * respins;
    return flow.repeat('respin', 'Respin while respins are left and a cell is empty', longest + 1,
        mechanics.add(respin.mechanic('respin', `One respin: each of the ${cells} cells may receive a coin, and the respins are restored or used up.`)),
        {until: or(lte(state('respins'), num(0)), gte(state('coinCount'), num(cells))), bodyLabel: 'Respin'})
        .write('collect', 'The feature pays the sum of the held coins', 'bonusCoins', sumOver('coins', state('item'), {itemType: 'Number'}))
        .mechanic('hold-and-spin', `Hold & Spin: held coins stay while the other cells respin. A new coin restores the ${respins} respins; the feature ends when they run out or the board is full, and pays every held coin.`);
}
