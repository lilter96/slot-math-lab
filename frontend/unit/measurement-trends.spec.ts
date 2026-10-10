import { test, expect } from '@playwright/test';
import { availableReducers, chartReducer, definition, newMetric, samePlan, type MeasurementSnapshot } from '../src/lib/measurements/model';
import { defaultOptions } from '../src/lib/measurements/analysis';
import { chartMeasurements, restoreTrendPoints, scalarMeasurements, trendStatistic } from '../src/lib/measurements/trends';
import { serializeCheckpoint } from '../src/lib/measurements/checkpoints';
import { completed } from './realtime-fixtures';

// Independent examples: two included children valued 2 and 4, a third child
// excluded. Those two children share one paid parent and one owning episode.
const value = { id: 'sticky', observations: 3, count: 2, excluded: 1, errors: 0, min: 2, max: 4, mean: 3, sum: 6, stdDev: Math.SQRT2, firstError: null,
  analysis: { moments: { secondMoment: 10, populationVariance: 1, sampleVariance: 2 },
    parentExposure: { paidRoundsWithMatchingChildren: 1, episodesWithMatchingChildren: 1 }, entries: 1, exits: 1, unclosedEpisodes: 0,
    bins: ['large'.repeat(10000)], support: ['large'.repeat(10000)] } } as unknown as MeasurementSnapshot;

test('Chart history preserves every defined scalar without retaining rich distributions', () => {
  const trend = chartMeasurements([value])![0];
  expect(trendStatistic(trend, 'max')).toBe(4); expect(trendStatistic(trend, 'secondMoment')).toBe(10);
  expect(trendStatistic(trend, 'sampleVariance')).toBe(2); expect(trendStatistic(trend, 'matchRate')).toBe(2 / 3);
  expect(trendStatistic(trend, 'distinctParents')).toBe(1); expect(trendStatistic(trend, 'matchingEpisodes')).toBe(1);
  expect(trendStatistic(trend, 'accountingResidual')).toBe(0); expect(trendStatistic(trend, 'correlation')).toBeNull();
  expect(trend).not.toHaveProperty('analysis'); expect(JSON.stringify(trend).length).toBeLessThan(2000);
  expect(chartMeasurements([trend])).toEqual([trend]);
  const legacy = scalarMeasurements([value])![0]; expect(trendStatistic(legacy, 'max')).toBe(4); expect(trendStatistic(legacy, 'sampleVariance')).toBeNull();
});
test('Undefined statistics and real zero counts remain distinct after bounded checkpoint serialization', () => {
  const empty = { ...value, observations: 3, count: 0, excluded: 3, min: null, max: null, mean: null, sum: null, stdDev: null, analysis: undefined };
  const run = completed(); const point = { n: 1, rtp: 0, stdErr: 0, elapsedMs: 1, rate: 1, measurements: chartMeasurements([empty]) };
  const restored = JSON.parse(serializeCheckpoint({ run, progress: run.progress!, history: [], points: [point] }));
  const trend = restoreTrendPoints(restored.points)[0].measurements![0];
  expect(trendStatistic(trend, 'count')).toBe(0); expect(trendStatistic(trend, 'matchRate')).toBe(0);
  expect(trendStatistic(trend, 'mean')).toBeNull(); expect(trendStatistic(trend, 'sampleVariance')).toBeNull();
  expect(trendStatistic({ ...trend, statistics: { count: 0, mean: null } }, 'mean')).toBeNull();
});
test('Presentation changes never change the pinned plan and unsupported chart choices have a safe fallback', () => {
  const metric = { ...newMetric('sticky'), name: 'Sticky payout', options: defaultOptions() };
  expect(samePlan([definition(metric)], [definition({ ...metric, reducers: ['count'], chartStatistic: 'max', chart: false })])).toBe(true);
  expect(chartReducer({ ...metric, chartStatistic: 'sampleVariance' })).toBe('sampleVariance');
  expect(chartReducer({ chartStatistic: 'matchingEpisodes', options: defaultOptions() })).toBe('mean');
  expect(chartReducer({ chartStatistic: 'sampleVariance' })).toBe('mean');
  expect(availableReducers(metric)).not.toContain('correlation'); expect(availableReducers(metric)).not.toContain('assertionViolations');
  expect(availableReducers({ options: { ...defaultOptions(), pairRole: 'wager' } })).toContain('ratio');
  expect(availableReducers({ options: { ...defaultOptions(), subject: 'episode' } })).toContain('matchingEpisodes');
});
test('Corrupted, duplicate and oversized recovery histories cannot introduce chart values', () => {
  const point = { n: 1, rtp: 1, stdErr: 0, elapsedMs: 1, rate: 1, measurements: chartMeasurements([value]) };
  const invalid = { ...point, n: 2, measurements: [{ ...point.measurements![0], statistics: { mean: Infinity } }] };
  expect(restoreTrendPoints([point, invalid, point, { ...point, n: 3, measurements: [null] }])).toHaveLength(1);
  expect(restoreTrendPoints([{ ...point, measurements: [{ ...point.measurements![0], statistics: { fakeStatistic: 1 } }] }])).toEqual([]);
  expect(restoreTrendPoints([{ ...point, measurements: [{ ...point.measurements![0], statistics: { mean: 4 } }] }])).toEqual([]);
  expect(restoreTrendPoints([{ ...point, measurements: [{ ...point.measurements![0], count: 0, excluded: 3 }] }])).toEqual([]);
  const history = restoreTrendPoints(Array.from({ length: 10000 }, (_, i) => ({ ...point, n: i + 1 })));
  expect(history).toHaveLength(600); expect(history.at(-1)!.n).toBe(10000);
});
