import { parseExpression, type ExpressionAst } from '../expressionParser';
export interface MeasurementDefinition { id: string; name: string; nodeId: string | null; value: ExpressionAst | null; filter: ExpressionAst | null; unit: string }
export interface MeasurementSnapshot { id: string; observations: number; count: number; excluded: number; errors: number; min: number | null; max: number | null; mean: number | null; sum: number | null; stdDev: number | null; firstError: string | null }
export interface MeasurementSchema { points: { nodeId: string; label: string }[]; fields: { name: string; type: string }[] }
export const reducers = ['min', 'max', 'mean', 'sum', 'count', 'stdDev', 'matchRate'] as const;
export type Reducer = typeof reducers[number];
export const reducerNames: Record<Reducer, string> = { min: 'Minimum', max: 'Maximum', mean: 'Average', sum: 'Sum', count: 'Matching count', stdDev: 'Standard deviation', matchRate: 'Matching share' };
export interface MetricDraft { id: string; name: string; nodeId: string; valueMode: 'payout' | 'expression' | 'ast'; expression: string; filterMode: 'all' | 'expression' | 'ast'; filter: string; unit: string; reducers: Reducer[]; chart: boolean; hidden: boolean }
export const newMetric = (id: string = crypto.randomUUID()): MetricDraft => ({ id, name: '', nodeId: '', valueMode: 'payout', expression: '', filterMode: 'all', filter: '', unit: '× stake', reducers: ['min', 'max', 'mean'], chart: true, hidden: false });
function ast(source: string, mode: 'expression' | 'ast'): ExpressionAst {
  if (mode === 'expression') return parseExpression(source);
  const value = JSON.parse(source);
  if (!value || typeof value.exprType !== 'string') throw new Error('AST must be an object with exprType.');
  return value;
}
export function definition(d: MetricDraft): MeasurementDefinition {
  if (!d.name.trim() || d.name.length > 80) throw new Error('Give the metric a name (up to 80 characters).');
  if (d.unit.length > 24) throw new Error('Unit must be at most 24 characters.');
  if (!d.reducers.length) throw new Error('Choose at least one statistic to display.');
  if (d.nodeId && d.valueMode === 'payout') throw new Error('Choose a state expression at a graph node. Settled payout exists at round completion.');
  return { id: d.id, name: d.name.trim(), nodeId: d.nodeId || null, value: d.valueMode === 'payout' ? null : ast(d.expression, d.valueMode),
    filter: d.filterMode === 'all' ? null : ast(d.filter, d.filterMode), unit: d.unit };
}
export function draftFromDefinition(d: MeasurementDefinition): MetricDraft {
  return { ...newMetric(d.id), id: d.id, name: d.name, nodeId: d.nodeId ?? '', unit: d.unit,
    valueMode: d.value ? 'ast' : 'payout', expression: d.value ? JSON.stringify(d.value, null, 2) : '',
    filterMode: d.filter ? 'ast' : 'all', filter: d.filter ? JSON.stringify(d.filter, null, 2) : '' };
}
export function statistic(snapshot: MeasurementSnapshot | undefined, reducer: Reducer): number | null {
  if (!snapshot) return null;
  if (reducer === 'count') return snapshot.count;
  if (reducer === 'matchRate') return snapshot.observations ? snapshot.count / snapshot.observations : null;
  return snapshot[reducer];
}
export function formatStatistic(value: number | null, reducer: Reducer, unit: string): string {
  if (value == null) return '—';
  if (reducer === 'matchRate') return `${(value * 100).toFixed(2)}%`;
  if (reducer === 'count') return value.toLocaleString();
  return `${value.toLocaleString(undefined, { maximumFractionDigits: 4 })}${unit ? ` ${unit}` : ''}`;
}

export function samePlan(a: MeasurementDefinition[], b: MeasurementDefinition[]): boolean {
  const normalize = (v: unknown): unknown => Array.isArray(v) ? v.map(normalize) : v && typeof v === 'object' ? Object.fromEntries(Object.entries(v).filter(([, value]) => value != null).sort(([x], [y]) => x.localeCompare(y)).map(([k, value]) => [k, normalize(value)])) : v;
  return JSON.stringify(normalize(a)) === JSON.stringify(normalize(b));
}
