import { test, expect } from '@playwright/test';
import { serializeCheckpoint } from '../src/lib/measurements/checkpoints';
import { defaultExecution, sameExecution } from '../src/lib/measurements/execution';
import { definition, newMetric, type MeasurementSnapshot } from '../src/lib/measurements/model';
import { snapshotDecision, validSnapshot } from '../src/lib/realtime/runProtocol';
import { completed } from './realtime-fixtures';

test('Large live distributions stay on the server while reload retains the exact plan and a bounded trend', () => {
  const run = completed(), metric = { ...newMetric('law'), name: 'Pinned payout law' }, pinned = [definition(metric)];
  const measured = { id: 'law', observations: 1000, count: 1000, errors: 0, excluded: 0, min: 0, max: 2, mean: 1, sum: 1000, stdDev: 1, firstError: null,
    analysis: { support: 'large evidence'.repeat(10000) } } as unknown as MeasurementSnapshot;
  run.measurements = pinned; run.measurementHash = run.progress!.measurementHash = 'b'.repeat(64); run.progress!.measurements = [measured];
  const session = { run, progress: run.progress!, history: Array.from({ length: 50 }, (_, i) => ({ ...run, id: `prior-${i}` })),
    points: Array.from({ length: 1000 }, (_, i) => ({ n: i, measurements: [measured] })) };
  const serialized = serializeCheckpoint(session), restored = JSON.parse(serialized);
  expect(serialized.length).toBeLessThanOrEqual(1_500_000); expect(restored.run.measurements).toEqual(pinned);
  expect(restored.run.resultJson).toBeNull(); expect(restored.progress.resultJson).toBeNull(); expect(restored.points.at(-1).n).toBe(999);
  expect(restored.run.progress.measurements[0].analysis).toBeUndefined(); expect(restored.points[0].measurements[0].analysis).toBeUndefined();
  expect(validSnapshot(restored.run)).toBe(true);
  const authoritative = { ...run, progress: { ...run.progress!, measurements: restored.run.progress.measurements } };
  expect(snapshotDecision(restored.run, restored.run.progress, authoritative)).toBe('duplicate'); // Same revision can enrich terminal evidence.
});
test('Reload and replay do not substitute a different state regime or bankroll', () => {
  const run = completed(); run.execution = { ...defaultExecution(), regime: 'sessions' }; run.streamScheme = 'splitmix64-session-v1-100';
  const restored = JSON.parse(serializeCheckpoint({ run, progress: run.progress!, points: [], history: [] })).run;
  expect(restored.execution).toEqual(run.execution); expect(sameExecution(run.execution, { ...run.execution, initialBankroll: 200 })).toBe(false);
  expect(snapshotDecision(restored, restored.progress, { ...run, execution: { ...run.execution, wager: 2 } })).toBe('invalid');
});
test('Visual AST and textual settlement predicates produce the same pinned Boolean rule', () => {
  const text = definition({ ...newMetric('zero'), name: 'Zero payout', valueMode: 'expression', expression: 'measurement.payout == 0' });
  const visual = definition({ ...newMetric('zero'), name: 'Zero payout', valueMode: 'visual', expression: JSON.stringify(text.value) });
  expect(visual.value).toEqual(text.value); expect(text.value?.left).toEqual({ exprType: 'fieldAccess', target: 'measurement', path: ['payout'] });
  expect(() => definition({ ...newMetric('bad'), name: 'Invalid', valueMode: 'visual', expression: '{"missing":"type"}' })).toThrow();
});
