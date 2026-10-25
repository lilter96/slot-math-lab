import {
    add,
    and,
    at,
    cellSet,
    emptyArray,
    eq,
    except,
    type Expr,
    firstOf,
    foldOver,
    gt,
    gte,
    inSet,
    intersect,
    lt,
    mapOver,
    moved,
    mul,
    not,
    num,
    only,
    or,
    sizeOf,
    state,
    toNumber,
    union,
    when
} from '../expr';
import {Flow, type Mechanic, Mechanics} from '../flow';
import {cellsShowing, readBoard, type SymbolBoard, symbolCellState} from './symbol-cells';
import {candidateState, clusterCandidates} from './cluster-candidates';

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
    /** An alternative to numeric weights: the mask of cells that contribute one; other cells contribute zero. */
    weightsMask?: string;
    /** Select the previous grid composition when preserving a saved authoring contract. */
    componentTraversal?: 'grid' | 'adjacency';
}

const hasWeights = (board: ClusterBoard) => Boolean(board.weights || board.weightsMask);
const cellsOf = ({columns, rows}: ClusterBoard) => columns * rows;
// JSON authored in JavaScript must keep every adjacency mask exact. Larger boards
// retain the existing expression-based traversal rather than round a numeric mask.
const hasPreparedAdjacency = (board: ClusterBoard) => board.componentTraversal !== 'grid' && cellsOf(board) <= 53;
const symbolBoard = (board: ClusterBoard): SymbolBoard => ({cells: cellsOf(board), symbolCount: board.symbolCount});
/** The fewest cells a group needs to pay. */
const smallestGroup = ({pays}: ClusterBoard) =>
    Math.min(...Object.values(pays).flatMap(row => Object.entries(row).filter(([, pay]) => pay > 0).map(([size]) => Number(size))));

/**
 * What the cluster subgraphs leave about the groups that paid, in the order they were paid: the symbol, how many
 * cells, the pay in credits, and for every cell of the board 1 if it belongs to the group. They are kept only in
 * a traced (played) round and describe the last board paid.
 */
export const clusterTrace = ['hitSymbols', 'hitSizes', 'hitPays', 'hitCells'] as const;

/**
 * State the cluster subgraphs read and write. The board is `board`, numbered column by column from the top.
 * `clusterPays` holds the pay of every symbol for every group size, 0 where a group is too small. A set of cells
 * (`group`, `symbolCells`, `wildCells`, `removed` and the like) is one number.
 */
export function clusterState(board: ClusterBoard) {
    const {symbolCount, pays} = board, cells = cellsOf(board);
    return {
        clusterPays: Array.from({length: symbolCount}, (_, symbol) => Array.from({length: cells + 1}, (_, size) => pays[symbol]?.[size] ?? 0)).flat(),
        ...(hasPreparedAdjacency(board) ? {
            clusterAdjacency: Array.from({length: cells}, (_, cell) => {
                const adjacent = [cell - board.rows, cell + board.rows];
                if (cell % board.rows > 0) adjacent.push(cell - 1);
                if (cell % board.rows < board.rows - 1) adjacent.push(cell + 1);
                return adjacent.filter(neighbor => neighbor >= 0 && neighbor < cells)
                    .reduce((mask, neighbor) => mask + 2 ** neighbor, 0);
            })
        } : {}),
        clusterMinSize: smallestGroup(board),
        clusterSymbol: '0',
        clusterCoins: 0,
        unpaidCells: 0,
        wildCells: 0,
        wildsTaken: 0,
        symbolCells: 0,
        joinable: 0,
        ungrouped: 0,
        group: 0,
        reached: 0,
        groupSize: 0,
        groupPay: 0,
        paidCells: 0,
        removed: 0,
        ...symbolCellState(symbolBoard(board)),
        ...candidateState(),
        ...Object.fromEntries(clusterTrace.map(key => [key, []])),
    };
}

const place = () => state('place');
const item = () => state('item');

/**
 * The cells that touch a cell of `set` side by side. Cells are numbered column by column from the top, so the cell
 * below is the next one, unless the column ends there, and the cell to the right is one column of cells further.
 */
function touching(set: Expr, {columns, rows}: ClusterBoard): Expr {
    const cells = Array.from({length: columns * rows}, (_, cell) => cell);
    const aboveAnother = cellSet(cells.filter(cell => cell % rows < rows - 1)),
        belowAnother = cellSet(cells.filter(cell => cell % rows > 0));
    return union(moved(intersect(set, aboveAnother), 1), moved(intersect(set, belowAnother), -1), moved(set, rows), moved(set, -rows));
}

/** One pass of growing a group: the cells that touch what the last pass reached join it. */
function growGroup(board: ClusterBoard): Mechanic {
    return new Flow()
        .write('reach', 'The cells that touch the ones just reached, can join and are not in the group yet', 'reached',
            except(intersect(touching(state('reached'), board), state('joinable')), state('group')))
        .write('join', 'They join the group', 'group', union(state('group'), state('reached')))
        .mechanic('grow-group', 'One pass of growing a group: the cells that touch it side by side and can join it do.');
}

