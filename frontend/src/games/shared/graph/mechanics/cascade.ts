import {
    add,
    append,
    at,
    choose,
    emptyArray,
    eq,
    filterOver,
    gt,
    gte,
    inSet,
    intersect,
    length,
    lt,
    mapOver,
    moved,
    mul,
    not,
    num,
    or,
    sizeOf,
    state,
    sub,
    when,
    type Expr
} from '../expr';
import {Flow, Mechanics, type Mechanic, type Outcome} from '../flow';
import {reelStop, reelSymbol, type ReelSet} from './reels';

/** Per-cell state that moves with the symbols when they fall, for example which cells are held. */
export interface Carried {
    /** State key of the array, one number per cell. */
    key: string;
    /** What the array is, for labels: `held cells`. */
    label: string;
    /** The value a cell gets when a new symbol drops into it. */
    fresh: number;
    /** The caller proves this vector is unchanged by every permitted fall; survivor bookkeeping remains available. */
    stationary?: boolean;
}

export interface Tumble {
    /** Subgraph name; a game whose spins refill from different reels names each of its tumbles. */
    name?: string;
    columns: number;
    rows: number;
    /**
     * Where the new symbols come from. `cells`: every empty cell draws its own symbol from one list of weights.
     * `reels`: every reel with empty cells stops once more, in `stop<reel>`, and the symbols from that stop on drop in.
     */
    refill: { cells: readonly Outcome[] } | { reels: ReelSet };
    carried?: readonly Carried[];
    /** Keep the survivor arrays for consumers that inspect them. Disable when only the settled board is needed. */
    retainSurvivors?: boolean;
}

export interface Cascade {
    /** Subgraph name; a game with several kinds of cascade names each. */
    name?: string;
    /** The most steps of one cascade that pay. */
    maxSteps: number;
    /** Subgraph that pays the board: leaves the pay in the state key `coins` and the set of cells to remove in `removed`. */
    pays: { mechanic: string; label: string; coins: string };
    /** Subgraph run for every step that pays, before its symbols are removed: what the game does with the pay. */
    onStep: { mechanic: string; label: string };
    /** Subgraph that removes the marked cells and fills the board again. */
    tumble: { mechanic: string; label: string };
}

/**
 * What a cascade leaves in the trace of a played round, one entry per step that paid: the board that paid, the
 * cells removed from it (1 for removed), and the board after the symbols fell and new ones dropped in.
 */
export const cascadeTrace = ['cascadeBoards', 'cascadeRemoved', 'cascadeAfter'] as const;

const survivorsOf = (key: string) => `${key}Survivors`;
const topDrop = (reel: number) => `topDrop${reel}`;

// Small columns have few removal patterns. Export their source-row table into the JSON, so a fall reads the old
// board directly instead of allocating a filtered copy. A negative source encodes a fresh row: -1 is the top.
// Taller columns retain the general filter composition, avoiding an exponentially growing table.
const usesSourceTable = (rows: number) => rows <= 8;
const settledSources = (rows: number) => Array.from({length: 2 ** rows}, (_, mask) => {
    const kept = Array.from({length: rows}, (_, row) => row).filter(row => (mask & (1 << row)) === 0);
    const empty = rows - kept.length;
    return Array.from({length: rows}, (_, row) => row < empty ? -1 - row : kept[row - empty]);
}).flat();

/**
 * State the cascade subgraphs read and write. `cascadeSteps` counts the steps of the running cascade that paid,
 * `survivors` are the symbols the last step left on the board, in board order, and `drops` the symbols drawn to
 * fill it, column by column, the lowest of a column first; a board filled from its reels draws none. For every cell,
 * `columnStart` says where its column begins and `cellColumn` which column that is; `columnsBefore[c]` is the set
 * of the cells in the columns before column `c`, and `emptyBefore[c]` how many of them the last step left empty.
 */
