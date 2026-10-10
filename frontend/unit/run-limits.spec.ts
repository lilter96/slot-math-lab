import { test, expect } from '@playwright/test';
import { LIMITS, RUN_WORKERS, progressBatchSize, runBudgetMinutes, runBudgetLabel } from '../src/lib/limits';

test('Run limits allow ten billion rounds and one to eight workers', () => {
  expect(LIMITS.maxRunRounds).toBe(10_000_000_000);
  expect(Number.isSafeInteger(LIMITS.maxRunRounds)).toBe(true);
  expect(RUN_WORKERS).toEqual([1, 2, 3, 4, 5, 6, 7, 8]);
});

test('The displayed execution budget follows the backend rule of five minutes per started ten million rounds', () => {
  expect(runBudgetMinutes(1)).toBe(5);
  expect(runBudgetMinutes(10_000_000)).toBe(5);
  expect(runBudgetMinutes(10_000_001)).toBe(10);
  expect(runBudgetMinutes(10_000_000_000)).toBe(5000);
  expect(runBudgetMinutes(Number.NaN)).toBe(5);
  expect(runBudgetLabel(100_000)).toBe('5-minute');
  expect(runBudgetLabel(200_000_000)).toBe('1 h 40 min');
  expect(runBudgetLabel(120_000_000)).toBe('1-hour');
  expect(runBudgetLabel(10_000_000_000)).toBe('83 h 20 min');
});

test('Long runs ask for one progress snapshot per random stream', () => {
  expect(progressBatchSize(10_000_000)).toBe(1000);
  expect(progressBatchSize(10_000_001)).toBe(65_536);
});
