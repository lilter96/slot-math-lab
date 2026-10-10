import type { components as ApiComponents } from '../../api/generated-types';
import type { ExpressionAst } from '../expressionParser';
import { defaultOptions } from './analysis';
import { draftFromDefinition, type MeasurementDefinition, type MetricDraft } from './model';
export interface AccountingInput { name: string; nodeId: string | null; unit: string; stake: number; total: ExpressionAst; components: { name: string; value: ExpressionAst }[]; filter: ExpressionAst | null; group: ExpressionAst | null; supportLimit: number; groupLimit: number }
// Transport types come from the server OpenAPI contract. Authoring inputs
// above remain local UI state; only validated pinned measurements are evidence.
export type AccountingRequest = ApiComponents['schemas']['ComponentAccountingRequest'];
export type AccountingReport = ApiComponents['schemas']['ComponentAccountingReport'];
export type AccountingEvidence = ApiComponents['schemas']['MeasurementAccountingReport'];
export type RetainedAccounting = ApiComponents['schemas']['RetainedReferenceOfMeasurementAccountingReport'];
export const payoutExpression = (): ExpressionAst => ({ exprType: 'fieldAccess', target: 'measurement', path: ['payout'] });
export function accountingPlan(input: AccountingInput, prefix = `accounting-${crypto.randomUUID().slice(0, 12)}`): { metrics: MetricDraft[]; request: AccountingRequest } {
  if (!input.name.trim() || input.name.length > 64) throw new Error('Name the reconciliation (up to 64 characters).');
  if (input.components.length < 2 || input.components.length > 6 || input.components.some(c => !c.name.trim())) throw new Error('Name 2–6 components and author their values.');
  const options = { ...defaultOptions(), binEdges: [0], quantiles: [], thresholds: [], supportLimit: input.supportLimit, groupLimit: input.groupLimit,
    stake: input.stake, ...(input.group ? { group: input.group } : {}) };
  const make = (suffix: string, name: string, value: ExpressionAst, pair?: ExpressionAst, assertion = false): MetricDraft => {
    const d: MeasurementDefinition = { id: `${prefix}-${suffix}`, name: `${input.name.trim()} · ${name}`, nodeId: input.nodeId, value, filter: input.filter, unit: input.unit,
      options: { ...options, ...(pair ? { pair } : {}), ...(assertion ? { assertion: 'zero' } : {}) } };
    if (d.name.length > 80) throw new Error('Shorten the reconciliation or component name; their combined metric name is limited to 80 characters.');
    return { ...draftFromDefinition(d), reducers: assertion ? ['assertionViolations', 'count', 'invalid'] : pair ? ['covariance', 'count', 'invalid'] : ['mean', 'sampleVariance', 'count'], chart: !pair, hidden: !!pair, chartStatistic: assertion ? 'assertionViolations' : 'mean' };
  };
  const metrics = [make('total', 'Total', input.total), ...input.components.map((c, i) => make(`component${i}`, c.name, c.value))];
  for (let i = 0; i < input.components.length; i++) for (let j = i + 1; j < input.components.length; j++)
    metrics.push(make(`pair${i}-${j}`, `Covariance ${i + 1}/${j + 1}`, input.components[i].value, input.components[j].value));
  const sum = input.components.slice(1).reduce<ExpressionAst>((left, c) => ({ exprType: 'binary', op: 'Add', left, right: c.value }), input.components[0].value);
  const residual = make('residual', 'Exact reconciliation', { exprType: 'binary', op: 'Sub', left: input.total, right: sum }, undefined, true);
  metrics.push(residual);
  return { metrics, request: { name: input.name.trim(), totalMeasurementId: metrics[0].id, componentMeasurementIds: metrics.slice(1, input.components.length + 1).map(m => m.id), residualMeasurementId: residual.id } };
}
function canonical(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(canonical);
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).filter(([key, v]) => key !== 'annotation' && v != null).sort(([a], [b]) => a.localeCompare(b)).map(([key, v]) => [key, canonical(v)]));
  return value;
}
const same = (a: unknown, b: unknown) => JSON.stringify(canonical(a)) === JSON.stringify(canonical(b));
/** Discover the explicitly authored residual tree, not a payout rule or a
 * population guarantee. The server independently validates all scopes/pairs. */
export function accountingCandidates(plan: MeasurementDefinition[]): AccountingRequest[] {
  const results: AccountingRequest[] = [];
  const single = plan.filter(d => d.options?.subject === 'observation' && !d.options.pair && d.options.assertion !== 'zero');
  for (const residual of plan.filter(d => d.options?.assertion === 'zero')) {
    const tree = residual.value;
    if (tree?.exprType !== 'binary' || tree.op !== 'Sub') continue;
    const compatible = single.filter(d => d.nodeId === residual.nodeId && d.unit === residual.unit && same(d.filter, residual.filter) && same(d.options?.group, residual.options?.group));
    const total = compatible.find(d => same(d.value ?? payoutExpression(), tree.left));
    const used = new Set(total ? [total.id] : []);
    const find = (v: ExpressionAst, depth = 0): MeasurementDefinition[] | null => {
      if (!v || depth > 6) return null;
      const direct = compatible.find(d => !used.has(d.id) && same(d.value ?? payoutExpression(), v));
      if (direct) { used.add(direct.id); return [direct]; }
      if (v.exprType !== 'binary' || v.op !== 'Add') return null;
      const left = find(v.left as ExpressionAst, depth + 1), right = find(v.right as ExpressionAst, depth + 1);
      return left && right ? [...left, ...right] : null;
    };
    // The outer sum must have at least two terms. A component can itself be
    // an addition tree; its expression stays one component, never flattened.
    const sum = tree.right as ExpressionAst;
    if (sum?.exprType !== 'binary' || sum.op !== 'Add') continue;
    const left = find(sum.left as ExpressionAst), right = find(sum.right as ExpressionAst);
    const components = left && right ? [...left, ...right] : null;
    if (total && components && components.length >= 2 && components.length <= 6) results.push({ name: residual.name.replace(/ · Exact reconciliation$/, ''), totalMeasurementId: total.id, componentMeasurementIds: components.map(d => d.id), residualMeasurementId: residual.id });
  }
  return results;
}
