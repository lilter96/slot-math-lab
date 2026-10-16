import { test, expect } from '@playwright/test';
import { boardCells, cascadeTrace } from '../src/lib/roundTrace';
import type { GraphRound } from '../src/lib/graphApi';
const round: GraphRound = { seed: 42, roundIndex: 0, configHash: 'fixture', win: 1, winNumerator: '1', winDenominator: '1', state: {
  stepSpins: [0], stepBoardsBefore: [['1', '2']], stepBoardsAfter: [['3', '2']], stepMultipliers: ['2'], stepWins: [{ numerator: '3', denominator: '2' }], stepHitSymbols: [['1']], stepHitSizes: [[1]], stepHitTotals: [['1.5']], stepHitPositions: [[[0]]],
} };
test('Trace projection reads returned values and row-major positions without changing units', () => {
  expect(cascadeTrace(round, 2)).toEqual([{ spin: 0, before: ['1', '2'], after: ['3', '2'], multiplier: 2, win: 1.5, hits: [{ symbol: '1', size: 1, payout: 1.5, positions: [0] }] }]);
  expect(boardCells(['1'], 2)).toBeNull();
});
test('Missing, incomplete and out-of-bounds trace records have no reconstructed projection', () => {
  expect(cascadeTrace({ ...round, state: {} }, 2)).toBeNull();
  expect(cascadeTrace({ ...round, state: { ...round.state, stepWins: [] } }, 2)).toBeNull();
  expect(cascadeTrace({ ...round, state: { ...round.state, stepHitPositions: [[[2]]] } }, 2)).toBeNull();
});
