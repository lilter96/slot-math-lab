import { validateWeights, type DrawRow } from './modelCommands';
export function weightsCsv(rows: DrawRow[], weights = rows.map(row => String(row.weight))): string {
  const quote = (value: string) => `"${value.replaceAll('"', '""')}"`;
  return ['outcomeId,weight', ...rows.map((row, index) => `${quote(row.outcomeId)},${weights[index]}`)].join('\r\n');
}
function parseCsv(text: string): string[][] {
  const result: string[][] = []; let row: string[] = [], field = '', quoted = false, closed = false;
  const finishField = () => { row.push(field); field = ''; closed = false; };
  const finishRow = () => { finishField(); result.push(row); row = []; };
  text = text.replace(/^\uFEFF/, '');
  for (let i = 0; i < text.length; i++) {
    const char = text[i];
    if (quoted) { if (char === '"') { if (text[i + 1] === '"') { field += '"'; i++; } else { quoted = false; closed = true; } } else field += char; }
    else if (char === '"') { if (field || closed) throw new Error('Invalid CSV quoting.'); quoted = true; }
    else if (char === ',') finishField();
    else if (char === '\r' || char === '\n') { if (char === '\r' && text[i + 1] === '\n') i++; finishRow(); }
    else { if (closed) throw new Error('Unexpected text after quoted field.'); field += char; }
  }
  if (quoted) throw new Error('Unclosed CSV quote.');
  if (field || row.length || closed) finishRow();
  return result;
}
/** Require every original ID exactly once. File ordering never changes outcome ordering. */
export function importWeightsCsv(text: string, rows: DrawRow[]): string[] {
  const parsed = parseCsv(text);
  if (parsed[0]?.join(',') !== 'outcomeId,weight' || parsed.length !== rows.length + 1) throw new Error('Use the outcomeId,weight header and exactly one row per original outcome.');
  const byId = new Map<string, string>();
  for (const row of parsed.slice(1)) {
    if (row.length !== 2 || byId.has(row[0]) || !rows.some(original => original.outcomeId === row[0])) throw new Error('CSV contains duplicate, unknown or malformed outcomes.');
    byId.set(row[0], row[1]);
  }
  const weights = rows.map(row => byId.get(row.outcomeId)!);
  const error = validateWeights(weights);
  if (error) throw new Error(error);
  return weights.map(weight => String(Number(weight)));
}
