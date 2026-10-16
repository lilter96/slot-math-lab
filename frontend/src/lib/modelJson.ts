/** Browser model numbers use IEEE-754; reject unsupported magnitudes before authoring. */
export class ModelNumberError extends Error {
  constructor() {
    super('This number is outside the exact browser range (±9007199254740991). It has not been applied; keep the original JSON for lossless storage.');
    this.name = 'ModelNumberError';
  }
}

export function assertModelNumbers(value: unknown): void {
  if (typeof value === 'number') {
    if (!Number.isFinite(value) || Math.abs(value) > Number.MAX_SAFE_INTEGER) throw new ModelNumberError();
  } else if (value && typeof value === 'object') {
    for (const child of Object.values(value)) assertModelNumbers(child);
  }
}

/** Inspect original tokens: a reviver alone cannot see values already rounded by JSON.parse. */
export function parseModelJson(text: string): Record<string, unknown> {
  const parsed: unknown = JSON.parse(text);
  for (const match of text.matchAll(/"(?:\\.|[^"\\])*"|(-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?)/g)) {
    const token = match[1];
    if (!token) continue; // String literals, including escaped quotes and numeric-looking IDs.
    const value = Math.abs(Number(token));
    if (!Number.isFinite(value) || value > Number.MAX_SAFE_INTEGER) throw new ModelNumberError();
    if (value !== Number.MAX_SAFE_INTEGER) continue;
    // At the boundary a fractional token can round down onto MAX_SAFE_INTEGER.
    const [mantissa, exponent = '0'] = token.replace(/^-/, '').toLowerCase().split('e');
    const [whole, fraction = ''] = mantissa.split('.');
    const digits = (whole + fraction).replace(/^0+/, '');
    const integerDigits = digits.length - fraction.length + Number(exponent);
    const prefix = digits.padEnd(16, '0').slice(0, 16);
    if (integerDigits > 16 || (integerDigits === 16 &&
      (prefix > '9007199254740991' || (prefix === '9007199254740991' && /[1-9]/.test(digits.slice(16)))))) throw new ModelNumberError();
  }
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('Choose a JSON model object.');
  return parsed as Record<string, unknown>;
}

/** Validate the user's original decimal text before any Number conversion reaches the model. */
export function parseModelNumber(text: string): number {
  const match = text.trim().match(/^([+-]?)(?:(\d+)(?:\.(\d*))?|\.(\d+))([eE][+-]?\d+)?$/);
  if (!match) throw new Error('Enter a complete finite number. This edit has not been applied.');
  const whole = (match[2] ?? '0').replace(/^0+(?=\d)/, '');
  const fraction = match[3] ?? match[4];
  const token = `${match[1] === '-' ? '-' : ''}${whole}${fraction === undefined ? '' : '.' + (fraction || '0')}${match[5] ?? ''}`;
  return parseModelJson(`{"value":${token}}`).value as number;
}
