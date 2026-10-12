import {Flow, gameGraph, Mechanics} from '../../src/games/shared/graph/flow';
import {coinPrizes, coinState} from '../../src/games/shared/graph/mechanics/hold-and-spin';
import {state, sumOver} from '../../src/games/shared/graph/expr';

/** One coin, stake two, prizes 1 with weight three and 5 with weight one: expected credits (3×2+1×10)/4 = 4. */
export function prizeStudioProof() {
    const mechanics = new Mechanics(),
        root = new Flow().use('prizes', 'Draw a prize for the coin', mechanics.add(coinPrizes({
            cells: 1,
            symbol: 1,
            prizes: [[1, 3], [5, 1]]
        })))
            .write('pay', 'Pay the coin', 'coinsWon', sumOver('coins', state('item'), {itemType: 'Number'})).sink('sink', 'Credits', 'coinsWon', 100);
    return gameGraph({
        id: 'prize-studio-proof',
        name: 'Independent prize proof',
        description: 'Changing prize one to three yields expected seven credits; equal weights then yield eight.',
        root,
        mechanics,
        initialState: {...coinState(1, 2), board: ['1'], roundCost: 1},
        tables: {
            boardConfig: {columns: 1, rows: 1},
            symbols: [{id: '0', name: 'A', kind: 'Standard'}, {id: '1', name: 'Coin', kind: 'Money'}],
            paytables: [{id: 'private-prizes', entries: [{symbolId: '1', counts: [1, 2], payouts: ['2', '10']}]}]
        }
    });
}
