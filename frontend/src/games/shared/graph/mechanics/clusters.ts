import { add, and, append, appended, at, contains, countOver, emptyArray, eq, filterOver, foldOver, gt, gte, has, iteration, length, lt, mapOver, min, mul, not, num, or, state, str, toNumber, when, type Expr } from '../expr';
import { Flow, Mechanics, type Mechanic } from '../flow';

export interface ClusterBoard {
  columns: number;
  rows: number;
  wild: number;
  /** How many symbols the game has; symbol ids run from 0. */
  symbolCount: number;
  /** `pays[symbol][size]` is what a group of that many cells pays, in credits. A symbol without pays never forms a group. */
  pays: Record<number, Record<string, number>>;
  /**
   * State key of an array that says how much every cell counts towards the size of its group, 1 or 0. Without
   * it every cell counts 1. A game uses it for a symbol that covers several cells and counts once.
   */
  weights?: string;
}

const cellsOf = ({ columns, rows }: ClusterBoard) => columns * rows;
/** A cell that is not part of any group of the symbol being looked at carries this number. */
const outside = (board: ClusterBoard) => 2 * cellsOf(board);
const zeros = (count: number) => Array.from({ length: count }, () => 0);

/**
 * What the cluster subgraphs leave about the groups that paid, in the order they were paid: the symbol, how many
 * cells, the pay in credits, and for every cell of the board 1 if it belongs to the group. They are kept only in
 * a traced (played) round and describe the last board paid.
 */
export const clusterTrace = ['hitSymbols', 'hitSizes', 'hitPays', 'hitCells'] as const;

/**
 * State the cluster subgraphs read and write. The board is `board`, numbered column by column from the top.
 * `adjacent` lists the four neighbours of every cell (the cell itself where the board ends), and `clusterPays`
 * holds the pay of every symbol for every group size, 0 where a group is too small.
 */
export function clusterState(board: ClusterBoard) {
  const { columns, rows, symbolCount, pays } = board, cells = cellsOf(board);
  const neighbour = (cell: number, column: number, row: number) =>
    column >= 0 && column < columns && row >= 0 && row < rows ? column * rows + row : cell;
  const sizes = Object.values(pays).flatMap(row => Object.entries(row).filter(([, pay]) => pay > 0).map(([size]) => Number(size)));
  return {
    adjacent: Array.from({ length: cells }, (_, cell) => {
      const column = Math.floor(cell / rows), row = cell % rows;
      return [neighbour(cell, column, row - 1), neighbour(cell, column, row + 1), neighbour(cell, column - 1, row), neighbour(cell, column + 1, row)];
    }).flat(),
    clusterPays: Array.from({ length: symbolCount }, (_, symbol) => Array.from({ length: cells + 1 }, (_, size) => pays[symbol]?.[size] ?? 0)).flat(),
    clusterMinSize: Math.min(...sizes),
    payingSymbols: Object.keys(pays).map(String),
    noCells: zeros(cells),
    clusterSymbols: [], clusterSymbol: '0', clusterIndex: 0, clusterCoins: 0,
    groups: zeros(cells), groupsBefore: zeros(cells), groupStarts: [0], groupIndex: 0, groupStart: 0, groupSize: 0, groupPay: 0,
    wildTaken: zeros(cells), paidSymbols: [], removed: zeros(cells),
    ...Object.fromEntries(clusterTrace.map(key => [key, []])),
  };
}

const numbered = { index: 'place', itemType: 'Number' } as const;
const place = () => state('place');
const item = () => state('item');

/**
 * One pass of joining. Every cell of a group carries a number; here each takes the smallest number among itself
 * and its four neighbours, so touching cells end up with the same number.
 */
function joinGroups(board: ClusterBoard): Mechanic {
  const neighbour = (direction: number) => at('groups', at('adjacent', add(mul(place(), num(4)), num(direction))));
  return new Flow()
    .write('before', 'Remember the numbers before this pass', 'groupsBefore', state('groups'))
    .write('join', 'Every cell takes the smallest number among itself and its four neighbours', 'groups',
      mapOver('groups', when(gte(item(), num(outside(board))), item(),
        min(min(item(), neighbour(0)), min(min(neighbour(1), neighbour(2)), neighbour(3)))), numbered))
    .mechanic('join-groups', 'One pass of joining touching cells: each takes the smallest number among itself and its neighbours.');
}

/** A group that pays: its pay is added, its wilds are used up, and it is remembered for the trace. */
function claimGroup({ wild }: ClusterBoard): Mechanic {
  const inGroup = (cell: Expr) => eq(at('groups', cell), state('groupStart'));
  return new Flow()
    .write('pay', 'Add what the group pays', 'clusterCoins', add(state('clusterCoins'), state('groupPay')))
    .write('wilds', 'Its wilds are used up: no group of another symbol can count them', 'wildTaken',
      mapOver('wildTaken', when(and(inGroup(place()), eq(at('board', place()), str(wild))), num(1), item()), numbered))
    .write('symbol', 'Remember that this symbol has paid', 'paidSymbols',
      when(has('paidSymbols', state('clusterSymbol')), state('paidSymbols'), append('paidSymbols', state('clusterSymbol'))))
    .record('trace-symbol', 'Remember the symbol', 'hitSymbols', state('clusterSymbol'))
    .record('trace-size', 'Remember the size', 'hitSizes', state('groupSize'))
    .record('trace-pay', 'Remember the pay', 'hitPays', state('groupPay'))
    .record('trace-cells', 'Remember the cells', 'hitCells', mapOver('groups', when(eq(item(), state('groupStart')), num(1), num(0)), { itemType: 'Number' }))
    .mechanic('claim-group', 'A group that pays: its pay is added to the board\'s, and the wilds in it cannot be used by another symbol.');
}

