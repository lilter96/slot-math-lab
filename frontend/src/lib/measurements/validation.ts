import type { MeasurementAnalysis } from './analysis';
const object = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const count = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const finite = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value);
const nullable = (value: unknown) => value == null || finite(value);
const text = (value: unknown) => typeof value === 'string' && value.length <= 1024;
const interval = (value: unknown) => value == null || object(value) && finite(value.lower) && finite(value.upper) && value.lower <= value.upper && text(value.method) && text(value.assumptions);
const near = (a: unknown, b: unknown) => a === b || finite(a) && finite(b) && Math.abs(a - b) <= 1e-9 * Math.max(1, Math.abs(a), Math.abs(b));

/** Reject corrupted rich snapshots before they enter the durable run store or charts. */
export function validAnalysis(value: unknown, expectedCount?: number, expectedMean?: number | null, expectedSum?: number | null, depth = 0): value is MeasurementAnalysis {
  if (!object(value) || depth > 1 || !count(value.count) || expectedCount != null && value.count !== expectedCount
    || expectedMean !== undefined && !near(value.mean, expectedMean) || expectedSum !== undefined && !near(value.sum, expectedSum)
    || !text(value.subject) || !text(value.reduction)) return false;
  for (const field of ['min', 'max', 'mean', 'sum', 'meanStandardError', 'requiredSampleSize']) if (!nullable(value[field])) return false;
  for (const field of ['distinctParents', 'entries', 'exits', 'unclosedEpisodes', 'uniqueAwards', 'duplicateAwards']) if (!count(value[field])) return false;
  if (value.normalization != null && (!object(value.normalization) || !count(value.normalization.paidRounds) || value.normalization.paidRounds === 0
    || !nullable(value.normalization.externalTurnover) || value.normalization.externalTurnover != null && (value.normalization.externalTurnover as number) <= 0 || !text(value.normalization.basis))) return false;
  if (value.assertion != null && (!object(value.assertion) || value.assertion.kind !== 'zero' || value.assertion.checked !== value.count
    || !count(value.assertion.violations) || value.assertion.violations > (value.count as number) || !['invalid', 'discrepancy', 'insufficient', 'noObservedViolations'].includes(String(value.assertion.status)))) return false;
  if (!object(value.moments) || !Object.values(value.moments).every(nullable) || !interval(value.meanInterval) || !interval(value.probabilityInterval) || !interval(value.sequentialMeanInterval) || !interval(value.clusteredMeanInterval)) return false;
  if (value.pair != null && (!object(value.pair) || !count(value.pair.count) || value.pair.count !== value.count
    || !['sumY', 'meanY', 'covariance', 'correlation', 'ratio', 'meanDifference'].every(key => nullable(value.pair && (value.pair as Record<string, unknown>)[key])) || !interval(value.pair.ratioInterval))) return false;
  if (value.groupsComplete !== undefined && typeof value.groupsComplete !== 'boolean') return false;
  if (object(value.pair)) {
    if (!['sampleVarianceY', 'sampleVarianceSum', 'sampleVarianceDifference'].every(k => nullable((value.pair as Record<string, unknown>)[k])) || !interval(value.pair.differenceInterval)) return false;
    const joint = value.pair.joint;
    if (joint != null && (!object(joint) || typeof joint.complete !== 'boolean' || !nullable(joint.chiSquare) || !nullable(joint.pValue) || !count(joint.degreesOfFreedom)
      || !text(joint.calibration) || typeof joint.expectedCountsAdequate !== 'boolean' || !Array.isArray(joint.support) || joint.support.length > 1024
      || !joint.support.every(p => object(p) && finite(p.x) && finite(p.y) && count(p.count) && finite(p.probability) && near(p.probability, p.count / (value.count as number)))
      || joint.complete && joint.support.reduce((n, p) => n + p.count, 0) !== value.count || !joint.complete && joint.support.length !== 0)) return false;
  }
  if (!Array.isArray(value.bins) || value.bins.length > 65 || !value.bins.every(b => object(b) && nullable(b.lower) && nullable(b.upper) && count(b.count) && finite(b.sum) && finite(b.sumSquares))) return false;
  if (value.bins.reduce((sum, bin) => sum + bin.count, 0) !== value.count || !near(value.bins.reduce((sum, bin) => sum + bin.sum, 0), value.sum ?? 0)) return false;
  if (typeof value.supportComplete !== 'boolean' || !Array.isArray(value.support) || value.support.length > 1024
    || !value.support.every(p => object(p) && finite(p.value) && count(p.count) && finite(p.sum))
    || value.supportComplete && value.support.reduce((sum, p) => sum + p.count, 0) !== value.count || !value.supportComplete && value.support.length !== 0) return false;
  if (!Array.isArray(value.quantiles) || value.quantiles.length > 16 || !value.quantiles.every(q => object(q) && finite(q.probability) && q.probability > 0 && q.probability <= 1 && nullable(q.value) && nullable(q.lower) && nullable(q.upper) && text(q.method))) return false;
  if (!Array.isArray(value.tails) || value.tails.length > 32 || !value.tails.every(t => object(t) && finite(t.threshold) && count(t.count) && t.count <= (value.count as number) && finite(t.probability) && t.probability >= 0 && t.probability <= 1 && finite(t.sum) && nullable(t.mean) && finite(t.secondMoment))) return false;
  if (!interval(value.meanAbsoluteDeviationBounds) || !Array.isArray(value.upperTails) || value.upperTails.length > 16 || !value.upperTails.every(t => object(t) && finite(t.quantile) && finite(t.tailMass) && nullable(t.mean) && nullable(t.lowerMean) && nullable(t.upperMean) && nullable(t.lowerReturnShare) && nullable(t.upperReturnShare) && text(t.method))) return false;
  if (value.sequence != null && (!object(value.sequence) || value.sequence.count !== value.count || typeof value.sequence.ordered !== 'boolean'
    || !['events', 'longestEventStreak', 'longestDrought', 'completedGaps'].every(key => count((value.sequence as Record<string, unknown>)[key]))
    || !nullable(value.sequence.meanGap) || !object(value.sequence.autocorrelations) || Object.keys(value.sequence.autocorrelations).length > 16 || !Object.values(value.sequence.autocorrelations).every(nullable))) return false;
  if (value.sequence != null) {
    const sequence = value.sequence as Record<string, unknown>;
    if (!count(sequence.equalAdjacentPairs) || !count(sequence.adjacentPairs) || sequence.adjacentPairs !== Math.max(0, (value.count as number) - 1) || sequence.equalAdjacentPairs > sequence.adjacentPairs
      || typeof sequence.stateDwellComplete !== 'boolean' || !Array.isArray(sequence.stateDwell) || sequence.stateDwell.length > 1024
      || !sequence.stateDwell.every(s => object(s) && finite(s.state) && count(s.completedRuns) && s.completedRuns > 0 && count(s.observations) && count(s.maximumLength) && finite(s.meanLength) && near(s.meanLength, s.observations / s.completedRuns))
      || !sequence.stateDwellComplete && sequence.stateDwell.length !== 0) return false;
  }
  if (value.weights != null && (!object(value.weights) || !['weightSum', 'weightSquares', 'effectiveSampleSize', 'ordinaryEstimate', 'selfNormalizedEstimate', 'minWeight', 'maxWeight', 'eventEstimate', 'pairedRatio'].every(key => nullable((value.weights as Record<string, unknown>)[key])) || !interval(value.weights.ordinaryInterval) || !interval(value.weights.eventInterval) || !interval(value.weights.pairedRatioInterval))) return false;
  if (value.comparison != null && (!object(value.comparison) || !finite(value.comparison.totalVariation) || !finite(value.comparison.cdfDistance) || !nullable(value.comparison.chiSquare) || !nullable(value.comparison.pValue) || value.comparison.pValue != null && ((value.comparison.pValue as number) < 0 || (value.comparison.pValue as number) > 1) || !text(value.comparison.calibration) || !count(value.comparison.degreesOfFreedom) || !count(value.comparison.unexpectedObservations) || typeof value.comparison.expectedCountsAdequate !== 'boolean')) return false;
  if (typeof value.transitionsComplete !== 'boolean' || !Array.isArray(value.transitions) || value.transitions.length > 1024 || !value.transitions.every(t => object(t) && finite(t.from) && finite(t.to) && count(t.count) && count(t.fromExposure) && t.fromExposure >= t.count && finite(t.probability) && t.probability >= 0 && t.probability <= 1)) return false;
  if (!Array.isArray(value.checks) || value.checks.length > 8 || !value.checks.every(c => object(c) && text(c.id) && text(c.status) && nullable(c.observed) && nullable(c.reference) && nullable(c.difference) && text(c.detail))) return false;
  return object(value.groups) && Object.keys(value.groups).length <= 64 && Object.values(value.groups).every(g => validAnalysis(g, undefined, undefined, undefined, depth + 1));
}

export function validWitnesses(value: unknown): boolean {
  return value === undefined || Array.isArray(value) && value.length <= 7 && new Set(value.map(w => w?.kind)).size === value.length
    && value.every(w => object(w) && count(w.roundIndex) && count(w.observationOrdinal) && (w.nodeId == null || text(w.nodeId))
      && ['first', 'minimum', 'maximum', 'invalid', 'duplicateAward', 'unexpectedSupport', 'assertionViolation'].includes(String(w.kind))
      && nullable(w.value) && nullable(w.pair) && (w.group == null || text(w.group)) && (w.detail == null || text(w.detail)));
}
