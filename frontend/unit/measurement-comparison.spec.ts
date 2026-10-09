import { test, expect } from '@playwright/test';
import { compareMeasurements } from '../src/lib/measurements/comparison';
import { defaultOptions, type MeasurementAnalysis } from '../src/lib/measurements/analysis';
import type { MeasurementSnapshot } from '../src/lib/measurements/model';
import { completed } from './realtime-fixtures';
function run(id: string, upper: number, hits: number, seed: number) {
  const run = completed(); run.id = id; run.seed = seed;
  run.measurements = [{ id: 'law', name: 'Payout law', nodeId: null, value: null, filter: null, unit: 'payout', options: { ...defaultOptions(), independentSubjects: true } }];
  const variance = ((100 - hits) * .98 ** 2 + hits * (upper - .98) ** 2) / 99;
  const metric: MeasurementSnapshot = { id: 'law', count: 100, observations: 100, excluded: 0, errors: 0, min: 0, max: upper, mean: .98, sum: 98, stdDev: Math.sqrt(variance), firstError: null,
    analysis: { supportComplete: true, moments: { sampleVariance: variance }, support: [{ value: 0, count: 100 - hits, sum: 0 }, { value: upper, count: hits, sum: 98 }] } as unknown as MeasurementAnalysis };
  run.progress!.measurements = [metric]; return run;
}
test('Equal-RTP laws retain their full-distribution effects and separate uncertainty', () => {
  const a = run('a', 1.96, 50, 42), b = run('b', 9.8, 10, 43), result = compareMeasurements(a, b)[0];
  expect(result.meanDelta).toBe(0); expect(result.totalVariation).toBeCloseTo(.5, 14); expect(result.cdfDistance).toBeCloseTo(.4, 14);
  expect(result.varianceDelta).toBeLessThan(0); expect(result.meanDifferenceInterval![0]).toBeLessThan(0); expect(result.meanDifferenceInterval![1]).toBeGreaterThan(0);
  b.seed = a.seed; expect(compareMeasurements(a, b)[0].meanDifferenceInterval).toBeNull();
});
test('Changed scope, units, invalid subjects and retained state cannot receive a difference interval', () => {
  const a = run('a', 1.96, 50, 42), b = run('b', 9.8, 10, 43);
  b.measurements![0].unit = 'coins'; const incompatible = compareMeasurements(a, b)[0]; expect(incompatible.meanDelta).toBeNull(); expect(incompatible.totalVariation).toBeNull();
  b.measurements![0].unit = 'payout'; b.progress!.measurements![0].errors = 1; expect(compareMeasurements(a, b)[0].meanDifferenceInterval).toBeNull();
  b.progress!.measurements![0].errors = 0; b.execution = { regime: 'persistent', persistentKeys: ['meter'], sessionLength: 100, initialBankroll: 100, wager: 1 };
  expect(compareMeasurements(a, b)[0].meanDifferenceInterval).toBeNull();
});
