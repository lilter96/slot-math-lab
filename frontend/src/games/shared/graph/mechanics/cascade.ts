import { add, and, append, appended, at, div, emptyArray, eq, filterOver, floor, foldOver, gt, gte, itemAt, length, lt, mapOver, mul, num, or, state, sub, toNumber, when } from '../expr';
import { Flow, Mechanics, type Mechanic, type Outcome } from '../flow';
import { reelStop, type ReelSet } from './reels';

/** Per-cell state that moves with the symbols when they fall, for example which cells are held. */
export interface Carried {
  /** State key of the array, one number per cell. */
  key: string;
  /** What the array is, for labels: `held cells`. */
  label: string;
  /** The value a cell gets when a new symbol drops into it. */
  fresh: number;
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
}

export interface Cascade {
  /** Subgraph name; a game with several kinds of cascade names each. */
  name?: string;
  /** The most steps of one cascade that pay. */
  maxSteps: number;
  /** Subgraph that pays the board: leaves the pay in the state key `coins` and marks the cells to remove in `removed`. */
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

/**
 * State the cascade subgraphs read and write. `cascadeSteps` counts the steps of the running cascade that paid,
 * and `drops` are the symbols that dropped in last, column by column, the lowest of a column first.
 */
export const cascadeState = ({ carried = [] }: Partial<Pick<Tumble, 'carried'>> = {}) => ({
  cascadeSteps: 0, survivors: ['0'], keptThrough: [0], drops: ['0'], dropSymbol: '0', dropChunk: ['0'], fromZero: [0],
  ...Object.fromEntries(carried.map(({ key }) => [survivorsOf(key), [0]])),
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
 * Removes the cells marked in `removed` from `board`: the symbols above them fall to the bottom of their column
 * and new symbols drop in from the top. New symbols come column by column from the left, and in a column the
 * first one lands lowest.
 */
export function tumble(mechanics: Mechanics, spec: Tumble): Mechanic {
  const { name = 'tumble', columns, rows, refill, carried = [] } = spec, cells = columns * rows;
  const place = state('place');
  // The column of a cell runs from the cell `first` to just before the cell `next`. `keptThrough[k]` counts the
  // symbols that stay among the first k cells of the board, so the symbols that stay in the columns up to and
  // including this one are `kept`, and the cell receives one of them unless it lies above them all.
  const first = mul(floor(div(place, num(rows))), num(rows)), next = add(first, num(rows));
  const kept = at('keptThrough', next);
  const survivor = sub(kept, sub(next, place));
  const stays = gte(survivor, at('keptThrough', first));
  const dropped = sub(sub(next, kept), add(sub(place, first), num(1)));
  const remains = eq(at('removed', place), num(0));

  const flow = new Flow()
    .write('survivors', 'The symbols that stay, in board order', 'survivors', filterOver('board', remains, { index: 'place' }));
  for (const { key, label } of carried)
    flow.write(`survivors-${key}`, `The ${label} that stay with them`, survivorsOf(key), filterOver(key, remains, { index: 'place', itemType: 'Number' }));
  flow
    .write('count', 'Count the symbols that stay, cell by cell', 'keptThrough',
      foldOver('removed', state('fromZero'), appended(state('acc'), add(itemAt(state('acc'), place), sub(num(1), state('item')))), { index: 'place', itemType: 'Number' }))
    .write('empty', 'No new symbol has dropped yet', 'drops', emptyArray());

  if ('cells' in refill)
    // Never more than every cell of the board, and one pass of slack so that the limit is never what ends the loop.
    flow.repeat('refill', 'Draw a new symbol for every cell left empty', cells + 1, mechanics.add(dropSymbol(refill.cells)),
      { until: gte(add(length('drops'), length('survivors')), num(cells)), bodyLabel: 'One new symbol' });
  else
    refill.reels.strips.forEach((_, column) => {
      const empty = sub(num(rows), sub(at('keptThrough', num((column + 1) * rows)), at('keptThrough', num(column * rows))));
      const stop = toNumber(state(`stop${column}`));
      flow.either(`reel-${column}`, `Does reel ${column + 1} have empty cells?`, gt(empty, num(0)), path => path
        .use(`stop-${column}`, `Reel ${column + 1} stops for its new symbols`, mechanics.add(reelStop(refill.reels, column)))
        .write(`chunk-${column}`, 'The symbols from the stop on, one for every empty cell', 'dropChunk',
          filterOver(`${refill.reels.stripKey}${column}`, and(gte(place, stop), lt(place, add(stop, empty))), { index: 'place' }))
        .write(`drops-${column}`, 'They wait above the board with the others', 'drops',
          foldOver('dropChunk', state('drops'), appended(state('acc'), state('item')))));
    });

  flow.write('settle', 'The symbols that stay fall to the bottom of their column; the new ones fill it from the top', 'board',
    mapOver('board', when(stays, at('survivors', survivor), at('drops', dropped)), { index: 'place' }));
  for (const { key, label, fresh } of carried)
    flow.write(`settle-${key}`, `The ${label} fall with their symbols`, key,
      mapOver(key, when(stays, at(survivorsOf(key), survivor), num(fresh)), { index: 'place', itemType: 'Number' }));
  return flow.mechanic(name, 'Removes the marked symbols: the rest fall down their columns and new symbols drop in from the top.');
}

/**
 * A cascade on `board`: while the board pays, the step is paid, its symbols are removed and the board is filled
 * again. It ends when a board pays nothing, or after `maxSteps` steps that paid.
 */
export function cascade(mechanics: Mechanics, { name = 'cascade', maxSteps, pays, onStep, tumble: fall }: Cascade): Mechanic {
  const step = mechanics.add(new Flow()
    .use('pay', onStep.label, onStep.mechanic)
    .record('trace-board', 'Remember the board that paid', 'cascadeBoards', state('board'))
    .record('trace-removed', 'Remember the cells removed', 'cascadeRemoved', state('removed'))
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
      { until: or(eq(state(pays.coins), num(0)), gte(state('cascadeSteps'), num(maxSteps))), bodyLabel: 'One step' })
    .mechanic(name, `Cascade: every board that pays is paid, its symbols are removed and new ones drop in, until a board pays nothing or ${maxSteps} steps have paid.`);
}
