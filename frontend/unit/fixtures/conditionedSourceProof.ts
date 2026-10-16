import {Flow, gameGraph, Mechanics} from '../../src/games/shared/graph/flow';
import {reelState, showingState, spinReelsShowing} from '../../src/games/shared/graph/mechanics/reels';
import {add, and, at, eq, num, state, str, when} from '../../src/games/shared/graph/expr';

/** Each call draws two uniform S/X reels conditioned on at least one S.
 * SS is one of three equally likely boards: two calls pay 2/3 in expectation.
 * Making only the second call's first reel all-S changes its SS chance to 1/2: total 5/6. */
export function conditionedSourceProof() {
    const spec = {name: 'conditional-source', stripKey: 'strip', rows: 1, strips: [['S', 'A'], ['S', 'B']]},
        showing = {name: spec.name, symbol: 'S', symbolName: 'Scatter', atLeast: 1};
    const mechanics = new Mechanics(), source = mechanics.add(spinReelsShowing(spec, showing));
    const both = () => when(and(eq(at('board', num(0)), str('S')), eq(at('board', num(1)), str('S'))), num(1), num(0));
    const root = new Flow().use('first', 'First conditioned call', source).write('remember', 'Retain the first win', 'firstCoins', both()).use('second', 'Second conditioned call', source).write('sum', 'Sum both wins', 'coins', add(state('firstCoins'), both())).sink('sink', 'Payout', 'coins', 2);
    return gameGraph({
        id: 'conditioned-copy-proof',
        name: 'Conditioned source copy',
        description: 'Independent conditional law: 2/3 then 5/6.',
        root,
        mechanics,
        initialState: {
            emptyArray: [],
            firstCoins: 0,
            coins: 0,
            board: ['A', 'B'], ...reelState(spec), ...showingState(spec, showing)
        },
        tables: {
            boardConfig: {columns: 2, rows: 1},
            symbols: ['S', 'A', 'B'].map(id => ({
                id,
                name: id === 'S' ? 'Scatter' : id,
                kind: id === 'S' ? 'Scatter' : 'Standard'
            }))
        },
        opaque: {retain: true}
    });
}
