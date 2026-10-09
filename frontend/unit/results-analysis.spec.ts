import { test, expect } from '@playwright/test';
import { assess, compareRuns, inspectRun, interval, planSamples, quantileBounds, tailBounds, wilson, type RunEvidence, type Reference } from '../src/lib/results/model';
import { evidenceBundle, reportCsv, reportHtml } from '../src/lib/results/export';
import { completed } from './realtime-fixtures';

// Independent weighted coin: P(0)=1/2, P(1)=3/8, P(3)=1/8.
// Sum/n=.75, sum of squares/n=1.5, unbiased variance=.9375*n/(n-1).
function evidence(n = 1000): RunEvidence {
  const run = completed(10, n), p = run.progress!;
  p.totalSamples = n;
  p.runningRtp = .75; p.nonZeroCount = n / 2; p.hitFrequency = .5;
  p.volatility = Math.sqrt(.9375 * n / (n - 1)); p.stdErr = p.volatility / Math.sqrt(n); p.maxWin = 3;
  p.histogram = [{ lo: 0, hi: 1, count: n / 2 }, { lo: 1, hi: 2, count: n * 3 / 8 }, { lo: 2, hi: 4, count: n / 8 }];
  const result = { status: 'completed', sampleCount: n, totalSamples: n, seed: 42, configHash: run.configHash, configVersion: 1,
    degreeOfParallelism: run.degreeOfParallelism, streamScheme: run.streamScheme,
    rtp: .75, volatility: p.volatility, stdErr: p.stdErr, hitFrequency: .5, maxWin: 3, adaptiveHistogram: p.histogram };
  run.resultJson = p.resultJson = JSON.stringify(result);
  return { run, model: { name: 'Coin', modelHash: 'b'.repeat(64), targetRtp: .75, winCap: 10000 },
    pinnedConfig: { name: 'Coin', nodes: [], edges: [] }, inputVerified: true, computedConfigHash: run.configHash };
}
function other(a: RunEvidence, id = 'second') {
  const b = structuredClone(a); b.run.id = b.run.progress!.runId = id; return b;
}
function refresh(a: RunEvidence) {
  const p = a.run.progress!, result = JSON.parse(a.run.resultJson!);
  Object.assign(result, { seed: a.run.seed, degreeOfParallelism: a.run.degreeOfParallelism, volatility: p.volatility, stdErr: p.stdErr });
  a.run.resultJson = p.resultJson = JSON.stringify(result);
}
test('coin uncertainty and sample planning agree with the independent moments', () => {
  const a = evidence(), p = a.run.progress!;
  expect(inspectRun(a.run).issues).toEqual([]);
  const half = 1.96 * Math.sqrt(.9375 / 999);
  expect(interval(p)![0]).toBeCloseTo(.75 - half, 14);
  expect(interval(p)![1]).toBeCloseTo(.75 + half, 14);
  expect(planSamples(p, .5)!.total).toBe(Math.ceil(1.96 ** 2 * (.9375 * 1000 / 999) / .005 ** 2));
  expect(planSamples(p, .00001)!.exceedsRunLimit).toBe(true);
  for (const t of [0, -1, NaN, Infinity]) expect(planSamples(p, t)).toBeNull();
  const hit = wilson(500, 1000)!; expect(hit[0]).toBeCloseTo(.469069, 5); expect(hit[1]).toBeCloseTo(.530931, 5);
  expect(wilson(0, 0)).toBeNull(); expect(wilson(2, 1)).toBeNull();
});
test('acceptance uses the whole interval and withholds verdicts for partial or unverified evidence', () => {
  expect(assess(evidence(1000000).run, .75, .5).kind).toBe('within');
  expect(assess(evidence().run, .75, .5).kind).toBe('inconclusive');
  expect(assess(evidence().run, .98, .5).kind).toBe('outside');
  expect(assess(evidence().run, null, .5).kind).toBe('unverified');
  expect(assess(evidence().run, .75, NaN).kind).toBe('insufficient');
  expect(assess(evidence().run, .75, .5, false).kind).toBe('unverified');
  const partial = evidence(); partial.run.status = partial.run.progress!.status = 'cancelled';
  partial.run.resultJson = partial.run.progress!.resultJson = '{"status":"cancelled","sampleCount":1000}';
  partial.run.progress!.totalSamples = 10000;
  expect(assess(partial.run, .75, 10).kind).toBe('partial');
  const zero = evidence(); zero.run.progress!.volatility = zero.run.progress!.stdErr = 0;
  refresh(zero);
  expect(assess(zero.run, .75, 10).title).toBe('Variance unresolved');
});
test('integrity catches conflicting terminal metrics, malformed JSON and overlapping histograms', () => {
  for (const patch of [{ seed: 43 }, { configHash: 'x' }, { rtp: 0 }, { volatility: 2 }, { adaptiveHistogram: [] }]) {
    const a = evidence(); a.run.resultJson = a.run.progress!.resultJson = JSON.stringify({ ...JSON.parse(a.run.resultJson!), ...patch });
    expect(inspectRun(a.run).issues.length).toBeGreaterThan(0);
    expect(assess(a.run, .75, 5).kind).toBe('invalid');
  }
  const a = evidence(); a.run.progress!.histogram[1].lo = .5;
  expect(inspectRun(a.run).issues).toContain('Histogram bins overlap or are not ordered.');
  const b = evidence(); b.run.progress!.hitFrequency = .6;
  expect(inspectRun(b.run).issues).toContain('Winning-round count differs from hit frequency.');
  b.run.resultJson = b.run.progress!.resultJson = '{'; expect(inspectRun(b.run).complete).toBe(false);
  const reordered = evidence(); const result = JSON.parse(reordered.run.resultJson!);
  result.adaptiveHistogram = result.adaptiveHistogram.map((bin: { lo: number; hi: number; count: number }) => ({ count: bin.count, hi: bin.hi, lo: bin.lo }));
  reordered.run.resultJson = reordered.run.progress!.resultJson = JSON.stringify(result);
  expect(inspectRun(reordered.run).issues).toEqual([]);
  const missing = evidence(); missing.run.resultJson = missing.run.progress!.resultJson = '{}';
  expect(inspectRun(missing.run).issues).toContain('The completed result is missing required metrics or provenance.');
});
test('percentiles remain bounded and tail counts never interpolate across bin boundaries', () => {
  const p = evidence().run.progress!;
  expect(quantileBounds(p, .5)).toEqual({ lo: 0, hi: 0 });
  expect(quantileBounds(p, .9)).toEqual({ lo: 2, hi: 3 });
  expect(quantileBounds(p, .99)).toEqual({ lo: 2, hi: 3 });
  expect(quantileBounds(p, 0)).toBeNull();
  expect(tailBounds(p, 2)).toMatchObject({ lower: 125, upper: 125 });
  expect(tailBounds(p, 3)).toMatchObject({ lower: 0, upper: 125 });
  expect(tailBounds(p, 100).zeroEventUpper95).toBeCloseTo(1 - .05 ** (1 / 1000), 14);
});
test('replay compares every statistic across worker counts and distinguishes correlated streams', () => {
  const a = evidence(), b = other(a); b.run.degreeOfParallelism = 4;
  refresh(b);
  expect(compareRuns(a, b).kind).toBe('replay-match');
  b.run.progress!.capHits = 1;
  expect(compareRuns(a, b).kind).toBe('replay-mismatch');
  const longer = other(evidence(2000)); expect(compareRuns(a, longer).kind).toBe('correlated');
  const mismatch = other(a); mismatch.model.modelHash = 'c'.repeat(64);
  expect(compareRuns(a, mismatch).kind).toBe('different');
  expect(compareRuns(a, a).kind).toBe('unavailable');
});
test('independent seeds receive a difference interval while uncertain variance does not', () => {
  const a = evidence(), b = other(a); b.run.seed = 43; refresh(b);
  const result = compareRuns(a, b); expect(result.kind).toBe('independent');
  expect(result.ci![0]).toBeCloseTo(-1.96 * Math.sqrt(2 * .9375 / 999), 14);
  b.run.progress!.volatility = b.run.progress!.stdErr = 0;
  refresh(b);
  expect(compareRuns(a, b).kind).toBe('insufficient');
});
test('portable evidence preserves pinned inputs and escapes HTML and spreadsheet model names', () => {
  const a = evidence(); a.model.name = '=SUM(1,2)\n<script>alert("x")</script>';
  const reference: Reference = { sourceHash: a.run.configHash, kind: 'ExactDistribution', rtp: .75, rational: '3/4', note: 'Full enumeration', calculatedAt: '2026-10-09T00:00:00Z' };
  const bundle = evidenceBundle(a, reference, .5);
  expect(bundle.evidence.pinnedConfig).toBe(a.pinnedConfig); expect(bundle.reference?.rational).toBe('3/4');
  expect(evidenceBundle(a, { ...reference, sourceHash: 'other' }, .5).reference).toBeNull();
  expect(reportCsv(a, { ...reference, sourceHash: 'other' })).not.toContain('3/4');
  expect(reportCsv(a, reference)).toContain('"\'=SUM(1,2)');
  const html = reportHtml(a, reference, .5); expect(html).not.toContain('<script>'); expect(html).toContain('&lt;script&gt;');
  expect(reportCsv(a, reference)).toContain('upstream cap events are not instrumented');
  expect(html).toContain(a.run.configHash); expect(html).toContain('75.000%');
  const noTarget = evidence(1000000); noTarget.model.targetRtp = null;
  const withReference = evidenceBundle(noTarget, reference, .5);
  expect(withReference.assessment.kind).toBe('within'); expect(withReference.assessment.targetSource).toBe('CalculatedReference');
  expect(reportHtml(noTarget, reference, .5)).toContain('Within tolerance');
});