/** A group that pays: its pay is added, its wilds are used up, and it is remembered for the trace. */
function claimGroup(): Mechanic {
    return new Flow()
        .write('pay', 'Add what the group pays', 'clusterCoins', add(state('clusterCoins'), state('groupPay')))
        .write('wilds', 'Its wilds are used up: no group of another symbol can count them', 'wildsTaken',
            union(state('wildsTaken'), intersect(state('group'), state('wildCells'))))
        .write('symbol', 'Every cell of a symbol that has paid leaves the board', 'paidCells', union(state('paidCells'), state('symbolCells')))
        .record('trace-symbol', 'Remember the symbol', 'hitSymbols', state('clusterSymbol'))
        .record('trace-size', 'Remember the size', 'hitSizes', state('groupSize'))
        .record('trace-pay', 'Remember the pay', 'hitPays', state('groupPay'))
        .record('trace-cells', 'Remember the cells', 'hitCells', mapOver('board', when(inSet(state('group'), place()), num(1), num(0)), {index: 'place'}))
        .mechanic('claim-group', 'A group that pays: its pay is added to the board\'s, and the wilds in it cannot be used by another symbol.');
}

/** One group of the symbol: it grows from one cell, its size is counted and looked up in the paytable. */
function payGroup(mechanics: Mechanics, board: ClusterBoard): Mechanic {
    const cells = cellsOf(board);
    const flow = new Flow()
        .write('start', 'A group starts from the first cell of the symbol that is in no group yet', 'reached', only(firstOf(state('ungrouped'))));
    if (hasPreparedAdjacency(board)) {
        flow.write('complete', 'All allowed cells reachable from the seed', 'group', {
            exprType: 'reachable',
            vertexCount: cells,
            adjacency: state('clusterAdjacency'),
            allowed: state('joinable'),
            seeds: state('reached')
        })
            .write('exhausted', 'The complete component has no pending frontier', 'reached', num(0));
    } else {
        flow.write('alone', 'So far the group is that cell', 'group', state('reached'))
            // Every pass but the last adds a cell, so a group is whole within as many passes as the board has cells. The
            // loops here are given one pass more than they can need: a loop that uses up its whole limit is reported as
            // cut short by the model.
            .use('first-neighbors', 'Reach the first neighbors', mechanics.add(growGroup(board)))
            .use('second-neighbors', 'Reach the next neighbors', mechanics.add(growGroup(board)))
            .repeat('grow', 'Grow the group until no more cells can join', Math.max(1, cells - 1), mechanics.add(growGroup(board)),
                {until: eq(state('reached'), num(0)), bodyLabel: 'Grow'});
    }
    return flow
        .write('grouped', 'Its cells are in a group now', 'ungrouped', except(state('ungrouped'), state('group')))
        .write('size', 'Count the cells of the group, wilds included', 'groupSize', board.weightsMask
            ? sizeOf(intersect(state('group'), state(board.weightsMask)))
            : board.weights
                ? foldOver(board.weights, num(0), add(state('acc'), when(inSet(state('group'), place()), item(), num(0))), {
                    index: 'place',
                    itemType: 'Number'
                })
                : sizeOf(state('group')))
        .write('pay', 'Look up what a group of this size pays', 'groupPay',
            at('clusterPays', add(mul(toNumber(state('clusterSymbol')), num(cells + 1)), state('groupSize'))))
        .onlyIf('pays', 'A group that pays is claimed', gt(state('groupPay'), num(0)), mechanics.add(claimGroup()))
        .mechanic('pay-group', 'One group of touching symbols: it grows from one cell, its size selects its pay, and a group that pays is claimed.');
}

/** Finds the groups of the symbol in `clusterSymbol`, one after another, and pays each. */
function symbolGroups(mechanics: Mechanics, board: ClusterBoard): Mechanic {
    const done = eq(state('ungrouped'), num(0));
    return new Flow()
        // No two groups touch, so a board has at most one group for every two cells.
        .repeat('group', 'Pay every group', Math.ceil(cellsOf(board) / 2) + 1, mechanics.add(payGroup(mechanics, board)),
            {
                until: !hasWeights(board) ? when(state('useCandidates'), done, or(done,
                    lt(add(sizeOf(state('ungrouped')), sizeOf(state('wildCells'))), state('clusterMinSize')))) : done,
                bodyLabel: 'Pay one group'
            })
        .mechanic('symbol-groups', 'Pays every group of one symbol, starting with the group of its first cell.');
}