export const cascadeState = ({
                                 columns,
                                 rows,
                                 carried = [],
                                 refill
                             }: Pick<Tumble, 'columns' | 'rows' | 'carried'> & Partial<Pick<Tumble, 'refill'>>) => ({
    cascadeSteps: 0, survivors: ['0'], drops: ['0'], dropSymbol: '0',
    columnStart: Array.from({length: columns * rows}, (_, cell) => cell - cell % rows),
    cellColumn: Array.from({length: columns * rows}, (_, cell) => Math.floor(cell / rows)),
    columnsBefore: Array.from({length: columns + 1}, (_, column) => 2 ** (column * rows) - 1),
    emptyBefore: Array.from({length: columns + 1}, () => 0),
    ...(usesSourceTable(rows) ? {
        columnOffsets: Array.from({length: columns}, (_, column) => column * rows),
        columnRemoved: Array.from({length: columns}, () => 0),
        boardRows: Array.from({length: columns * rows}, (_, cell) => cell % rows),
        settledSources: settledSources(rows),
    } : {}),
    ...(refill && 'reels' in refill ? Object.fromEntries(refill.reels.strips.map((_, reel) => [topDrop(reel), 0])) : {}),
    ...Object.fromEntries(carried.map(({key}) => [survivorsOf(key), [0]])),
    ...Object.fromEntries(cascadeTrace.map(key => [key, []])),
});

/** One new symbol: it is drawn and waits above the board with the others. */
function dropSymbol(refill: readonly Outcome[]): Mechanic {
    return new Flow()
        .draw('symbol', 'Draw the symbol that drops in', 'dropSymbol', refill)
        .write('drop', 'It waits above the board with the others', 'drops', append('drops', state('dropSymbol')))
        .mechanic('drop-symbol', 'Draws one new symbol for an empty cell.');
}

/**
 * Removes the cells of the set `removed` from `board`: the symbols above them fall to the bottom of their column
 * and new symbols drop in from the top. New symbols come column by column from the left, and in a column the
 * first one lands lowest.
 */
export function tumble(mechanics: Mechanics, spec: Tumble): Mechanic {
    const {name = 'tumble', columns, rows, refill, carried = [], retainSurvivors = true} = spec, cells = columns * rows;
    const place = state('place');
    // A column with `empty` empty cells receives that many new symbols in its top cells; the symbols that stay fill
    // the cells below, in their order. `emptyThrough` counts the empty cells of the columns up to and including this
    // one, which is also how many new symbols those columns take. In board order the cell `place` is therefore
    // preceded by `place - emptyThrough` symbols that stay, once its column has settled.
    const lookup = usesSourceTable(rows);
    const row = lookup ? at('boardRows', place) : sub(place, at('columnStart', place)),
        column = at('cellColumn', place);
    const sourceRow = lookup ? at('settledSources', add(mul(num(rows), at('columnRemoved', column)), row)) : num(0);
    const emptyThrough = at('emptyBefore', add(column, num(1)));
    const stays = lookup ? gte(sourceRow, num(0)) : gte(row, sub(emptyThrough, at('emptyBefore', column)));
    const survivor = lookup ? add(at('columnStart', place), sourceRow) : sub(place, emptyThrough);
    const dropped = sub(sub(emptyThrough, num(1)), row);
    const remains = not(inSet(state('removed'), place));

    const flow = new Flow()
        .write('empty-cells', 'Count the empty cells in the columns before each column', 'emptyBefore',
            mapOver('columnsBefore', sizeOf(intersect(state('removed'), state('item'))), {itemType: 'Number'}));
    if (lookup) flow.write('column-removed', 'Read the removal pattern of each column', 'columnRemoved',
        mapOver('columnOffsets', intersect(moved(state('removed'), sub(num(0), state('item'))), num(2 ** rows - 1)), {itemType: 'Number'}));
    if (!lookup || retainSurvivors) {
        flow.write('survivors', 'The symbols that stay, in board order', 'survivors', filterOver('board', remains, {index: 'place'}));
        for (const {key, label} of carried)
            flow.write(`survivors-${key}`, `The ${label} that stay with them`, survivorsOf(key), filterOver(key, remains, {
                index: 'place',
                itemType: 'Number'
            }));
    }
    let fresh: Expr;
    if ('cells' in refill) {
        // Never more than every cell of the board, and one pass of slack so that the limit is never what ends the loop.
        flow.write('empty', 'No new symbol has dropped yet', 'drops', emptyArray())
            .repeat('refill', 'Draw a new symbol for every cell left empty', cells + 1, mechanics.add(dropSymbol(refill.cells)),
                {until: gte(length('drops'), sizeOf(state('removed'))), bodyLabel: 'One new symbol'});
        fresh = at('drops', lookup ? add(emptyThrough, sourceRow) : dropped);
    } else {
        // A reel with empty cells stops again, and the symbols from the stop on fill them: the first one lands lowest,
        // so the top cell takes the symbol one place short of as many places below the stop as the reel has empty cells.
        const {reels} = refill;
        reels.strips.forEach((_, reel) => {
            const empty = sub(at('emptyBefore', num(reel + 1)), at('emptyBefore', num(reel)));
            flow.either(`reel-${reel}`, `Does reel ${reel + 1} have empty cells?`, gt(empty, num(0)), path => path
                .use(`stop-${reel}`, `Reel ${reel + 1} stops for its new symbols`, mechanics.add(reelStop(reels, reel)))
                .write(`top-${reel}`, 'How far below the stop the symbol for the top cell is', topDrop(reel), sub(empty, num(1))));
        });
        fresh = choose(column, reels.strips.map((_, reel) => reelSymbol(reels, reel, sub(state(topDrop(reel)), row))));
    }

    flow.write('settle', 'The symbols that stay fall to the bottom of their column; the new ones fill it from the top', 'board',
        mapOver('board', when(stays, at(lookup ? 'board' : 'survivors', survivor), fresh), {index: 'place'}));
    for (const {key, label, fresh, stationary} of carried)
        if (!stationary) flow.write(`settle-${key}`, `The ${label} fall with their symbols`, key,
            mapOver(key, when(stays, at(lookup ? key : survivorsOf(key), survivor), num(fresh)), {
                index: 'place',
                itemType: 'Number'
            }));
    return flow.mechanic(name, 'Removes the marked symbols: the rest fall down their columns and new symbols drop in from the top.');
}

