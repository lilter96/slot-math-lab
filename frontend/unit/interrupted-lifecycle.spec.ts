import { test, expect } from '@playwright/test';
import { validAnalysis } from '../src/lib/measurements/validation';
import type { MeasurementAnalysis } from '../src/lib/measurements/analysis';
import { availableReducers, formatStatistic, statistic, type MeasurementSnapshot } from '../src/lib/measurements/model';
import { validProgress, progressDecision } from '../src/lib/realtime/runProtocol';
import { progress, snapshot } from './realtime-fixtures';

const empty = { interruptedRounds: 0, entries: 0, exits: 0, openInstances: 0, complete: true };
const analysis: MeasurementAnalysis = {
  count: 0, min: null, max: null, mean: null, sum: null, subject: 'episode', reduction: 'sum',
  distinctParents: 0, entries: 0, exits: 0, unclosedEpisodes: 0, uniqueAwards: 0, duplicateAwards: 0,
  interruptedLifecycle: { cancelled: empty, failed: { interruptedRounds: 1, entries: 2, exits: 1, openInstances: 1, complete: true } },
  moments: { secondMoment: null, populationVariance: null, sampleVariance: null, coefficientOfVariation: null, skewness: null, excessKurtosis: null, meanAbsoluteDeviation: null },
  pair: null, support: [], supportComplete: true, bins: [{ lower: null, upper: null, count: 0, sum: 0, sumSquares: 0 }], quantiles: [], tails: [], upperTails: [],
  meanInterval: null, probabilityInterval: null, sequentialMeanInterval: null, meanStandardError: null, requiredSampleSize: null,
  weights: null, sequence: null, comparison: null, transitions: [], transitionsComplete: true, checks: [], groups: {},
};
const metric: MeasurementSnapshot = { id: 'feature', observations: 0, count: 0, excluded: 0, errors: 0, min: null, max: null, mean: null, sum: null, stdDev: null, firstError: null, analysis };

test('Interrupted boundaries have their own count population; legacy or incomplete coverage withholds a scalar', () => {
  expect(validAnalysis(analysis)).toBe(true); expect(statistic(metric, 'interruptedFeatureEntries')).toBe(2);
  expect(statistic(metric, 'interruptedFeatureExits')).toBe(1); expect(statistic(metric, 'interruptedOpenEpisodes')).toBe(1);
  expect(statistic(metric, 'featureEntries')).toBe(0); expect(statistic(metric, 'mean')).toBeNull();
  expect(formatStatistic(2, 'interruptedFeatureEntries', 'coins')).toBe('2');
  expect(statistic({ ...metric, analysis: { ...analysis, interruptedLifecycle: undefined } }, 'interruptedFeatureEntries')).toBeNull();
  expect(statistic({ ...metric, analysis: { ...analysis, interruptedLifecycle: { cancelled: empty, failed: { ...analysis.interruptedLifecycle!.failed, complete: false } } } }, 'interruptedFeatureEntries')).toBeNull();
  expect(availableReducers({}).includes('interruptedOpenEpisodes')).toBe(false);
});
test('Corrupt lifecycle coverage, reason counts and non-lifecycle populations are rejected', () => {
  for (const failed of [{ ...empty, entries: 1 }, { ...analysis.interruptedLifecycle!.failed, openInstances: 0 }, { ...analysis.interruptedLifecycle!.failed, interruptedRounds: -1 }, { ...analysis.interruptedLifecycle!.failed, complete: 'yes' }])
    expect(validAnalysis({ ...analysis, interruptedLifecycle: { cancelled: empty, failed } })).toBe(false);
  expect(validAnalysis({ ...analysis, subject: 'observation' })).toBe(false);
  expect(validAnalysis({ ...analysis, interruptedLifecycle: undefined })).toBe(true);
  expect(validAnalysis({ ...analysis, interruptedLifecycle: { cancelled: empty, failed: empty } })).toBe(true);
  expect(validAnalysis({ ...analysis, groups: { impossible: { ...analysis, interruptedLifecycle: { cancelled: empty, failed: { ...analysis.interruptedLifecycle!.failed, interruptedRounds: 2 } } } } })).toBe(false);
});
test('Durable progress reconciles the failed round count and rejects loss of already published interruption evidence', () => {
  const frame = { ...progress(1, 0), measurements: [metric], measurementHash: 'b'.repeat(64), execution: {
    regime: 'independentRounds', attemptedRounds: 1, completedRounds: 0, interruptedRounds: 1, cancelledRounds: 0, failedRounds: 1,
    completedSessions: 0, interruptedSessions: 0, carriesState: false, stateResetPolicy: 'Reset each round', sessionPolicy: 'No sessions', sessionMetrics: [],
  } };
  expect(validProgress(frame)).toBe(true); expect(validProgress({ ...frame, execution: undefined })).toBe(false);
  expect(validProgress({ ...frame, execution: { ...frame.execution, failedRounds: 0, interruptedRounds: 0 } })).toBe(false);
  const run = { ...snapshot(frame), measurements: [{ id: 'feature', name: 'Feature', nodeId: 'reveal', value: null, filter: null, unit: '' }], measurementHash: frame.measurementHash };
  const regression = { ...frame, sequence: 2, measurements: [{ ...metric, analysis: { ...analysis, interruptedLifecycle: { cancelled: empty, failed: empty } } }] };
  expect(progressDecision(run, frame, regression)).toBe('stale');
  expect(progressDecision(run, frame, { ...frame, sequence: 2 })).toBe('accept');
});
