import {add, at, gte, moved, mul, num, sizeOf, state, toNumber, union, type Expr} from '../expr';
import {Flow, type Mechanic, type Mechanics} from '../flow';
import {PackedVector} from '../packed-vector';
import {lineSpinTrace, type LineSpin} from './line-spin';
import {stopReels, stopReelsShowing, type ReelSet, type Showing} from './reels';

export interface PackedLineSpin extends Omit<LineSpin, 'reels' | 'scatterPay'> {
    reels: ReelSet;
    vector: PackedVector;
    showing?: Showing;
}

const windowsKey = (reels: ReelSet, column: number) => `${reels.stripKey}PackedWindow${column}`;

/** Each immutable lookup contains one local reel window; runtime shifts place it in the full vector. */
export function packedReelState(reels: ReelSet, vector: PackedVector) {
    if (reels.rows * reels.strips.length !== vector.length) throw new Error('Reels and packed geometry disagree');
    return Object.fromEntries(reels.strips.map((strip, column) => [windowsKey(reels, column),
        strip.map((_, stop) => vector.encode(Array.from({length: reels.rows}, (_, row) => Number(strip[(stop + row) % strip.length]))))]));
}

/** Stops preserve their original scalar draw order and outcomes; window materialization is numeric. */
export function packedLineSpin(mechanics: Mechanics, spec: PackedLineSpin): Mechanic {
    const {reels, vector, mystery, multiplierKey, trigger, showing, name = 'spin', description} = spec;
    const flow = new Flow();
    if (mystery) flow.draw('mystery', 'Draw the mystery symbol', 'mysterySymbol', mystery.outcomes);
    const stops = showing ? stopReelsShowing(reels, showing) : stopReels(reels);
    const word = union(...reels.strips.map((_, column) => moved(
        at(windowsKey(reels, column), toNumber(state(`stop${column}`))), column * reels.rows * vector.bits)));
    stops.write('window', 'Symbols showing in the window', vector.key, word);
    flow.use('reels', 'Stop the reels', mechanics.add(stops.mechanic(
        showing ? `${showing.name}-packed` : `${reels.name}-packed`, 'Stop every reel in its authored order and assemble the finite vector.')));
    if (mystery) flow.write('reveal', 'Replace every mystery cell with the drawn symbol', vector.key,
        union(vector.replace(mystery.placeholder, toNumber(state('mysterySymbol'))), num(0)));
    flow.use('lines', 'Pay the lines', spec.paylines)
        .write('scatter-count', 'Count scatters anywhere on the field', 'scatterCount', sizeOf(vector.matches(spec.scatter)))
        .write('scatter-pay', 'Scatter payout times the bet', 'scatterCoins', mul(at('scatterPays', state('scatterCount')), state('totalBet')));
    if (trigger) flow.write('trigger', 'Enough scatters trigger the feature', trigger.key, gte(state('scatterCount'), num(trigger.count)));
    const win = add(state('lineCoins'), state('scatterCoins'));
    flow.write('win', 'Payout of this spin', 'spinCoins', multiplierKey ? mul(win, state(multiplierKey)) : win)
        .write('round', 'Add the spin payout to the round', 'roundCoins', add(state('roundCoins'), state('spinCoins')));
    const values: Record<(typeof lineSpinTrace)[number], Expr> = {
        spinBoards: vector.decode(), spinLinePayouts: state('linePayouts'), spinLineSymbols: state('lineSymbols'),
        spinLineCounts: state('lineCounts'), spinScatters: state('scatterCount'),
        spinScatterWins: multiplierKey ? mul(state('scatterCoins'), state(multiplierKey)) : state('scatterCoins'),
        spinWins: state('spinCoins'),
    };
    for (const key of lineSpinTrace) flow.record(`trace-${key}`, 'Record the played spin', key, values[key]);
    return flow.mechanic(name, description ?? 'One spin: stop the reels, reveal symbols, pay lines and scatters, and record its result.');
}
