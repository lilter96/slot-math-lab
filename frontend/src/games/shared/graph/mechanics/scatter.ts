import { at, countOver, eq, mul, param, state, toText } from '../expr';
import { Flow, type Mechanic } from '../flow';

/**
 * The scatter paytable for `scatterPays`: entry `count` is the pay for that many scatters, as a multiple of the
 * total bet. It has an entry for every count the board could hold, so the lookup can never miss.
 */
export const scatterPayTable = (pays: Record<string, number>, cells: number): number[] =>
  Array.from({ length: cells + 1 }, (_, count) => pays[count] ?? 0);

/**
 * Scatter pay. Parameter `symbol` is the scatter. The number of scatters anywhere on `board` is looked up
 * in `scatterPays` (indexed by count) and multiplied by `totalBet`. Leaves `scatterCount` and `scatterCoins`.
 */
export function scatterPay(): Mechanic {
  return new Flow()
    .write('count', 'Count the scatters on the board', 'scatterCount', countOver('board', eq(state('item'), toText(param('symbol')))))
    .write('pay', 'Scatter payout × total bet', 'scatterCoins', mul(at('scatterPays', state('scatterCount')), state('totalBet')))
    .mechanic('scatter-pay', 'Scatters pay anywhere: their count selects a multiple of the total bet.');
}
