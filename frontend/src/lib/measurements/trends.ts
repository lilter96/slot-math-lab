import { reducers, statistic, type MeasurementSnapshot, type Reducer } from './model';

/** Browser-only chart history. No distributions, witnesses, groups or CI objects
 * are copied into each point. These values never substitute for server evidence. */
export interface MeasurementTrend extends Omit<MeasurementSnapshot, 'analysis' | 'witnesses'> {
  statistics?: Partial<Record<Reducer, number | null>>;
}
export interface MeasurementTrendPoint { measurements?: MeasurementTrend[]; n: number; rtp: number; stdErr: number; elapsedMs: number; rate: number }

/** Local storage is a recovery hint, not a trusted source. Bound and validate
 * charts separately from the run snapshot that HTTP will verify. */
export function restoreTrendPoints(input: unknown): MeasurementTrendPoint[] {
  if (!Array.isArray(input)) return [];
  const result: MeasurementTrendPoint[] = [];
  for (const point of input.slice(-600)) {
    if (!point || !Number.isSafeInteger(point.n) || point.n <= 0 || result.length > 0 && result.at(-1)!.n >= point.n
      || !['rtp', 'stdErr', 'elapsedMs', 'rate'].every(key => typeof point[key] === 'number' && Number.isFinite(point[key]) && point[key] >= 0)
      || point.measurements !== undefined && (!Array.isArray(point.measurements) || point.measurements.length > 32)) continue;
    const ids = new Set<string>();
    if (point.measurements?.some((value: MeasurementTrend) => {
      if (!value || typeof value.id !== 'string' || value.id.length > 128 || ids.has(value.id)) return true;
      ids.add(value.id);
      if (!['observations', 'count', 'excluded', 'errors'].every(key => Number.isSafeInteger(value[key as 'count']) && value[key as 'count'] >= 0)
        || value.count + value.excluded + value.errors !== value.observations) return true;
      for (const key of ['min', 'max', 'mean', 'sum', 'stdDev'] as const) {
        const number = value[key];
        if (key === 'stdDev' ? value.count < 2 ? number !== null : typeof number !== 'number' || !Number.isFinite(number) || number < 0
          : value.count === 0 ? number !== null : typeof number !== 'number' || !Number.isFinite(number)) return true;
      }
      if (value.count > 0 && (value.min! > value.max! || value.mean! < value.min! - 1e-9 || value.mean! > value.max! + 1e-9)) return true;
      if (value.firstError != null && (typeof value.firstError !== 'string' || value.firstError.length > 1024)) return true;
      if (value.statistics != null && (typeof value.statistics !== 'object' || Array.isArray(value.statistics)
        || Object.entries(value.statistics).some(([key, number]) => !reducers.includes(key as Reducer) || number !== null && (typeof number !== 'number' || !Number.isFinite(number))))) return true;
      return value.statistics != null && (['min', 'max', 'mean', 'sum', 'stdDev', 'count', 'eligible', 'excluded', 'invalid', 'matchRate', 'accountingResidual'] as const)
        .some(reducer => Object.hasOwn(value.statistics!, reducer) && value.statistics![reducer] !== statistic(value, reducer));
    })) continue;
    result.push({ n: point.n, rtp: point.rtp, stdErr: point.stdErr, elapsedMs: point.elapsedMs, rate: point.rate,
      measurements: chartMeasurements(point.measurements) });
  }
  return result;
}

export function scalarMeasurements(values?: MeasurementSnapshot[]): MeasurementTrend[] | undefined {
  return values?.map(m => ({ id: m.id, observations: m.observations, count: m.count, excluded: m.excluded, errors: m.errors,
    min: m.min, max: m.max, mean: m.mean, sum: m.sum, stdDev: m.stdDev, firstError: m.firstError }));
}

export function chartMeasurements(values?: (MeasurementSnapshot | MeasurementTrend)[]): MeasurementTrend[] | undefined {
  return values?.map(value => ({ ...scalarMeasurements([value])![0], statistics: Object.fromEntries(reducers.map(reducer => {
    // Repacking an already compact checkpoint is idempotent. An explicit null
    // remains unavailable; it must not become a zero or borrow another statistic.
    const number = 'statistics' in value && value.statistics && Object.hasOwn(value.statistics, reducer)
      ? value.statistics[reducer] : statistic(value, reducer);
    return [reducer, typeof number === 'number' && Number.isFinite(number) ? number : null];
  })) }));
}

export function trendStatistic(value: MeasurementTrend | undefined, reducer: Reducer): number | null {
  if (!value) return null;
  const number = value.statistics && Object.hasOwn(value.statistics, reducer) ? value.statistics[reducer] : statistic(value, reducer);
  return typeof number === 'number' && Number.isFinite(number) ? number : null;
}
