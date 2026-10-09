import catalogue from './catalog.generated.json' with { type: 'json' };
import coverage from './implementation.generated.json' with { type: 'json' };
import { defaultOptions, type MeasurementOptions } from './analysis';
import { newMetric, type MetricDraft, type Reducer } from './model';
export type RecipeAction = 'measurement' | 'reference' | 'execution' | 'results' | 'planning';
export interface MetricRecipe { id: string; name: string; definition: string; unit: string; priority: string; family: string; familyName: string; population: string; parameters: string[]; action: RecipeAction; prerequisite: string; availability: 'ready' | 'authored' | 'partial'; implementationScope: string }
const references = new Set(['comparison.exact_support', 'return.theoretical', 'return.house_edge', 'bounds.pruned_mass', 'bounds.mean', 'bounds.event', 'bounds.second_variance', 'states.absorption', 'states.expected_duration', 'states.expected_reward', 'states.nontermination', 'states.long_run_return']);
const results = new Set(['counts.attempted_rounds', 'counts.interrupted_rounds', 'comparison.rtp_delta', 'comparison.probability_delta', 'comparison.component_delta', 'comparison.sensitivity', 'bounds.reachable_max', 'bounds.loop_termination', 'provenance.seed_replay', 'provenance.parallel', 'provenance.engine', 'provenance.serialization', 'provenance.recovery', 'provenance.identity', 'provenance.witness']);
const reviewed = coverage as Record<string, { status: MetricRecipe['availability']; scope: string }>;
export const metricRecipes: MetricRecipe[] = catalogue.families.flatMap(f => f.metrics.map(m => ({ id: m[0], name: m[1], definition: m[2], unit: m[3], priority: m[5], availability: reviewed[m[0]].status, implementationScope: reviewed[m[0]].scope,
  family: f.id, familyName: f.name, population: f.subject, parameters: f.parameters,
  action: m[0] === 'inference.sample_plan' ? 'planning' as const : references.has(m[0]) ? 'reference' as const : results.has(m[0]) ? 'results' as const : f.id === 'experience' || m[0] === 'states.long_run_return' ? 'execution' as const : 'measurement' as const,
  prerequisite: references.has(m[0]) ? 'Supply an independent finite payout law or transient transition/reward model. Exact input probabilities and mode cost are required.'
    : results.has(m[0]) ? 'Use pinned saved runs and independent comparison evidence. A sampled maximum, matching replay or green statistical check alone cannot prove model correctness.'
    : f.id === 'experience' ? 'Choose independent sessions, their horizon, initial bankroll, cost and retained state. First-passage and censored populations must remain distinct.'
    : f.id === 'rng' || f.id === 'mechanics' ? 'Select an authored point immediately after the value is recorded. Specify reel, symbol, award, strategy or state cohort; a node visit alone does not identify a reveal.'
    : 'Choose the authored observation point, scope and units. Independent-subject inference requires an explicit model guarantee.' })));
const scalar: Record<string, Reducer> = { 'counts.eligible': 'eligible', 'counts.matching': 'count', 'counts.excluded': 'excluded', 'counts.invalid': 'invalid', 'counts.feature_entries': 'featureEntries', 'counts.feature_exits': 'featureExits', 'counts.visits': 'count', 'counts.reveals': 'count', 'counts.distinct_parents': 'distinctParents', 'counts.unique_awards': 'uniqueAwards',
  'moments.sum': 'sum', 'moments.min': 'min', 'moments.max': 'max', 'moments.mean': 'mean', 'moments.second': 'secondMoment', 'moments.variance': 'sampleVariance', 'moments.sd': 'stdDev', 'moments.cv': 'coefficientOfVariation', 'moments.skew': 'skewness', 'moments.kurtosis': 'excessKurtosis', 'moments.mean_abs_deviation': 'meanAbsoluteDeviation',
  'dependence.covariance': 'covariance', 'dependence.correlation': 'correlation', 'return.rtp_mixed': 'ratio', 'return.component_share': 'ratio', 'return.mode': 'ratio', 'events.match_share': 'matchRate', 'events.reciprocal': 'eventReciprocal', 'dependence.paired_delta': 'meanDifference', 'dependence.variance_decomposition': 'varianceSum' };
const settlementEvents: Record<string, string> = { hit: 'measurement.payout > 0', zero: 'measurement.payout == 0', substake: 'measurement.payout > 0 && measurement.payout < measurement.cost', breakeven: 'measurement.payout == measurement.cost', profit: 'measurement.payout > measurement.cost' };
/** Recipes configure the generic collector. Required game-specific expressions remain empty:
 * choosing a catalogue entry cannot silently manufacture game semantics or numeric zeros. */
