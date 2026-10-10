import { parseExpression, type ExpressionAst } from '../expressionParser';
import type { MeasurementAnalysis, MeasurementOptions } from './analysis';
export interface MeasurementDefinition { id: string; name: string; nodeId: string | null; value: ExpressionAst | null; filter: ExpressionAst | null; unit: string; options?: MeasurementOptions }
export interface MeasurementWitness { roundIndex: number; observationOrdinal: number; nodeId: string | null; kind: string; value: number | null; pair: number | null; group: string | null; detail: string | null }
export interface MeasurementSnapshot { witnesses?: MeasurementWitness[]; id: string; observations: number; count: number; excluded: number; errors: number; min: number | null; max: number | null; mean: number | null; sum: number | null; stdDev: number | null; firstError: string | null; analysis?: MeasurementAnalysis | null }
export interface MeasurementSchema { points: { nodeId: string; label: string }[]; fields: { name: string; type: string }[] }
export function measurementPopulation(value: { nodeId?: string | null; options?: MeasurementOptions }): string {
  if (value.options?.subject === 'episode') return 'feature episodes';
  if (value.options?.subject === 'transition') return 'state transitions';
  if (value.options?.subject === 'round' || !value.nodeId) return 'paid rounds';
  return 'node visits';
}
export const reducers = ['min', 'max', 'mean', 'sum', 'count', 'stdDev', 'matchRate', 'secondMoment', 'populationVariance', 'sampleVariance', 'coefficientOfVariation', 'skewness', 'excessKurtosis', 'meanAbsoluteDeviation', 'covariance', 'correlation', 'ratio', 'distinctParents', 'uniqueAwards', 'duplicateAwards', 'eligible', 'excluded', 'invalid', 'featureEntries', 'featureExits', 'unclosedEpisodes', 'eventReciprocal', 'accountingResidual', 'meanDifference', 'varianceSum', 'varianceDifference', 'assertionViolations'] as const;
export type Reducer = typeof reducers[number];
export const reducerNames: Record<Reducer, string> = { min: 'Minimum', max: 'Maximum', mean: 'Average', sum: 'Sum', count: 'Matching count', stdDev: 'Standard deviation', matchRate: 'Matching share', secondMoment: 'Second moment', populationVariance: 'Population variance', sampleVariance: 'Sample variance', coefficientOfVariation: 'Coefficient of variation', skewness: 'Moment skewness', excessKurtosis: 'Excess moment kurtosis', meanAbsoluteDeviation: 'Mean absolute deviation', covariance: 'Paired covariance', correlation: 'Paired correlation', ratio: 'Payout / denominator', distinctParents: 'Distinct paid rounds', uniqueAwards: 'Unique award IDs', duplicateAwards: 'Duplicate award IDs', eligible: 'Eligible observations', excluded: 'Excluded observations', invalid: 'Invalid observations', featureEntries: 'Feature entries', featureExits: 'Feature exits', unclosedEpisodes: 'Unclosed feature episodes', eventReciprocal: 'Trials per event · empirical reciprocal', accountingResidual: 'Exposure accounting residual', meanDifference: 'Paired mean difference', varianceSum: 'Variance of the pair sum', varianceDifference: 'Variance of the pair difference', assertionViolations: 'Exact assertion violations' };
export interface MetricDraft { id: string; name: string; nodeId: string; valueMode: 'payout' | 'expression' | 'ast' | 'visual'; expression: string; filterMode: 'all' | 'expression' | 'ast' | 'visual'; filter: string; unit: string; reducers: Reducer[]; chart: boolean; hidden: boolean; options?: MeasurementOptions }
export const newMetric = (id: string = crypto.randomUUID()): MetricDraft => ({ id, name: '', nodeId: '', valueMode: 'payout', expression: '', filterMode: 'all', filter: '', unit: '× stake', reducers: ['min', 'max', 'mean'], chart: true, hidden: false });
export function metricExpression(source: string, mode: 'expression' | 'ast' | 'visual'): ExpressionAst {
  if (mode === 'expression') return parseExpression(source);
  const value = JSON.parse(source);
  if (!value || typeof value.exprType !== 'string') throw new Error('AST must be an object with exprType.');
  return value;
}
export function definition(d: MetricDraft): MeasurementDefinition {
  if (!d.name.trim() || d.name.length > 80) throw new Error('Give the metric a name (up to 80 characters).');
  if (d.unit.length > 24) throw new Error('Unit must be at most 24 characters.');
  if (d.options?.assertion === 'zero' && d.options.subject !== 'observation') throw new Error('An exact assertion checks each observation. Author a completed-subject residual at its boundary instead of aggregating away failures.');
  if (d.reducers.includes('assertionViolations') && d.options?.assertion !== 'zero') throw new Error('Configure an exact zero / false assertion before displaying its failure count.');
  if (d.reducers.includes('eventReciprocal') && d.options?.source !== 'event') throw new Error('Trials per event requires an authored Boolean event population.');
  if (!d.reducers.length) throw new Error('Choose at least one statistic to display.');
  const usesExpression = !d.options || ['value', 'event'].includes(d.options.source);
  if (usesExpression && (d.nodeId || d.options?.subject === 'transition') && d.valueMode === 'payout') throw new Error('Choose a state expression at a graph node. Settled payout exists at round completion.');
  if (d.reducers.some(r => ['covariance', 'correlation', 'ratio', 'meanDifference', 'varianceSum', 'varianceDifference'].includes(r)) && !d.options?.pair && d.options?.pairRole !== 'wager' && d.options?.subject !== 'transition') throw new Error('Configure a paired value or external wager before displaying paired statistics.');
  if (!d.options && d.reducers.some(r => reducers.indexOf(r) >= 7)) throw new Error('Enable distribution and verification collection for these statistics.');
  return { id: d.id, name: d.name.trim(), nodeId: d.nodeId || null, value: !usesExpression || d.valueMode === 'payout' ? null : metricExpression(d.expression, d.valueMode),
    filter: d.filterMode === 'all' ? null : metricExpression(d.filter, d.filterMode), unit: d.unit, ...(d.options ? { options: { ...d.options, assertion: d.options.assertion ?? 'none' } } : {}) };
}
export function draftFromDefinition(d: MeasurementDefinition): MetricDraft {
  return { ...newMetric(d.id), id: d.id, name: d.name, nodeId: d.nodeId ?? '', unit: d.unit, options: d.options,
    valueMode: d.value ? 'ast' : 'payout', expression: d.value ? JSON.stringify(d.value, null, 2) : '',
    filterMode: d.filter ? 'ast' : 'all', filter: d.filter ? JSON.stringify(d.filter, null, 2) : '' };
}
export function statistic(snapshot: MeasurementSnapshot | undefined, reducer: Reducer): number | null {
  if (!snapshot) return null;
  if (reducer === 'assertionViolations') return snapshot.analysis?.assertion?.violations ?? null;
  if (reducer === 'accountingResidual') return snapshot.observations - snapshot.count - snapshot.excluded - snapshot.errors;
  if (reducer === 'eventReciprocal') return snapshot.analysis?.subject && snapshot.count > 0 && snapshot.mean != null && snapshot.mean > 0 && snapshot.mean <= 1 ? 1 / snapshot.mean : null;
  if (reducer === 'meanDifference') return snapshot.analysis?.pair?.meanDifference ?? null;
  if (reducer === 'varianceSum') return snapshot.analysis?.pair?.sampleVarianceSum ?? null;
  if (reducer === 'varianceDifference') return snapshot.analysis?.pair?.sampleVarianceDifference ?? null;
  if (reducer === 'count') return snapshot.count;
  if (reducer === 'eligible') return snapshot.observations;
  if (reducer === 'excluded') return snapshot.excluded;
  if (reducer === 'invalid') return snapshot.errors;
  if (['featureEntries', 'featureExits', 'unclosedEpisodes'].includes(reducer)) return snapshot.analysis?.[reducer === 'featureEntries' ? 'entries' : reducer === 'featureExits' ? 'exits' : 'unclosedEpisodes'] ?? null;
  if (reducer === 'matchRate') return snapshot.observations ? snapshot.count / snapshot.observations : null;
  if (['min', 'max', 'mean', 'sum', 'stdDev'].includes(reducer)) return snapshot[reducer as 'mean'];
  if (['covariance', 'correlation', 'ratio'].includes(reducer)) return snapshot.analysis?.pair?.[reducer as 'ratio'] ?? null;
  if (['distinctParents', 'uniqueAwards', 'duplicateAwards', 'eligible', 'excluded', 'invalid', 'featureEntries', 'featureExits', 'unclosedEpisodes'].includes(reducer)) return snapshot.analysis?.[reducer as 'distinctParents'] ?? null;
  return snapshot.analysis?.moments[reducer as 'secondMoment'] ?? null;
}
export function formatStatistic(value: number | null, reducer: Reducer, unit: string): string {
  if (value == null) return '—';
  if (reducer === 'matchRate') return `${(value * 100).toFixed(2)}%`;
  if (['assertionViolations', 'count', 'distinctParents', 'uniqueAwards', 'duplicateAwards', 'eligible', 'excluded', 'invalid', 'featureEntries', 'featureExits', 'unclosedEpisodes'].includes(reducer)) return value.toLocaleString();
  if (['coefficientOfVariation', 'skewness', 'excessKurtosis', 'correlation', 'ratio', 'eventReciprocal', 'accountingResidual'].includes(reducer)) unit = '';
  if (['secondMoment', 'populationVariance', 'sampleVariance', 'covariance', 'varianceSum', 'varianceDifference'].includes(reducer) && unit) unit = `(${unit})²`;
  return `${value.toLocaleString(undefined, { maximumFractionDigits: 4 })}${unit ? ` ${unit}` : ''}`;
}

export function samePlan(a: MeasurementDefinition[], b: MeasurementDefinition[]): boolean {
  const normalize = (v: unknown): unknown => Array.isArray(v) ? v.map(normalize) : v && typeof v === 'object' ? Object.fromEntries(Object.entries(v).filter(([, value]) => value != null).sort(([x], [y]) => x.localeCompare(y)).map(([k, value]) => [k, normalize(value)])) : v;
  // Older persisted drafts omit this optional setting. Only its declared
  // semantic default is normalized; a changed rule still changes the plan.
  const defaults = (plan: MeasurementDefinition[]) => plan.map(d => d.options ? { ...d, options: { ...d.options, assertion: d.options.assertion ?? 'none' } } : d);
  return JSON.stringify(normalize(defaults(a))) === JSON.stringify(normalize(defaults(b)));
}
