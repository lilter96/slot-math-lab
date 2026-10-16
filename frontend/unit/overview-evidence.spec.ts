import { test, expect } from '@playwright/test';
import { verifiedOverviewRun } from '../src/lib/overviewEvidence';
import { completed } from './realtime-fixtures';

// Independent Bernoulli fixture: 50 zero/50 one payouts, mean 1/2;
// unbiased variance = (1/4)*100/99 and SE = sqrt((1/4)/99).
function lookup(singleZero = false) {
  const n = singleZero ? 1 : 100, mean = singleZero ? 0 : .5;
  const run = completed(10, n), p = run.progress!;
  Object.assign(p, { sampleCount: n, totalSamples: n, runningRtp: mean, nonZeroCount: singleZero ? 0 : 50,
    hitFrequency: mean, volatility: singleZero ? 0 : Math.sqrt(.25 * 100 / 99), stdErr: singleZero ? 0 : Math.sqrt(.25 / 99),
    maxWin: singleZero ? 0 : 1, histogram: singleZero ? [{ lo: 0, hi: 1, count: 1 }] : [{ lo: 0, hi: 1, count: 50 }, { lo: 1, hi: 2, count: 50 }] });
  run.completedAt = '2026-10-09T00:00:05Z';
  run.resultJson = p.resultJson = JSON.stringify({ status: 'completed', sampleCount: n, totalSamples: n, seed: run.seed,
    configHash: run.configHash, configVersion: run.configVersion, degreeOfParallelism: run.degreeOfParallelism,
    streamScheme: run.streamScheme, rtp: mean, stdErr: p.stdErr, volatility: n > 1 ? p.volatility : null,
    hitFrequency: mean, maxWin: p.maxWin, adaptiveHistogram: p.histogram });
  const modelHash = 'b'.repeat(64);
  return { modelHash, evidence: { run, inputVerified: true, computedConfigHash: run.configHash,
    model: { name: 'Bernoulli', modelHash, targetRtp: null, winCap: 10000 }, pinnedConfig: { schemaVersion: '1.0.0', nodes: [], edges: [] } } };
}

test('Overview evidence exposes the independently derived sample interval and current pinned source', () => {
  const value = lookup(), result = verifiedOverviewRun(value)!;
  expect(result.progress.runningRtp).toBe(.5);
  expect(result.progress.sampleCount).toBe(100);
  expect(result.rtpInterval![0]).toBeCloseTo(.5 - 1.96 * Math.sqrt(.25 / 99), 14);
  expect(result.rtpInterval![1]).toBeCloseTo(.5 + 1.96 * Math.sqrt(.25 / 99), 14);
  expect(result.evidence.run.configHash).toBe(value.evidence.computedConfigHash);
  expect(verifiedOverviewRun({ modelHash: value.modelHash, evidence: null })).toBeNull();
});

test('Overview refuses unverified, mismatched, partial and inconsistent saved results', () => {
  const mutations = [
    (value: ReturnType<typeof lookup>) => { value.evidence.inputVerified = false; },
    (value: ReturnType<typeof lookup>) => { value.modelHash = 'c'.repeat(64); },
    (value: ReturnType<typeof lookup>) => { value.evidence.computedConfigHash = 'c'.repeat(64); },
    (value: ReturnType<typeof lookup>) => { value.evidence.run.status = 'cancelled'; },
    (value: ReturnType<typeof lookup>) => { value.evidence.run.progress!.runningRtp = .9; },
    (value: ReturnType<typeof lookup>) => { value.evidence.run.resultJson = '{}'; },
    (value: ReturnType<typeof lookup>) => { value.evidence.run.completedAt = 'invalid date'; },
  ];
  for (const mutate of mutations) { const value = lookup(); mutate(value); expect(() => verifiedOverviewRun(value)).toThrow(); }
  for (const value of [null, {}, { modelHash: 'bad', evidence: null }, { modelHash: 'a'.repeat(64), evidence: {} }])
    expect(() => verifiedOverviewRun(value)).toThrow();
});

test('One actual zero payout stays visible with N=1 and no uncertainty claim', () => {
  const result = verifiedOverviewRun(lookup(true))!;
  expect(result.progress.runningRtp).toBe(0);
  expect(result.progress.sampleCount).toBe(1);
  expect(result.rtpInterval).toBeNull();
});