export function recipeDraft(recipe: MetricRecipe, id?: string): MetricDraft {
  if (recipe.action !== 'measurement') throw new Error('This recipe uses a reference, execution or evidence workflow.');
  const options = defaultOptions();
  const draft: MetricDraft = { ...newMetric(id), name: recipe.name, unit: recipe.unit.length <= 24 ? recipe.unit : '', options,
    valueMode: 'expression', expression: '', reducers: scalar[recipe.id] ? [scalar[recipe.id]] : ['min', 'max', 'mean'], chart: true };
  const [family, metric] = recipe.id.split('.');
  if (['moments', 'distribution', 'inference', 'comparison'].includes(family)) draft.valueMode = 'payout';
  if (family === 'events' && settlementEvents[metric]) { options.source = 'event'; options.referenceStatistic = 'probability'; draft.expression = settlementEvents[metric]; draft.unit = 'probability'; draft.reducers = ['mean', 'count']; }
  if (['counts.eligible', 'counts.matching', 'counts.excluded', 'counts.invalid'].includes(recipe.id)) { options.source = 'count'; draft.valueMode = 'payout'; }
  if (recipe.id === 'counts.completed_rounds') { options.source = 'count'; draft.valueMode = 'payout'; draft.reducers = ['count']; }
  if (recipe.id === 'counts.accounting_residual' || recipe.id === 'return.reconciliation' || recipe.id === 'return.rounding') { options.assertion = recipe.id === 'return.rounding' ? 'none' : 'zero'; draft.valueMode = 'expression'; draft.expression = ''; draft.reducers = ['min', 'max', 'sum', 'count', 'invalid', ...(options.assertion === 'zero' ? ['assertionViolations' as const] : [])]; }
  if (['counts.visits', 'counts.reveals', 'counts.distinct_parents'].includes(recipe.id)) { options.source = 'count'; draft.valueMode = 'payout'; draft.nodeId = '__choose_point__'; if (metric === 'distinct_parents') { options.subject = 'round'; options.reduction = 'any'; } }
  const sources: Record<string, MeasurementOptions['source']> = { 'return.turnover': 'turnover', 'return.net': 'net', 'bounds.cap_deduction': 'capDeduction' };
  if (sources[recipe.id]) { options.source = sources[recipe.id]; draft.valueMode = 'payout'; draft.reducers = ['sum', 'mean']; }
  if (['return.payout', 'return.rtp_fixed', 'return.mode', 'return.rtp_mixed'].includes(recipe.id)) { draft.valueMode = 'payout'; if (['rtp_fixed', 'mode', 'rtp_mixed'].includes(metric)) { options.pairRole = 'wager'; options.referenceStatistic = 'ratio'; draft.reducers = ['ratio']; } }
  if (['return.component', 'events.round_activation'].includes(recipe.id)) {
    options.subject = 'round'; draft.nodeId = '__choose_point__';
    if (['round_activation', 'any_retrigger'].includes(metric)) { options.source = 'event'; options.reduction = 'any'; options.referenceStatistic = 'probability'; }
  }
  if (['features.episode_payout', 'features.played', 'features.any_retrigger', 'counts.feature_entries', 'counts.feature_exits'].includes(recipe.id)) {
    options.subject = 'episode'; draft.nodeId = '__choose_point__'; if (['played', 'feature_entries', 'feature_exits'].includes(metric)) { options.source = 'count'; draft.valueMode = 'payout'; }
    if (metric === 'any_retrigger') { options.source = 'event'; options.reduction = 'any'; options.referenceStatistic = 'probability'; }
  }
  if (recipe.id === 'features.zero_episode') { options.source = 'event'; options.referenceStatistic = 'probability'; draft.nodeId = '__choose_point__'; }
  if (['states.transition_count', 'states.transition_probability'].includes(recipe.id)) { options.subject = 'transition'; draft.nodeId = ''; }
  if (family === 'mechanics' || family === 'rng' || ['reveal_payout', 'ordinal_profile', 'type_mix', 'exit_reason'].includes(metric)) draft.nodeId = '__choose_point__';
  if (['dependence.autocorrelation', 'dependence.gaps', 'dependence.streaks', 'rng.serial', 'rng.duplicates', 'states.dwell', 'events.reciprocal'].includes(recipe.id)) options.lags = [1, 2, 4];
  if (recipe.id === 'events.entry_rate') { options.subject = 'round'; options.source = 'count'; options.reduction = 'sum'; draft.nodeId = '__choose_point__'; draft.valueMode = 'payout'; draft.reducers = ['mean', 'sum', 'count']; }
  if (recipe.id === 'states.reset_violation') { options.source = 'event'; options.referenceStatistic = 'probability'; options.assertion = 'zero'; draft.nodeId = '__choose_point__'; }
  if (recipe.id === 'events.reciprocal' || recipe.id === 'events.conditional' || recipe.id === 'events.threshold' || recipe.id === 'bounds.zero_event') { options.source = 'event'; options.referenceStatistic = 'probability'; }
  if (recipe.id === 'comparison.invariants') { options.source = 'event'; options.assertion = 'zero'; draft.valueMode = 'expression'; draft.expression = ''; draft.reducers = ['sum', 'count', 'invalid', 'assertionViolations']; }
  if (['bounds.cap_reached', 'bounds.cap_exceeded'].includes(recipe.id)) { options.source = 'event'; options.referenceStatistic = 'probability'; draft.expression = metric === 'cap_exceeded' ? 'measurement.rawPayout > measurement.payout' : ''; }
  return draft;
}
