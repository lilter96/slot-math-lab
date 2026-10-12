import { validateWeights, type DrawRow } from './modelCommands';

export function checkDrawRows(rows: Pick<DrawRow, 'weight'>[]): void {
  const error = validateWeights(rows.map(row => String(row.weight)));
  if (error) throw new Error(error);
}

/** Exact authored static-table summary; an expression override can use a different law. */
export function drawRowSummary(rows: { weight: number; value?: number }[]): { total: string; mean: string } | null {
  if (!rows.length || rows.some(row => !Number.isSafeInteger(row.weight) || row.weight < 0 || !Number.isSafeInteger(row.value ?? 0))) return null;
  const total = rows.reduce((sum, row) => sum + BigInt(row.weight), 0n);
  if (total === 0n) return { total: '0', mean: 'Undefined (zero total)' };
  const numerator = rows.reduce((sum, row) => sum + BigInt(row.weight) * BigInt(row.value ?? 0), 0n);
  let divisor = numerator < 0n ? -numerator : numerator, remainder = total;
  while (remainder) [divisor, remainder] = [remainder, divisor % remainder];
  return { total: String(total), mean: total / divisor === 1n ? String(numerator / divisor) : `${numerator / divisor}/${total / divisor}` };
}

/** Follow configPayload: native expression ID, with edited draw-row aliases taking precedence. */
export function drawCardSummary(data: { backendNode?: unknown; weightExpressionId?: unknown; drawWeights?: unknown }): { label: string; value: string } {
  const native = data.backendNode && typeof data.backendNode === 'object' ? data.backendNode as Record<string, unknown> : null;
  const expression = native ? native.weightExpressionId : data.weightExpressionId;
  if (expression !== undefined && expression !== null && expression !== '') return { label: 'mode', value: 'Dynamic weights' };
  const rows = data.drawWeights || native?.drawWeights;
  if (!Array.isArray(rows) || rows.length === 0) return { label: 'mode', value: 'Unconfigured' };
  if (rows.some(row => !row || typeof row !== 'object' || typeof row.weight !== 'number' || (row.value !== undefined && typeof row.value !== 'number'))) return { label: 'mode', value: 'Unsupported rows' };
  const summary = drawRowSummary(rows);
  return summary ? { label: 'Static mean', value: summary.mean } : { label: 'mode', value: 'Unsupported rows' };
}
