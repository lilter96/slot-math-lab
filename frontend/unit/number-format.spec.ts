import { test, expect } from '@playwright/test';
import { formatNumber, formatPercent } from '../src/lib/numberFormat';
import { formatStatistic } from '../src/lib/measurements/model';
import { number, percent } from '../src/lib/results/model';

test('Nonzero metric evidence remains distinct from zero at dashboard and results display precision', () => {
  for (const value of [1e-9, -2.5e-7, Number.MIN_VALUE, 1e25]) {
    expect(Number(formatNumber(value))).not.toBe(0);
    expect(formatStatistic(value, 'mean', 'coins')).toContain('e');
    expect(number(value)).toContain('e');
  }
  expect(formatStatistic(1e-9, 'min', 'coins')).toBe('1e-9 coins');
  expect(formatStatistic(-2.5e-7, 'max', 'coins')).toBe('-2.5e-7 coins');
  expect(formatStatistic(1e-18, 'sampleVariance', 'coins')).toBe('1e-18 (coins)²');
  expect(formatStatistic(1e-9, 'accountingResidual', 'coins')).toBe('1e-9');
  expect(formatStatistic(0, 'mean', 'coins')).toBe('0 coins');
  expect(formatStatistic(null, 'mean', 'coins')).toBe('—');
  expect(formatStatistic(10000, 'count', '')).toBe((10000).toLocaleString());
});
test('Rare-event percentages keep nonzero exposure and ordinary RTP retains its familiar precision', () => {
  expect(formatPercent(1e-9)).toBe('1e-7%');
  expect(formatStatistic(1e-9, 'matchRate', '')).toBe('1e-7%');
  expect(percent(.98)).toBe('98.000%');
  expect(formatPercent(0)).toBe('0.000%');
  expect(formatPercent(Infinity)).toBe('—');
  expect(formatPercent(1e308)).toBe('1e+310%');
  expect(formatNumber(NaN)).toBe('—');
});
