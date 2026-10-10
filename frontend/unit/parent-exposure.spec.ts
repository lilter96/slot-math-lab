import { test, expect } from '@playwright/test';
import { validAnalysis } from '../src/lib/measurements/validation';
import { defaultOptions, type MeasurementAnalysis } from '../src/lib/measurements/analysis';
import { definition, formatStatistic, newMetric, statistic, type MeasurementSnapshot } from '../src/lib/measurements/model';

// An exit-excluded episode has a real accepted child but no numeric result.
const analysis: MeasurementAnalysis = {
  count: 0, min: null, max: null, mean: null, sum: null, subject: 'episode', reduction: 'average',
  distinctParents: 0, entries: 2, exits: 2, unclosedEpisodes: 0, uniqueAwards: 0, duplicateAwards: 0,
  parentExposure: { paidRoundsWithMatchingChildren: 1, episodesWithMatchingChildren: 1 },
  normalization: { paidRounds: 1, externalTurnover: 1, basis: 'Every settled paid round' },
  moments: { secondMoment: null, populationVariance: null, sampleVariance: null, coefficientOfVariation: null, skewness: null, excessKurtosis: null, meanAbsoluteDeviation: null },
  pair: null, support: [], supportComplete: true, bins: [{ lower: null, upper: null, count: 0, sum: 0, sumSquares: 0 }], quantiles: [],
  tails: [{ threshold: 1, count: 0, probability: null, sum: 0, mean: null, secondMoment: null }], upperTails: [],
  meanInterval: null, probabilityInterval: null, sequentialMeanInterval: null, meanStandardError: null, requiredSampleSize: null,
  weights: null, sequence: null, comparison: null, transitions: [], transitionsComplete: true, checks: [], groups: {},
};
const snapshot: MeasurementSnapshot = { id: 'parents', observations: 2, count: 0, excluded: 2, errors: 0, min: null, max: null, mean: null, sum: null, stdDev: null, firstError: null, analysis };

test('Exposure uses the authored parent population, independent of reduced-value count and units', () => {
  expect(validAnalysis(analysis)).toBe(true); expect(statistic(snapshot, 'distinctParents')).toBe(1); expect(statistic(snapshot, 'matchingEpisodes')).toBe(1);
  expect(formatStatistic(1, 'matchingEpisodes', '× stake')).toBe('1');
  expect(statistic({ ...snapshot, analysis: { ...analysis, parentExposure: undefined } }, 'distinctParents')).toBe(0);
  expect(statistic({ ...snapshot, analysis: { ...analysis, parentExposure: undefined } }, 'matchingEpisodes')).toBeNull();
});
test('Legacy evidence remains readable; impossible exposure and invented empty probabilities are rejected', () => {
  expect(validAnalysis({ ...analysis, parentExposure: undefined })).toBe(true);
  for (const parentExposure of [{ paidRoundsWithMatchingChildren: 2, episodesWithMatchingChildren: 1 }, { paidRoundsWithMatchingChildren: 1, episodesWithMatchingChildren: 3 }, { paidRoundsWithMatchingChildren: 1, episodesWithMatchingChildren: null }, { paidRoundsWithMatchingChildren: 1, episodesWithMatchingChildren: .5 }]) expect(validAnalysis({ ...analysis, parentExposure })).toBe(false);
  expect(validAnalysis({ ...analysis, subject: 'observation' })).toBe(false);
  expect(validAnalysis({ ...analysis, subject: 'observation', parentExposure: { paidRoundsWithMatchingChildren: 1, episodesWithMatchingChildren: null } })).toBe(true);
  expect(validAnalysis({ ...analysis, tails: [{ ...analysis.tails[0], probability: 0, secondMoment: 0 }] })).toBe(true);
  expect(validAnalysis({ ...analysis, tails: [{ ...analysis.tails[0], probability: .5 }] })).toBe(false);
});
test('An episode parent display requires the episode scope and its boundaries', () => {
  const metric = { ...newMetric('parents'), name: 'Matching parents', reducers: ['matchingEpisodes' as const], options: { ...defaultOptions(), source: 'count' as const } };
  expect(() => definition(metric)).toThrow('episode population');
  const valid = definition({ ...metric, nodeId: 'reveal', options: { ...metric.options, subject: 'episode', entryNodeId: 'enter', exitNodeId: 'exit' } });
  expect(valid.options?.subject).toBe('episode');
});
