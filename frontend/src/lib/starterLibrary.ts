import { add, min, num, state } from '../games/shared/graph/expr';
import { Flow, Mechanics, gameGraph } from '../games/shared/graph/flow';
import { cascade, cascadeState, tumble } from '../games/shared/graph/mechanics/cascade';
import { cellState, spinCells, type CellBoard } from '../games/shared/graph/mechanics/cells';
import { clusterPays, clusterState, type ClusterBoard } from '../games/shared/graph/mechanics/clusters';
import { gameTables } from '../games/shared/graph/tables';
import { createAncientDragonGraph } from '../games/ancient-dragon/graph';
import { createWoodlandWhisperGraph } from '../games/woodland-whisper/graph';
import { createNineLivesGraph } from '../games/nine-lives-tgslots/graph';
import { createLeMilitareGraph } from '../games/le-militare/graph';
import { createX7ClubGraph } from '../games/x7-club/graph';

const names = ['W', 'A', 'B', 'C', 'D', 'E'];
const board: ClusterBoard = { columns: 6, rows: 5, wild: 0, symbolCount: names.length,
  pays: Object.fromEntries(names.slice(1).map((_, index) => [index + 1,
    Object.fromEntries(Array.from({ length: 26 }, (_, size) => [size + 5, (index + 1) * (size + 1)]))])) };
const cells: CellBoard = { name: 'draw-cells', cells: board.columns * board.rows,
  outcomes: names.map((_, symbol) => [symbol, symbol === 0 ? 1 : 20]) };

/** Working examples of the same editable subgraphs used by the migrated games. No payout is calculated here. */
export function createClusterStarter(cascading = false): Record<string, unknown> {
  const mechanics = new Mechanics();
  const evaluation = mechanics.add(clusterPays(mechanics, board));
  const root = new Flow().use('draw', 'Draw the board · independent cells', mechanics.add(spinCells(cells)));
  if (cascading) {
    const award = mechanics.add(new Flow()
      .write('add', 'Add this board to the round, up to the cap', 'roundCoins', min(num(5000), add(state('roundCoins'), state('clusterCoins'))))
      .mechanic('award-step', 'Accumulate a cascade payout. This example has no multiplier or bonus.'));
    root.use('cascade', 'Pay → remove → refill → repeat', mechanics.add(cascade(mechanics, {
      maxSteps: 30,
      pays: { mechanic: evaluation, label: 'Find and pay touching groups', coins: 'clusterCoins' },
      onStep: { mechanic: award, label: 'Add the step payout' },
      tumble: { mechanic: mechanics.add(tumble(mechanics, { columns: board.columns, rows: board.rows, retainSurvivors: false, refill: { cells: cells.outcomes } })), label: 'Fall and refill' },
    })));
  } else root.use('evaluate', 'Find and pay touching groups', evaluation);
  root.sink('payout', 'Round payout', cascading ? 'roundCoins' : 'clusterCoins', 5000);
  return gameGraph({
    id: cascading ? 'starter-cascade' : 'starter-clusters',
    name: cascading ? 'Starter · cascading clusters' : 'Starter · cluster pays',
    description: '6×5, column-major cells, groups of 5+, exclusive wilds. Illustrative integer weights and payouts; RTP is not calibrated. Inspect each subgraph and change the data tables.',
    root, mechanics,
    initialState: { roundCoins: 0, ...cellState(cells), ...clusterState(board), ...cascadeState({ columns: board.columns, rows: board.rows }) },
    tables: gameTables({ names, wild: 0, totalBet: 1, roundCost: 1, columns: board.columns, rows: board.rows,
      clusterPays: Object.fromEntries(Object.entries(board.pays).map(([symbol, pays]) => [names[Number(symbol)], pays])) }),
  });
}

export const starterLibrary = [
  { id: 'clusters', name: 'Cluster pays', summary: 'Independent draws → touching groups → payout. Bit planes and safe candidate pruning.', mechanic: 'cluster-pays', create: () => createClusterStarter() },
  { id: 'cascade', name: 'Cascading clusters', summary: 'Reuse the cluster evaluator. Add an award step, gravity and refill, then repeat.', mechanic: 'cascade', create: () => createClusterStarter(true) },
] as const;

/** The complete migrated models, available in Build without importing repository fixtures. */
export const gameModelLibrary = [
  { name: 'Nine Lives', create: () => createNineLivesGraph() },
  { name: 'Ancient Dragon', create: () => createAncientDragonGraph() },
  { name: 'Woodland Whisper', create: () => createWoodlandWhisperGraph() },
  { name: 'X7 Club', create: () => createX7ClubGraph() },
  { name: 'Le Militare', create: () => createLeMilitareGraph() },
] as const;
