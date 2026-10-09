import type { ExpressionAst } from '../expressionParser';
export interface MeasurementOptions {
  subject: 'observation' | 'round' | 'episode' | 'transition'; reduction: 'sum' | 'count' | 'any' | 'all' | 'first' | 'last' | 'min' | 'max' | 'average' | 'delta';
  source: 'value' | 'event' | 'count' | 'rawPayout' | 'capDeduction' | 'turnover' | 'net';
  entryNodeId?: string; exitNodeId?: string; group?: ExpressionAst; pair?: ExpressionAst; weight?: ExpressionAst; awardId?: ExpressionAst;
  entryFilter?: ExpressionAst; exitFilter?: ExpressionAst;
  pairRole: 'value' | 'wager'; referenceStatistic: 'mean' | 'ratio' | 'probability';
  binEdges: number[]; quantiles: number[]; thresholds: number[]; supportLimit: number; groupLimit: number; lags: number[];
  stake: number; lowerBound?: number; upperBound?: number; confidence: number; errorFamilySize: number; independentSubjects: boolean; independentParents?: boolean;
  referenceMean?: number; tolerance?: number; referenceDistribution: { value: number; probability: number }[];
}
export const defaultOptions = (): MeasurementOptions => ({ subject: 'observation', reduction: 'sum', source: 'value',
  pairRole: 'value', referenceStatistic: 'mean',
  binEdges: [0, 1, 2, 5, 10, 20, 50, 100, 500, 1000], quantiles: [0.5, 0.9, 0.95, 0.99], thresholds: [1, 10, 100], supportLimit: 256,
  groupLimit: 32, lags: [], stake: 1, confidence: 0.95, errorFamilySize: 1, independentSubjects: false, independentParents: false, referenceDistribution: [] });
export interface NumericInterval { lower: number; upper: number; method: string; assumptions: string }
export interface MeasurementAnalysis {
  count: number; min: number | null; max: number | null; mean: number | null; sum: number | null;
  subject: string; reduction: string; distinctParents: number; entries: number; exits: number; unclosedEpisodes: number;
  uniqueAwards: number; duplicateAwards: number;
  moments: { secondMoment: number | null; populationVariance: number | null; sampleVariance: number | null; coefficientOfVariation: number | null; skewness: number | null; excessKurtosis: number | null; meanAbsoluteDeviation: number | null };
  pair: { count: number; sumY: number; meanY: number | null; covariance: number | null; correlation: number | null; ratio: number | null; ratioInterval: NumericInterval | null; meanDifference: number | null; sampleVarianceY?: number | null; sampleVarianceSum?: number | null; sampleVarianceDifference?: number | null; differenceInterval?: NumericInterval | null;
    joint?: { complete: boolean; support: { x: number; y: number; count: number; probability: number }[]; chiSquare: number | null; degreesOfFreedom: number; expectedCountsAdequate: boolean; pValue: number | null; calibration: string } | null } | null;
  support: { value: number; count: number; sum: number }[]; supportComplete: boolean;
  bins: { lower: number | null; upper: number | null; count: number; sum: number; sumSquares: number }[];
  quantiles: { probability: number; value: number | null; lower: number | null; upper: number | null; method: string }[];
  tails: { threshold: number; count: number; probability: number; sum: number; mean: number | null; secondMoment: number }[];
  upperTails: { quantile: number; tailMass: number; mean: number | null; lowerMean: number | null; upperMean: number | null; lowerReturnShare: number | null; upperReturnShare: number | null; method: string }[];
  meanAbsoluteDeviationBounds?: NumericInterval | null;
  meanInterval: NumericInterval | null; probabilityInterval: NumericInterval | null; sequentialMeanInterval: NumericInterval | null; clusteredMeanInterval?: NumericInterval | null;
  meanStandardError: number | null; requiredSampleSize: number | null;
  weights: { weightSum: number; weightSquares: number; effectiveSampleSize: number | null; ordinaryEstimate: number | null; selfNormalizedEstimate: number | null; minWeight: number; maxWeight: number; ordinaryInterval: NumericInterval | null; eventEstimate: number | null; eventInterval: NumericInterval | null; pairedRatio: number | null; pairedRatioInterval: NumericInterval | null } | null;
  sequence: { count: number; events: number; longestEventStreak: number; longestDrought: number; completedGaps: number; meanGap: number | null; autocorrelations: Record<string, number | null>; ordered: boolean; equalAdjacentPairs: number; adjacentPairs: number; stateDwell: { state: number; completedRuns: number; observations: number; maximumLength: number; meanLength: number }[]; stateDwellComplete: boolean } | null;
  comparison: { totalVariation: number; cdfDistance: number; chiSquare: number | null; degreesOfFreedom: number; expectedCountsAdequate: boolean; unexpectedObservations: number; pValue: number | null; calibration: string } | null;
  transitions: { from: number; to: number; count: number; fromExposure: number; probability: number }[]; transitionsComplete: boolean;
  checks: { id: string; status: string; observed: number | null; reference: number | null; difference: number | null; detail: string }[];
  groups: Record<string, MeasurementAnalysis>; groupsComplete?: boolean;
}
