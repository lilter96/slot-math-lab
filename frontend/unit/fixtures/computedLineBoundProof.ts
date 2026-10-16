import {Flow, gameGraph, Mechanics} from '../../src/games/shared/graph/flow';
import {div, eq, num, state, sub} from '../../src/games/shared/graph/expr';
import {lineSpin, lineSpinState} from '../../src/games/shared/graph/mechanics/line-spin';
import {linePayTable, paylines, paylineState} from '../../src/games/shared/graph/mechanics/paylines';
import {scatterPay, scatterPayTable} from '../../src/games/shared/graph/mechanics/scatter';

/** Independent fixture: 2-credit line, one paid + two actual x3 free spins, 2-credit round price.
 * Budget permits three free passes. Conservative source bound ceil((2 + 1) * (1 + 3*3) / 2) = 15.
 * Actual win is (2 + 2*6)/2 = 7. Raising line pay to 6 requires bound 35 and actual win 21. */
export function computedLineBoundProof() {
    const mechanics = new Mechanics(),
        fixed = mechanics.add(new Flow().mechanic('fixed-board', 'Keep the authored board'));
    const spin = mechanics.add(lineSpin({
        reels: fixed,
        paylines: mechanics.add(paylines(mechanics, [[0, 0]], 1)),
        scatterPay: mechanics.add(scatterPay()),
        scatter: 2,
        multiplierKey: 'spinMultiplier'
    }));
    const free = mechanics.add(new Flow().write('use', 'Use a free spin', 'freeSpins', sub(state('freeSpins'), num(1))).use('spin', 'Pay the free spin', spin).mechanic('free-pass', 'One free pass'));
    const feature = mechanics.add(new Flow().write('multiplier', 'Free multiplier', 'spinMultiplier', num(3)).repeat('play', 'At most three free passes', 3, free, {until: eq(state('freeSpins'), num(0))}).mechanic('feature', 'Free feature'));
    const root = new Flow().use('paid', 'One paid spin', spin).use('free', 'Two free spins', feature).write('credits', 'Normalize by price', 'winCredits', div(state('roundCoins'), state('roundCost'))).sink('sink', 'Conservative computed bound', 'winCredits', 15);
    return gameGraph({
        id: 'computed-bound-proof',
        name: 'Computed bound proof',
        description: 'Independent finite payout/bound fixture',
        root,
        mechanics,
        initialState: {
            ...lineSpinState(), ...paylineState(0, linePayTable({
                symbolCount: 3,
                wild: 0,
                pays: {1: {2: 2}},
                reels: 2
            })),
            board: ['1', '1'],
            totalBet: 1,
            roundCost: 2,
            spinMultiplier: 1,
            freeSpins: 2,
            winCredits: 0,
            scatterPays: scatterPayTable({2: 1}, 2)
        },
        tables: {
            boardConfig: {rows: 1, columns: 2},
            symbols: [{id: '0', name: 'Wild', kind: 'Wild'}, {id: '1', name: 'A', kind: 'Standard'}, {
                id: '2',
                name: 'Scatter',
                kind: 'Scatter'
            }],
            paylineSets: [{id: 'lines', paylines: [{positions: [0, 0]}]}],
            paytables: [{id: 'lines', entries: [{symbolId: '1', counts: [2], payouts: ['1']}]}, {
                id: 'scatters',
                entries: [{symbolId: '2', counts: [2], payouts: ['1/2']}]
            }]
        }
    });
}
