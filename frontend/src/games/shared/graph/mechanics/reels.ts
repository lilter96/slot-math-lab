import { add, arrayOf, at, choose, eq, itemAt, mapOver, num, state, sub, toNumber, type Expr } from '../expr';
import { Flow, type Mechanic, type Mechanics } from '../flow';

export interface ReelSet {
  /** Subgraph name, e.g. `spin-reels`. */
  name: string;
  /** State-key prefix of the strips: reel 2 is stored under `${stripKey}2`. */
  stripKey: string;
  /** One strip of symbol ids per reel, top to bottom. */
  strips: readonly (readonly (string | number)[])[];
  rows: number;
}

/** A graph file nests at most 64 levels, and a window listed cell by cell nests one level per cell. */
const LISTED_WINDOW = 20;
const cellsOf = (reels: ReelSet) => reels.strips.length * reels.rows;
const windowReels = (reels: ReelSet) => `windowReels${reels.strips.length}x${reels.rows}`;

/**
 * The reel strips as state data. Each strip runs on into its own start for `rows - 1` more symbols, so a window
 * never wraps. A large window also keeps, for every cell, the reel it belongs to: it is read by that.
 */
export function reelState(reels: ReelSet): Record<string, (string | number)[]> {
  const { stripKey, strips, rows } = reels;
  return {
    ...Object.fromEntries(strips.map((strip, reel) =>
      [`${stripKey}${reel}`, Array.from({ length: strip.length + rows - 1 }, (_, place) => String(strip[place % strip.length]))])),
    ...(cellsOf(reels) > LISTED_WINDOW ? { [windowReels(reels)]: Array.from({ length: cellsOf(reels) }, (_, cell) => Math.floor(cell / rows)) } : {}),
  };
}

/** The symbol `below` places below the stop of a reel. */
export const reelSymbol = (reels: ReelSet, reel: number, below: Expr) => at(`${reels.stripKey}${reel}`, add(toNumber(state(`stop${reel}`)), below));

/** Adds the step that reads the window off the strips into `board`, at the drawn stops. */
function readWindow(flow: Flow, reels: ReelSet): Flow {
  const { strips, rows } = reels;
  return flow.write('window', 'Symbols showing in the window', 'board', cellsOf(reels) <= LISTED_WINDOW
    ? arrayOf(strips.flatMap((_, reel) => Array.from({ length: rows }, (_, row) => reelSymbol(reels, reel, num(row)))))
    // Every cell reads its own reel, as many places below the stop as the cell lies below the top of the reel.
    : mapOver(windowReels(reels), choose(state('item'), strips.map((_, reel) => reelSymbol(reels, reel, sub(state('place'), num(reel * rows))))),
      { index: 'place', itemType: 'Number' }));
}

/** One reel of the set stops at a uniformly drawn position, kept in `stop<reel>`: the subgraph `paid-reels-stop-3`. */
export function reelStop(reels: ReelSet, reel: number): Mechanic {
  return new Flow()
    .draw('stop', `Reel ${reel + 1} stops`, `stop${reel}`, reels.strips[reel].map((_, stop) => [stop, 1] as const))
    .mechanic(`${reels.name}-stop-${reel + 1}`, `Reel ${reel + 1} stops at a random position of its strip.`);
}

/**
 * Every reel stops at a uniformly drawn position; the symbols showing in the window become `board`.
 * Board cell `reel * rows + row` is the symbol `row` places below the stop on that reel. With `mechanics`, the
 * stop of every reel is a subgraph of its own, shared with whatever else stops the same reels, such as a tumble
 * that fills the board from them.
 */
export function spinReels(reels: ReelSet, mechanics?: Mechanics): Mechanic {
  const flow = new Flow();
  reels.strips.forEach((strip, reel) => mechanics
    ? flow.use(`stop-${reel}`, `Reel ${reel + 1} stops`, mechanics.add(reelStop(reels, reel)))
    : flow.draw(`stop-${reel}`, `Reel ${reel + 1} stops`, `stop${reel}`, strip.map((_, stop) => [stop, 1] as const)));
  readWindow(flow, reels);
  return flow.mechanic(reels.name, `Stops each of the ${reels.strips.length} reels at a random position and reads the ${reels.rows} symbols showing on each into the board.`);
}