/**
 * A cascade on `board`: while the board pays, the step is paid, its symbols are removed and the board is filled
 * again. It ends when a board pays nothing, or after `maxSteps` steps that paid.
 */
export function cascade(mechanics: Mechanics, {
    name = 'cascade',
    maxSteps,
    pays,
    onStep,
    tumble: fall
}: Cascade): Mechanic {
    const step = mechanics.add(new Flow()
        .use('pay', onStep.label, onStep.mechanic)
        .record('trace-board', 'Remember the board that paid', 'cascadeBoards', state('board'))
        .record('trace-removed', 'Remember the cells removed', 'cascadeRemoved', mapOver('board', when(inSet(state('removed'), state('place')), num(1), num(0)), {index: 'place'}))
        .use('tumble', fall.label, fall.mechanic)
        .record('trace-after', 'Remember the board after the fall', 'cascadeAfter', state('board'))
        .write('count', 'Count the step', 'cascadeSteps', add(state('cascadeSteps'), num(1)))
        // After its last step a cascade is over whatever the new board shows, so that board is not looked at.
        .onlyIf('pays', `${pays.label}, unless the cascade has used up its steps`, lt(state('cascadeSteps'), num(maxSteps)), pays.mechanic)
        .mechanic(`${name}-step`, 'One step of a cascade: the board that paid is paid out, its symbols are removed, and the new board is looked at.'));
    return new Flow()
        .write('fresh', 'No step has paid yet', 'cascadeSteps', num(0))
        .use('pays', pays.label, pays.mechanic)
        // The last pass allowed is the one that reaches `maxSteps`; the loop has room for one more, never taken.
        .repeat('step', `Cascade while the board pays, for at most ${maxSteps} steps`, maxSteps + 1, step,
            {
                until: or(eq(state(pays.coins), num(0)), gte(state('cascadeSteps'), num(maxSteps))),
                bodyLabel: 'One step'
            })
        .mechanic(name, `Cascade: every board that pays is paid, its symbols are removed and new ones drop in, until a board pays nothing or ${maxSteps} steps have paid.`);
}