/** One group of the symbol: its size is counted and looked up in the paytable. */
function payGroup(mechanics: Mechanics, board: ClusterBoard): Mechanic {
  const inGroup = eq(item(), state('groupStart'));
  return new Flow()
    .write('start', 'A group is known by its first cell', 'groupStart', at('groupStarts', state('groupIndex')))
    .write('size', 'Count the cells of the group, wilds included', 'groupSize', board.weights
      ? foldOver('groups', num(0), add(state('acc'), when(inGroup, at(board.weights, place()), num(0))), numbered)
      : countOver('groups', inGroup, { itemType: 'Number' }))
    .write('pay', 'Look up what a group of this size pays', 'groupPay',
      at('clusterPays', add(mul(toNumber(state('clusterSymbol')), num(cellsOf(board) + 1)), state('groupSize'))))
    .onlyIf('pays', 'A group that pays is claimed', gt(state('groupPay'), num(0)), mechanics.add(claimGroup(board)))
    .mechanic('pay-group', 'One group of touching symbols: its size selects its pay, and a group that pays is claimed.');
}

/** Finds the groups of the symbol in `clusterSymbol` once its cells are numbered, and pays each. */
function symbolGroups(mechanics: Mechanics, board: ClusterBoard): Mechanic {
  const cells = cellsOf(board);
  return new Flow()
    .write('fresh', 'Nothing has been joined yet', 'groupsBefore', state('noCells'))
    // Numbers spread one cell a pass, so a group is joined within as many passes as it has cells, and the pass
    // after that changes nothing. The loops here are given one pass more than they can need: a loop that uses up
    // its whole limit is reported as cut short by the model.
    .repeat('join', 'Join touching cells until a pass changes nothing', cells + 1, mechanics.add(joinGroups(board)),
      { until: eq(state('groups'), state('groupsBefore')), bodyLabel: 'Join' })
    .write('starts', 'Every group now carries the number of its first cell: list the groups', 'groupStarts',
      filterOver('groups', eq(item(), place()), numbered))
    .repeat('group', 'Pay every group', Math.ceil(cells / 2) + 1, mechanics.add(payGroup(mechanics, board)),
      { until: gte(iteration('group'), length('groupStarts')), bodyLabel: 'Pay one group', counter: 'groupIndex' })
    .mechanic('symbol-groups', 'Joins the touching cells of one symbol into groups and pays every group.');
}

/** The groups of one symbol of the board, taken in the order the symbols first appear. */
function symbolClusters(mechanics: Mechanics, board: ClusterBoard): Mechanic {
  const cells = cellsOf(board);
  const free = and(eq(item(), str(board.wild)), eq(at('wildTaken', place()), num(0)));
  return new Flow()
    .write('symbol', 'The symbol to look at', 'clusterSymbol', at('clusterSymbols', state('clusterIndex')))
    .write('number', 'Number the cells of the symbol; a wild no group has used counts as the symbol', 'groups',
      mapOver('board', when(eq(item(), state('clusterSymbol')), place(), when(free, add(place(), num(cells)), num(outside(board)))), { index: 'place' }))
    .onlyIf('enough', 'With fewer cells than the smallest group that pays, nothing can pay',
      gte(countOver('groups', lt(item(), num(outside(board))), { itemType: 'Number' }), state('clusterMinSize')), mechanics.add(symbolGroups(mechanics, board)))
    .mechanic('symbol-clusters', 'The groups of one symbol: touching cells of the symbol, and the wilds between them, form a group.');
}

/**
 * Cluster pays on `board`. Cells of one symbol that touch side by side form a group, wilds standing in for the
 * symbol, and a group pays by its size. Symbols are looked at in the order they first appear on the board, and a
 * wild used by a group that pays cannot be used by a later symbol. Leaves the pay of the board in `clusterCoins`
 * and marks in `removed` the cells that leave it: every cell of a symbol that paid, and the wilds its groups used.
 */
export function clusterPays(mechanics: Mechanics, board: ClusterBoard): Mechanic {
  const flow = new Flow()
    .write('reset-pay', 'Nothing has paid yet', 'clusterCoins', num(0))
    .write('reset-wilds', 'Every wild is free', 'wildTaken', state('noCells'))
    .write('reset-symbols', 'No symbol has paid yet', 'paidSymbols', emptyArray());
  for (const key of clusterTrace) flow.write(`reset-${key}`, 'Start the trace of this board', key, emptyArray());
  return flow
    .write('order', 'List the paying symbols in the order they first appear on the board', 'clusterSymbols',
      foldOver('board', emptyArray(), when(and(has('payingSymbols', item()), not(contains(state('acc'), item()))), appended(state('acc'), item()), state('acc'))))
    .repeat('symbol', 'Pay the groups of every symbol on the board', Object.keys(board.pays).length + 1, mechanics.add(symbolClusters(mechanics, board)),
      { until: gte(iteration('symbol'), length('clusterSymbols')), bodyLabel: 'One symbol', counter: 'clusterIndex' })
    .write('removed', 'Every symbol that paid leaves the board, with the wilds its groups used', 'removed',
      mapOver('board', when(or(has('paidSymbols', item()), eq(at('wildTaken', place()), num(1))), num(1), num(0)), { index: 'place' }))
    .mechanic('cluster-pays', 'Cluster pays: touching cells of one symbol form a group that pays by its size; wilds stand in for any symbol, once.');
}
