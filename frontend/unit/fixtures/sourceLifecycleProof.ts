import { Flow, Mechanics, gameGraph } from '../../src/games/shared/graph/flow';
import { reelState, spinReels } from '../../src/games/shared/graph/mechanics/reels';
import { cellState, spinCells } from '../../src/games/shared/graph/mechanics/cells';
import { add, at, eq, num, state, str, when } from '../../src/games/shared/graph/expr';
export function sharedDrawingProof(kind: 'cells' | 'reels' = 'reels') {
  const mechanics = new Mechanics(), reels = { name: 'shared-draw', stripKey: 'sharedStrip', rows: 1, strips: [['A', 'B']] }, cells = { name: 'shared-draw', cells: 1, outcomes: [['A', 1], ['B', 1]] as const };
  const source = mechanics.add(kind === 'reels' ? spinReels(reels) : spinCells(cells));
  const payout = when(eq(at('board', num(0)), str('A')), num(1), num(0));
  const root = new Flow().use('first', 'First use of the source', source).write('first-pay', 'First A pays one', 'firstPay', payout).use('second', 'Second use of the source', source)
    .write('second-pay', 'Second A pays one', 'secondPay', payout).write('total', 'Pay both draws', 'total', add(state('firstPay'), state('secondPay'))).sink('sink', 'Payout', 'total', 2);
  return gameGraph({ id: 'source-sharing-proof', name: `Independent ${kind} source proof`, description: 'Two draws each pay A with probability one half. A private all-B second source reduces expected payout from one to one half.', root, mechanics,
    initialState: { ...(kind === 'reels' ? reelState(reels) : cellState(cells)), firstPay: 0, secondPay: 0, total: 0 }, tables: { boardConfig: { columns: 1, rows: 1 }, symbols: [{ id: 'A', name: 'A', kind: 'Standard' }, { id: 'B', name: 'B', kind: 'Standard' }], opaque: { retained: true } } });
}
export function emptySourceProof() { return { schemaVersion: '1.0.0', id: 'source-creation-proof', name: 'Create an independent source', nodes: [{ id: 'sink', nodeType: 'metricsSink', winCap: 100, inputs: { in: { name: 'in', type: 'Wins' } } }], edges: [], opaque: { retained: true } }; }
