/** Preserve nonzero evidence at display precision. Formatting never changes
 * stored measurements or the precision of the authored monetary model. */
export function formatNumber(value: number | null | undefined, digits = 4, options: Intl.NumberFormatOptions = {}): string {
  if (value == null || !Number.isFinite(value)) return '—';
  if (value !== 0 && (Math.abs(value) < 10 ** -digits || Math.abs(value) >= 1e12)) {
    const [coefficient, exponent] = value.toExponential(digits).split('e');
    return `${coefficient.replace(/\.?0+$/, '')}e${exponent}`;
  }
  return value.toLocaleString(undefined, { maximumFractionDigits: digits, ...options });
}

export function formatPercent(value: number | null | undefined, digits = 3): string {
  if (value == null || !Number.isFinite(value)) return '—';
  const scaled = value * 100;
  if (!Number.isFinite(scaled)) {
    const [coefficient, exponent] = value.toExponential(digits).split('e');
    return `${coefficient.replace(/\.?0+$/, '')}e+${Number(exponent) + 2}%`;
  }
  return `${scaled !== 0 && (Math.abs(scaled) < 10 ** -digits || Math.abs(scaled) >= 1e12) ? formatNumber(scaled, digits) : scaled.toFixed(digits)}%`;
}
