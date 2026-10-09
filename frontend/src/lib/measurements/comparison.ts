import type { MeasurementSnapshot, MeasurementDefinition } from './model';
import type { RunSnapshot } from '../realtime/runProtocol';
export interface MetricComparison { id: string; name: string; unit: string; populationCompatible: boolean; selected: MeasurementSnapshot; baseline: MeasurementSnapshot;
  meanDelta: number | null; meanDifferenceInterval: readonly [number, number] | null; ratioDelta: number | null; varianceDelta: number | null; weightedMeanDelta: number | null; totalVariation: number | null; cdfDistance: number | null; note: string }
const difference = (a?: number | null, b?: number | null) => a != null && b != null && Number.isFinite(a - b) ? a - b : null;
const semantics = (d: MeasurementDefinition) => JSON.stringify([d.nodeId ?? null, d.value ?? null, d.filter ?? null, d.options ?? null, d.unit]);
/** Compare measured populations, never pool them. Changed graphs are useful sensitivity
 * evidence; same-seed variants require paired round evidence for a difference interval. */
export function compareMeasurements(selected: RunSnapshot, baseline: RunSnapshot): MetricComparison[] {
  return (selected.measurements ?? []).flatMap(d => {
    const other = baseline.measurements?.find(m => m.id === d.id), a = selected.progress?.measurements?.find(m => m.id === d.id), b = baseline.progress?.measurements?.find(m => m.id === d.id);
    if (!other || !a || !b) return [];
    const compatible = semantics(d) === semantics(other);
    const clean = selected.status === 'completed' && baseline.status === 'completed' && !a.errors && !b.errors && !a.analysis?.unclosedEpisodes && !b.analysis?.unclosedEpisodes && !a.analysis?.duplicateAwards && !b.analysis?.duplicateAwards;
    const independent = compatible && clean && selected.seed !== baseline.seed && !selected.execution?.persistentKeys.length && !baseline.execution?.persistentKeys.length && d.options?.independentSubjects && other.options?.independentSubjects && !a.analysis?.weights && !b.analysis?.weights;
    const delta = difference(a.mean, b.mean); let ci: readonly [number, number] | null = null;
    if (independent && delta != null && a.count > 1 && b.count > 1 && a.stdDev! > 0 && b.stdDev! > 0) {
      const half = 1.959963984540054 * Math.hypot(a.stdDev! / Math.sqrt(a.count), b.stdDev! / Math.sqrt(b.count));
      if (Number.isFinite(delta - half) && Number.isFinite(delta + half)) ci = [delta - half, delta + half];
    }
    let tv: number | null = null, distance: number | null = null;
    if (compatible && a.count && b.count && a.analysis?.supportComplete && b.analysis?.supportComplete && !a.analysis.weights && !b.analysis.weights) {
      const ap = new Map(a.analysis.support.map(p => [p.value, p.count / a.count])), bp = new Map(b.analysis.support.map(p => [p.value, p.count / b.count])); let sum = 0, cumulative = 0, maximum = 0;
      for (const value of [...new Set([...ap.keys(), ...bp.keys()])].sort((x, y) => x - y)) { const d = (ap.get(value) ?? 0) - (bp.get(value) ?? 0); sum += Math.abs(d); cumulative += d; maximum = Math.max(maximum, Math.abs(cumulative)); }
      tv = Math.min(1, sum / 2); distance = Math.min(1, maximum);
    }
    return [{ id: d.id, name: d.name, unit: d.unit, populationCompatible: compatible, selected: a, baseline: b, meanDelta: compatible ? delta : null, meanDifferenceInterval: ci,
      ratioDelta: compatible ? difference(a.analysis?.pair?.ratio, b.analysis?.pair?.ratio) : null, varianceDelta: compatible ? difference(a.analysis?.moments.sampleVariance, b.analysis?.moments.sampleVariance) : null,
      weightedMeanDelta: compatible ? difference(a.analysis?.weights?.ordinaryEstimate, b.analysis?.weights?.ordinaryEstimate) : null, totalVariation: tv, cdfDistance: distance,
      note: !compatible ? 'Collection definitions or units differ; numerical deltas are withheld.' : !clean ? 'Incomplete or invalid evidence; descriptive deltas only.' : selected.seed === baseline.seed ? 'Seeded streams can overlap. A paired-difference interval requires matched round covariance, so it is withheld.' : ci ? 'Unadjusted 95% fixed-count normal difference, assuming independent declared subjects and independent seeds; multiple exploratory comparisons and adaptive stopping are not covered.' : 'Descriptive effects. Independent-subject uncertainty is unresolved or not declared.' }];
  });
}
