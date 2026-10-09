import { test, expect } from '@playwright/test';
import { definition, samePlan, statistic, formatStatistic, type MetricDraft, type MeasurementSnapshot } from '../src/lib/measurements/model';
import { validProgress, progressDecision, type LiveProgress, type RunSnapshot } from '../src/lib/realtime/runProtocol';
const draft: MetricDraft = { id: 'fs', name: 'Sticky FS payout', nodeId: 'fs/complete', valueMode: 'expression', expression: 'state.spinWin / 20',
  filterMode: 'expression', filter: 'state.fsType == "sticky"', unit: '× stake', reducers: ['min', 'max', 'mean'], chart: true, hidden: false };
const snapshot: MeasurementSnapshot = { id: 'fs', observations: 15, count: 10, excluded: 5, errors: 0, min: 2, max: 4, mean: 3, sum: 30, stdDev: Math.sqrt(10 / 9), firstError: null };
const progress: LiveProgress = { runId: 'run', sequence: 1, status: 'running', sampleCount: 5, totalSamples: 10, runningRtp: 0, stdErr: 0, elapsedMs: 1, hitFrequency: 0, nonZeroCount: 0, volatility: 0, maxWin: 0, capHits: 0, histogram: [{ lo: 0, hi: 1, count: 5 }], measurements: [snapshot], measurementHash: 'a'.repeat(64) };
test('Scoped DSL produces numeric and Boolean ASTs, never game-side payout code', () => {
  const plan = definition(draft); expect(plan.value?.exprType).toBe('binary'); expect(plan.filter?.exprType).toBe('compare'); expect(plan.nodeId).toBe('fs/complete');
  expect(() => definition({ ...draft, filter: 'state.fsType ==' })).toThrow();
  expect(() => definition({ ...draft, valueMode: 'payout' })).toThrow();
});
test('Advanced AST supports bounded constructor expressions', () => {
  const ast = { exprType: 'fold', stateKey: 'values', accName: 'acc', itemName: 'item', init: { exprType: 'constant', kind: 'Integer', value: '0' }, body: { exprType: 'fieldAccess', target: 'state', path: ['item'] } };
  expect(definition({ ...draft, valueMode: 'ast', expression: JSON.stringify(ast) }).value).toEqual(ast);
  expect(() => definition({ ...draft, valueMode: 'ast', expression: '[]' })).toThrow();
});
test('Empty scope is undefined; real zero remains numeric; matching share uses eligible visits', () => {
  expect(statistic({ ...snapshot, count: 0, mean: null, min: null, max: null, sum: null, stdDev: null, excluded: 15 }, 'mean')).toBeNull();
  expect(statistic({ ...snapshot, mean: 0 }, 'mean')).toBe(0); expect(statistic(snapshot, 'matchRate')).toBe(10 / 15);
  expect(formatStatistic(null, 'mean', '×')).toBe('—'); expect(formatStatistic(0, 'mean', '×')).toBe('0 ×');
});
test('Protocol rejects malformed, non-finite and wrong-plan observations', () => {
  expect(validProgress(progress)).toBe(true);
  for (const value of [{ ...snapshot, count: 11 }, { ...snapshot, mean: Infinity }, { ...snapshot, min: 5 }, { ...snapshot, stdDev: -1 }]) expect(validProgress({ ...progress, measurements: [value] })).toBe(false);
  const run = { id: 'run', status: 'running', measurementHash: progress.measurementHash, measurements: [definition(draft)] } as RunSnapshot;
  expect(progressDecision(run, null, { ...progress, measurementHash: 'b'.repeat(64) })).toBe('invalid');
  expect(progressDecision(run, null, { ...progress, measurements: [] })).toBe('invalid');
});
test('Plan equality ignores JSON order and nullable AST metadata, retaining scope and value', () => {
  const a = definition(draft), b = { ...a, value: { ...a.value!, annotation: null } }; expect(samePlan([a], [b])).toBe(true);
  expect(samePlan([a], [{ ...b, nodeId: 'other' }])).toBe(false);
});
test('Legacy null fingerprint remains compatible; round denominators and cumulative counts cannot regress', () => {
  const run = { id: 'run', status: 'running', configId: 'config', configHash: 'b'.repeat(64), configVersion: 1, seed: 42, degreeOfParallelism: 1,
    streamScheme: 'scheme', createdAt: 'today', progress: { ...progress, measurements: undefined, measurementHash: undefined } } as RunSnapshot;
  expect(progressDecision(run, null, { ...progress, measurements: [], measurementHash: null })).toBe('accept');
  const tracked = { ...run, measurementHash: progress.measurementHash, measurements: [definition({ ...draft, nodeId: '', valueMode: 'payout' })] };
  expect(progressDecision(tracked, null, progress)).toBe('invalid');
  const next = { ...progress, sequence: 2, measurements: [{ ...snapshot, count: 8, excluded: 7 }] };
  expect(progressDecision({ ...tracked, measurements: [definition(draft)] }, progress, next)).toBe('stale');
});
test('Results rejects persisted tracked aggregates that disagree with the live snapshot', async () => {
  const { inspectRun } = await import('../src/lib/results/model');
  const p: LiveProgress = { ...progress, totalSamples: 5, status: 'completed' };
  const run: RunSnapshot = { id: 'run', seed: 42, configId: 'config', configHash: 'b'.repeat(64), configVersion: 1, degreeOfParallelism: 1, streamScheme: 'scheme', status: 'completed', createdAt: 'today', measurementHash: p.measurementHash, measurements: [definition(draft)], progress: p };
  const result = { status: 'completed', sampleCount: 5, totalSamples: 5, seed: 42, configHash: run.configHash, configVersion: 1, degreeOfParallelism: 1, streamScheme: 'scheme', rtp: 0, stdErr: 0, hitFrequency: 0, maxWin: 0, volatility: 0, adaptiveHistogram: p.histogram, measurementHash: run.measurementHash, measurements: [snapshot] };
  run.resultJson = JSON.stringify(result); p.resultJson = run.resultJson;
  expect(inspectRun(run).issues).toEqual([]);
  run.resultJson = JSON.stringify({ ...result, measurements: [{ ...snapshot, mean: 33 }] }); p.resultJson = run.resultJson;
  expect(inspectRun(run).issues).toContain('Persisted tracked measurements differ from the pinned run snapshot.');
});
