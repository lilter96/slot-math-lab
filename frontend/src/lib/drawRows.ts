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
