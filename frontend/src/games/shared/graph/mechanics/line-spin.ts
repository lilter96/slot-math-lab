import { add, eq, gte, mapOver, mul, num, state, str, when } from '../expr';
import { Flow, type Mechanic, type Outcome } from '../flow';

export interface LineSpin {
  /** Subgraph that stops the reels and fills `board`. */
  reels: string;
  /** Subgraph that evaluates every payline into `lineCoins`. */
  paylines: string;
  /** Subgraph that pays scatters into `scatterCoins`. */
  scatterPay: string;
  scatter: number;
  /** A placeholder symbol on the strips that one symbol, drawn once per spin, replaces everywhere. */
  mystery?: { placeholder: number; outcomes: readonly Outcome[] };
  /** State key of a multiplier applied to the whole spin (for example, doubled free spins). */
  multiplierKey?: string;
  /** Leaves in `key` whether the spin showed `count` or more scatters, for the feature such a spin starts. */
  trigger?: { count: number; key: string };
  /** Name and description of the subgraph, when a game has more than one kind of spin. */
  name?: string;
  description?: string;
}

/** What one spin leaves in the trace of a played round, one entry per spin. */
export const lineSpinTrace = ['spinBoards', 'spinLinePayouts', 'spinLineSymbols', 'spinLineCounts', 'spinScatters', 'spinScatterWins', 'spinWins'] as const;
export const lineSpinState = () => ({ board: [], scatterCount: 0, scatterCoins: 0, spinCoins: 0, roundCoins: 0,
  ...Object.fromEntries(lineSpinTrace.map(key => [key, []])) });

/** One complete spin of a payline game: reels, optional mystery symbol, paylines, scatters, and the spin added to the round. */
export function lineSpin({ reels, paylines, scatterPay, scatter, mystery, multiplierKey, trigger, name = 'spin', description }: LineSpin): Mechanic {
  const flow = new Flow();
  if (mystery) flow.draw('mystery', 'Draw this spin\'s mystery symbol', 'mysterySymbol', mystery.outcomes);
  flow.use('reels', 'Spin the reels', reels);
  if (mystery) flow.write('reveal', 'Every mystery cell shows the drawn symbol', 'board',
    mapOver('board', when(eq(state('item'), str(mystery.placeholder)), state('mysterySymbol'), state('item'))));
  flow.use('lines', 'Pay the lines', paylines)
    .use('scatters', 'Pay the scatters', scatterPay, { symbol: scatter });
  if (trigger) flow.write('trigger', `${trigger.count} or more scatters trigger the feature`, trigger.key, gte(state('scatterCount'), num(trigger.count)));
  const win = add(state('lineCoins'), state('scatterCoins'));
  flow.write('win', multiplierKey ? 'Spin win: (lines + scatters) × spin multiplier' : 'Spin win: lines + scatters', 'spinCoins',
    multiplierKey ? mul(win, state(multiplierKey)) : win)
    .write('round', 'Add the spin to the round', 'roundCoins', add(state('roundCoins'), state('spinCoins')))
    .record('trace-board', 'Remember the board', 'spinBoards', state('board'))
    .record('trace-payouts', 'Remember the line payouts', 'spinLinePayouts', state('linePayouts'))
    .record('trace-symbols', 'Remember the line symbols', 'spinLineSymbols', state('lineSymbols'))
    .record('trace-counts', 'Remember the line lengths', 'spinLineCounts', state('lineCounts'))
    .record('trace-scatters', 'Remember the scatter count', 'spinScatters', state('scatterCount'))
    .record('trace-scatter-win', 'Remember how much of the spin win the scatters paid', 'spinScatterWins',
      multiplierKey ? mul(state('scatterCoins'), state(multiplierKey)) : state('scatterCoins'))
    .record('trace-win', 'Remember the spin win', 'spinWins', state('spinCoins'));
  return flow.mechanic(name, description ?? 'One spin: the reels stop, the lines and the scatters are paid, and the win is added to the round.');
}
