import { add, and, at, bool, div, eq, floor, gt, gte, mapOver, mul, num, or, param, state, toNumber, when } from '../expr';
import { Flow, Mechanics, type Mechanic } from '../flow';
import { recordHoldEvent } from './hold-and-spin';

/** One thing a pull of the boost can do to every coin of the column: add stakes, multiply, and end the boost or not. */
export interface BoostKind { kind: string; weight: number; add_bets: number; times: number; ends: boolean }

export interface ColumnBoost {
  columns: number;
  rows: number;
  /** The most pulls one column gets. */
  maxPulls: number;
  kinds: readonly BoostKind[];
}

/**
 * State the column-boost subgraphs read and write. The four `boost…` lists describe the kinds of pull, in the
 * order they are drawn by; `boostedColumns[column]` is 1 once that column has had its boost.
 */
export const columnBoostState = ({ columns, kinds }: ColumnBoost) => ({
  boostNames: kinds.map(kind => kind.kind), boostAdds: kinds.map(kind => kind.add_bets), boostTimes: kinds.map(kind => kind.times),
  boostEnds: kinds.map(kind => kind.ends ? 1 : 0), boostedColumns: Array.from({ length: columns }, () => 0),
  boostColumn: 0, boostKind: '0', boostPulls: 0, boostDone: false,
});

const numbered = { index: 'place', itemType: 'Number' } as const;

/** One pull of the boost on the column in `boostColumn`. */
function boostPull({ rows, maxPulls, kinds }: ColumnBoost): Mechanic {
  const kind = toNumber(state('boostKind'));
  const inColumn = eq(floor(div(state('place'), num(rows))), state('boostColumn'));
  const flow = new Flow()
    .draw('kind', 'Pull: which boost comes up', 'boostKind', kinds.map(({ weight }, index) => [index, weight] as const))
    .write('coins', 'Every coin of the column: (value + added stakes) × multiplier', 'coins',
      mapOver('coins', when(inColumn, mul(add(state('item'), mul(at('boostAdds', kind), state('coinStake'))), at('boostTimes', kind)), state('item')), numbered))
    .write('pulls', 'Count the pull', 'boostPulls', add(state('boostPulls'), num(1)))
    .write('done', `The boost ends on a kind that ends it, or after ${maxPulls} pulls`, 'boostDone',
      or(eq(at('boostEnds', kind), num(1)), gte(state('boostPulls'), num(maxPulls))));
  return recordHoldEvent(flow, 'trace', at('boostNames', kind), state('boostColumn'))
    .mechanic('boost-pull', 'One pull of a column boost: the kind drawn adds to or multiplies every coin of the column, and may end the boost.');
}

/** The boost of one full column. Parameter: `column`. Pulls until a pull ends the boost. */
function boostColumn(mechanics: Mechanics, spec: ColumnBoost): Mechanic {
  return new Flow()
    .write('boosting', 'This column is boosted now', 'boostColumn', param('column'))
    .write('mark', 'A column is boosted only once', 'boostedColumns',
      mapOver('boostedColumns', when(eq(state('place'), param('column')), num(1), state('item')), numbered))
    .write('start', 'No pull has been made yet', 'boostPulls', num(0))
    .write('open', 'The boost is running', 'boostDone', bool(false))
    // The pulls end the boost themselves, by `maxPulls` at the latest; the loop is given one more for the same
    // reason as every loop here: reaching its limit would then mean a fault.
    .repeat('pull', 'Pull until the boost ends', spec.maxPulls + 1, mechanics.add(boostPull(spec)), { until: state('boostDone'), bodyLabel: 'Pull' })
    .mechanic('boost-column', `Boosts one full column: up to ${spec.maxPulls} pulls, each changing every coin of the column, until a pull ends it.`);
}

/**
 * Boosts every column that is full of coins and has not been boosted yet, left to right. Placed after coins have
 * arrived, it gives each column its boost at the moment it fills.
 */
export function columnBoosts(mechanics: Mechanics, spec: ColumnBoost): Mechanic {
  const boost = mechanics.add(boostColumn(mechanics, spec));
  const flow = new Flow();
  for (let column = 0; column < spec.columns; column++) {
    const full = and(...Array.from({ length: spec.rows }, (_, row) => gt(at('coins', num(column * spec.rows + row)), num(0))));
    flow.onlyIf(`column-${column + 1}`, `Column ${column + 1} is full and not boosted yet: boost it`,
      and(eq(at('boostedColumns', num(column)), num(0)), full), boost, { column });
  }
  return flow.mechanic('column-boosts', 'Every column that has just filled with coins gets its boost, left to right.');
}