/** The groups of one symbol of the board, taken in the order the symbols first appear. */
function symbolClusters(mechanics: Mechanics, board: ClusterBoard): Mechanic {
    /** Whether a cell on its own is a group that pays. */
    const alone = smallestGroup(board) < 2;
    const cells = Array.from({length: cellsOf(board)}, (_, cell) => cell);
    // A connected k-cell component contains at least k - 1 undirected edges. Count each vertical/horizontal
    // edge once. Wilds still follow first-occurrence order; this rejects only impossible paying components.
    const edges = add(sizeOf(intersect(state('joinable'), moved(state('joinable'), board.rows))),
        sizeOf(intersect(state('joinable'), moved(intersect(state('joinable'), cellSet(cells.filter(cell => cell % board.rows < board.rows - 1))), 1))));
    const enough = gte(sizeOf(state('joinable')), state('clusterMinSize'));
    return new Flow()
        .write('symbol', 'The symbol to look at: the one on the first cell not looked at yet', 'clusterSymbol', at('board', firstOf(state('unpaidCells'))))
        .write('cells', 'The cells that show the symbol', 'symbolCells', cellsShowing(symbolBoard(board), state('clusterSymbol')))
        .write('looked', 'They have been looked at now', 'unpaidCells', except(state('unpaidCells'), state('symbolCells')))
        .write('joinable', 'A wild no group has used counts as the symbol', 'joinable',
            union(state('symbolCells'), except(state('wildCells'), state('wildsTaken'))))
        .write('ungrouped', alone ? 'None of its cells is in a group yet' : 'None of its cells is in a group yet; a cell that touches nothing it could join forms none',
            'ungrouped', when(state('useCandidates'), intersect(state('symbolCells'), state('candidateCells')),
                alone ? state('symbolCells') : intersect(state('symbolCells'), touching(state('joinable'), board))))
        .onlyIf('enough', 'With fewer cells than the smallest group that pays, nothing can pay',
            !hasWeights(board) ? when(state('useCandidates'), {exprType: 'constant', kind: 'Boolean', value: 'true'},
                and(enough, gte(edges, num(smallestGroup(board) - 1)))) : enough,
            mechanics.add(symbolGroups(mechanics, board)))
        .mechanic('symbol-clusters', 'The groups of one symbol: touching cells of the symbol, and the wilds between them, form a group.');
}

/**
 * Cluster pays on `board`. Cells of one symbol that touch side by side form a group, wilds standing in for the
 * symbol, and a group pays by its size. Symbols are looked at in the order they first appear on the board, and a
 * wild used by a group that pays cannot be used by a later symbol. Leaves the pay of the board in `clusterCoins`
 * and in `removed` the set of cells that leave it: every cell of a symbol that paid, and the wilds its groups used.
 */
export function clusterPays(mechanics: Mechanics, board: ClusterBoard, afterRead?: {
    mechanic: string;
    label: string
}): Mechanic {
    const flow = new Flow()
        .write('reset-pay', 'Nothing has paid yet', 'clusterCoins', num(0))
        .write('reset-wilds', 'Every wild is free', 'wildsTaken', num(0))
        .write('reset-paid', 'No symbol has paid yet', 'paidCells', num(0));
    for (const key of clusterTrace) flow.write(`reset-${key}`, 'Start the trace of this board', key, emptyArray());
    flow.use('read', 'Read the board: which cells show which symbol', mechanics.add(readBoard(symbolBoard(board))));
    // Callers can observe the prepared symbol sets without scanning the same board a second time.
    if (afterRead) flow.use('after-read', afterRead.label, afterRead.mechanic);
    flow
        .write('wilds', 'The cells that show a wild', 'wildCells', cellsShowing(symbolBoard(board), board.wild))
        .write('paying', 'The cells that show a symbol that can pay; none has been looked at yet', 'unpaidCells',
            union(...Object.keys(board.pays).map(symbol => cellsShowing(symbolBoard(board), Number(symbol)))));
    // Candidate seeds may change group order. Only wild-free, untraced evaluation can use them: wild claims
    // and played-round group order retain their original first-cell traversal. Weighted groups retain the
    // general traversal because their physical size need not equal the size that selects their payout.
    flow.write('candidate-mode', 'Use size-five seed pruning only without wilds and ordered traces', 'useCandidates',
        smallestGroup(board) >= 5 && !hasWeights(board) ? and(eq(state('wildCells'), num(0)), not(state('traceEnabled')))
            : {exprType: 'constant', kind: 'Boolean', value: 'false'});
    if (smallestGroup(board) >= 5 && !hasWeights(board)) {
        flow.onlyIf('candidates', 'Discard components that cannot reach five cells', state('useCandidates'),
            mechanics.add(clusterCandidates(board)))
            .write('candidate-symbols', 'Only symbols with a component large enough to pay need traversal', 'unpaidCells',
                when(state('useCandidates'), state('candidateCells'), state('unpaidCells')));
    }
    return flow
        .repeat('symbol', 'Pay the groups of every symbol on the board, in the order the symbols first appear', Object.keys(board.pays).length + 1,
            mechanics.add(symbolClusters(mechanics, board)), {
                until: eq(state('unpaidCells'), num(0)),
                bodyLabel: 'One symbol'
            })
        .write('removed', 'Every symbol that paid leaves the board, with the wilds its groups used', 'removed', union(state('paidCells'), state('wildsTaken')))
        .mechanic('cluster-pays', 'Cluster pays: touching cells of one symbol form a group that pays by its size; wilds stand in for any symbol, once.');
}
