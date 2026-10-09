import { test, expect } from '@playwright/test';
import { progressDecision, snapshotDecision, validProgress, validSnapshot } from '../src/lib/realtime/runProtocol';
import { completed, epoch, progress, snapshot } from './realtime-fixtures';

test('cumulative revisions reject reordered packets, regressed counts, wrong runs and terminal resurrection', () => {
  const current = progress(10, 100), run = snapshot(current);
  expect(progressDecision(run, current, progress(9, 90))).toBe('stale');
  expect(progressDecision(run, current, progress(11, 99))).toBe('stale');
  expect(progressDecision(run, current, { ...progress(11, 110), runId: 'another' })).toBe('wrong-run');
  expect(progressDecision(run, current, current)).toBe('duplicate');
  expect(progressDecision(run, current, progress(11, 110))).toBe('accept');
  const done = completed();
  expect(progressDecision(done, done.progress!, progress(100, 1000))).toBe('stale');
  expect(progressDecision(snapshot({ ...current, status: 'cancelling' }), current, progress(11, 110))).toBe('stale');
});
test('malformed and internally inconsistent snapshots cannot update metrics', () => {
  const p = progress();
  for (const broken of [{ ...p, runningRtp: NaN }, { ...p, sequence: Infinity }, { ...p, nonZeroCount: 20 },
    { ...p, histogram: [{ lo: 0, hi: null, count: 9 }] }, { ...p, status: 'live' }, { ...p, sampleCount: 1001 },
    { ...p, resultJson: '{}' }, { ...p, status: 'completed', resultJson: '{' },
    { ...p, status: 'completed', resultJson: '{"sampleCount":20}' }]) expect(validProgress(broken)).toBe(false);
  const s = snapshot();
  expect(validSnapshot({ ...s, sequence: 2 })).toBe(false);
  expect(validSnapshot({ ...s, streamEpoch: 'b'.repeat(32) })).toBe(false);
  expect(snapshotDecision(s, s.progress!, { ...s, seed: 43 })).toBe('invalid');
  expect(snapshotDecision(s, s.progress!, { ...s, configHash: 'e'.repeat(64) })).toBe('invalid');
});
test('only an authoritative interrupted checkpoint permits a new epoch and lower revision', () => {
  const run = snapshot(progress(100, 500));
  const checkpoint = { ...progress(3, 100), streamEpoch: 'b'.repeat(32), status: 'failed' as const,
    resultJson: '{"code":"RUN_INTERRUPTED","error":"restart"}' };
  expect(progressDecision(run, run.progress!, checkpoint)).toBe('epoch-change');
  expect(snapshotDecision(run, run.progress!, snapshot(checkpoint))).toBe('reset');
  expect(snapshotDecision(run, run.progress!, snapshot({ ...checkpoint, resultJson: '{"error":"something else"}' }))).toBe('invalid');
  const recovered = snapshot(checkpoint);
  expect(progressDecision(recovered, checkpoint, { ...progress(200, 600), streamEpoch: epoch })).toBe('stale');
});
test('arbitrary delivery order converges to the same final snapshot without resurrecting a run', () => {
  for (let seed = 1; seed <= 100; seed++) {
    const packets = Array.from({ length: 50 }, (_, i) => progress(i + 1, (i + 1) * 10));
    packets.push(completed(51, 500).progress!);
    let random = seed;
    for (let i = packets.length - 1; i > 0; i--) {
      random = (Math.imul(random, 1664525) + 1013904223) >>> 0;
      const j = random % (i + 1); [packets[i], packets[j]] = [packets[j], packets[i]];
    }
    let run = snapshot({ ...progress(0, 0), status: 'pending' });
    for (const p of [...packets, ...packets]) if (progressDecision(run, run.progress!, p) === 'accept') run = snapshot(p);
    expect(run).toEqual(completed(51, 500));
  }
});
