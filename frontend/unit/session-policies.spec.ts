import { test, expect } from '@playwright/test';
import { defaultExecution, sameExecution, validateExecution, validExecutionSummary } from '../src/lib/measurements/execution';
import { inspectRun, interval, tailBounds } from '../src/lib/results/model';
import { snapshot, progress } from './realtime-fixtures';

test('Session policy, threshold and raw stream auditing are pinned execution choices', () => {
  const base = { ...defaultExecution(), regime: 'sessions' as const, sessionStop: 'ruin' as const };
  validateExecution(base, 1000, 2);
  expect(sameExecution(base, { ...base, sessionStop: 'lossLimit' })).toBe(false);
  expect(sameExecution(base, { ...base, stopThreshold: 20 })).toBe(false);
  expect(sameExecution(base, { ...base, auditRandomStreams: true })).toBe(false);
  expect(() => validateExecution({ ...base, sessionStop: 'featureFirst' }, 1000, 2)).toThrow(/feature/);
});
test('A fully completed stopped-session population is complete evidence with its round interval withheld', () => {
  const p = { ...progress(3, 200), stdErr: 1 / Math.sqrt(200), status: 'completed' as const, execution: { regime: 'sessions', attemptedRounds: 200, completedRounds: 200, interruptedRounds: 0, cancelledRounds: 0, failedRounds: 0, completedSessions: 10, interruptedSessions: 0, carriesState: false, stateResetPolicy: 'Reset session', sessionPolicy: 'Ruin stopping', sessionStop: 'ruin', plannedRoundSlots: 1000, sessionMetrics: [] } };
  const run = { ...snapshot(p), execution: { ...defaultExecution(), regime: 'sessions' as const, sessionStop: 'ruin' as const } };
  run.resultJson = JSON.stringify({ status: run.status, sampleCount: p.sampleCount, totalSamples: p.totalSamples, seed: run.seed, configHash: run.configHash, configVersion: run.configVersion, degreeOfParallelism: run.degreeOfParallelism, streamScheme: run.streamScheme, rtp: p.runningRtp, stdErr: p.stdErr, hitFrequency: p.hitFrequency, maxWin: p.maxWin, volatility: p.volatility, adaptiveHistogram: p.histogram });
  run.progress!.resultJson = run.resultJson;
  expect(validExecutionSummary(p.execution, 200)).toBe(true);
  expect(inspectRun(run).issues).toEqual([]); expect(inspectRun(run).complete).toBe(true); expect(interval(p)).toBeNull(); expect(tailBounds(p, 10).zeroEventUpper95).toBeNull();
  run.progress!.execution!.completedSessions = 9; expect(inspectRun(run).complete).toBe(false);
});
test('Raw stream evidence rejects incomplete counts and impossible schedule calibration', () => {
  const summary = { regime: 'independentRounds', attemptedRounds: 10, completedRounds: 10, interruptedRounds: 0, cancelledRounds: 0, failedRounds: 0, completedSessions: 0, interruptedSessions: 0, carriesState: false, stateResetPolicy: 'reset', sessionPolicy: '', sessionMetrics: [], randomStreams: { streams: 2, expectedStreams: 2, collisionCandidatePairs: 0, scheduledInitialWordCollisionProbability: 0, nonemptyStreams: 2, comparablePrefixes: 2, complete: true, duplicates: [], idealIndependentPrefixCollisionUpperBound: 2 ** -256, calibration: 'Reference null', detail: 'Actual consumed words' } };
  expect(validExecutionSummary(summary, 10)).toBe(true);
  expect(validExecutionSummary({ ...summary, randomStreams: { ...summary.randomStreams, scheduledInitialWordCollisionProbability: .5 } }, 10)).toBe(false);
  expect(validExecutionSummary({ ...summary, randomStreams: { ...summary.randomStreams, comparablePrefixes: 3 } }, 10)).toBe(false);
});