export interface Showing {
  /** Subgraph name, e.g. `spin-reels-with-scatters`. */
  name: string;
  symbol: string | number;
  /** What the symbol is called in labels, e.g. `COIN`. */
  symbolName: string;
  /** The fewest of the symbol the window must show. */
  atLeast: number;
}

/**
 * How the stops of a reel set split when the window has to show at least `atLeast` of one symbol.
 * `stops[reel][count]` lists the stops of a reel that show `count` of the symbol; `patterns` lists every way to
 * spread enough of them over the reels, weighted by how many stop combinations give that spread.
 */
export function showingStops(reels: ReelSet, { symbol, atLeast }: Pick<Showing, 'symbol' | 'atLeast'>) {
  const stops = reels.strips.map(strip => {
    const byCount: number[][] = Array.from({ length: reels.rows + 1 }, () => []);
    strip.forEach((_, stop) => byCount[Array.from({ length: reels.rows }, (_, row) => strip[(stop + row) % strip.length])
      .filter(shown => String(shown) === String(symbol)).length].push(stop));
    return byCount;
  });
  const patterns: { counts: number[]; weight: number }[] = [];
  const spread = (counts: number[], weight: number) => {
    if (counts.length < stops.length) stops[counts.length].forEach((group, count) => { if (group.length) spread([...counts, count], weight * group.length); });
    else if (counts.reduce((sum, count) => sum + count, 0) >= atLeast) patterns.push({ counts, weight });
  };
  spread([], 1);
  if (!patterns.length) throw new Error(`No reel stops show ${atLeast} of symbol ${symbol}`);
  return { stops, patterns };
}

/** State the `spinReelsShowing` subgraph reads: one row per pattern, how many of the symbol each reel shows. */
export const showingState = (reels: ReelSet, showing: Showing) =>
  ({ showPatterns: showingStops(reels, showing).patterns.map(pattern => pattern.counts), showPattern: '0' });

/**
 * The reels stopped the way an ordinary spin stops them, given that the window shows at least `atLeast` of one
 * symbol. First it is drawn how many of the symbol each reel shows; then every reel stops, with equal chances, at
 * one of its positions that show that many. The result has exactly the chances of spinning until enough appear.
 */
export function spinReelsShowing(reels: ReelSet, showing: Showing): Mechanic {
  const { stops, patterns } = showingStops(reels, showing);
  const flow = new Flow()
    .draw('pattern', `How many ${showing.symbolName}s each reel shows`, 'showPattern', patterns.map(({ weight }, index) => [index, weight] as const));
  stops.forEach((groups, reel) => {
    const shown = itemAt(at('showPatterns', toNumber(state('showPattern'))), num(reel));
    const counts = groups.flatMap((group, count) => group.length ? [count] : []);
    const stopAt = (path: Flow, remaining: number[]) => {
      const count = remaining[0];
      const draw = (to: Flow) => to.draw(`stop-${reel}-showing-${count}`, `Reel ${reel + 1} stops showing ${count}`, `stop${reel}`, groups[count].map(stop => [stop, 1] as const));
      if (remaining.length === 1) draw(path);
      else path.either(`reel-${reel}-shows-${count}`, `Does reel ${reel + 1} show ${count}?`, eq(shown, num(count)), draw, rest => stopAt(rest, remaining.slice(1)));
    };
    stopAt(flow, counts);
  });
  readWindow(flow, reels);
  return flow.mechanic(showing.name, `Stops the ${reels.strips.length} reels so that at least ${showing.atLeast} ${showing.symbolName}s show, with the chances an ordinary spin gives those stops.`);
}
