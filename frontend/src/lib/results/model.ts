import { sameExecution } from '../measurements/execution';
import { formatNumber, formatPercent } from '../numberFormat';
import { validSnapshot, terminal, type RunSnapshot, type LiveProgress } from '../realtime/runProtocol';
export interface RunModel { name: string; modelHash: string | null; targetRtp: number | null; winCap: number | null }
export interface RunSummary {
  id: string; configId: string; configVersion: number; configHash: string | null; model: RunModel;
  status: string; createdAt: string; completedAt: string | null; seed: number; degreeOfParallelism: number;
  streamScheme: string; sampleCount: number; totalSamples: number; rtp: number | null; stdErr: number | null; elapsedMs: number;
}
export interface RunPage { items: RunSummary[]; nextCursor: string | null; total: number; completed: number; partial: number; failed: number; active: number }
export interface DiagnosticArtifact { id: string; kind: string; createdAt: string; inputSha256: string; outputSha256: string; configHash: string | null; measurementHash: string | null; runtimeProvenance: { coreBinarySha256: string | null }; input: unknown; output: unknown }
export interface RunEvidence { run: RunSnapshot; model: RunModel; pinnedConfig: Record<string, unknown> | null; computedConfigHash: string | null; inputVerified: boolean; diagnostics?: DiagnosticArtifact[] }
export interface Reference {
  sourceHash: string; kind: 'ExactExpectation' | 'ExactDistribution' | 'ExactInterval' | 'Unavailable';
  rtp?: number; lower?: number; upper?: number; rational?: string; note: string; calculatedAt: string;
  components?: { name: string; rtp: number; rational: string }[]; proof?: Record<string, unknown>;
}
const finite = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v);
const close = (a: number, b: number) => Math.abs(a - b) <= 1e-12 * Math.max(1, Math.abs(a), Math.abs(b));
export function inspectRun(run: RunSnapshot) {
  const issues: string[] = [];
  if (!validSnapshot(run)) issues.push('The run snapshot has invalid or inconsistent fields.');
  let result: Record<string, unknown> | null = null;
  if (run.resultJson) {
    try {
      const parsed = JSON.parse(run.resultJson);
      if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error();
      result = parsed;
    } catch { issues.push('The persisted result is not valid JSON.'); }
  } else if (terminal(run.status)) issues.push('The terminal result has not been retrieved.');
  if (result && run.status === 'completed' && ['status', 'sampleCount', 'totalSamples', 'seed', 'configHash', 'configVersion',
    'degreeOfParallelism', 'streamScheme', 'rtp', 'stdErr', 'hitFrequency', 'maxWin', 'volatility', 'adaptiveHistogram'].some(key => result![key] === undefined))
    issues.push('The completed result is missing required metrics or provenance.');
  const p = run.progress;
  if (p?.sampleCount && !close(p.hitFrequency, p.nonZeroCount / p.sampleCount)) issues.push('Winning-round count differs from hit frequency.');
  if (p && p.sampleCount > 1 && !close(p.stdErr, p.volatility / Math.sqrt(p.sampleCount))) issues.push('Standard error differs from observed variance and sample count.');
  if (p) {
    for (let i = 0; i < p.histogram.length; i++) {
      const bin = p.histogram[i], previous = p.histogram[i - 1];
      if (previous && (previous.hi == null || previous.hi > bin.lo)) { issues.push('Histogram bins overlap or are not ordered.'); break; }
      if (bin.count && bin.lo > p.maxWin) { issues.push('A populated histogram bin exceeds the maximum observed payout.'); break; }
    }
  }
  if (result && p) {
    if (run.status === 'completed' && (result.verificationProfileHash ?? null) !== (run.verificationProfileHash ?? null))
      issues.push('Persisted verification profile identity differs from its predeclared run.');
    if (run.measurements?.length && (run.status === 'completed' || result.measurements !== undefined)) {
      if (result.measurementHash !== run.measurementHash || !Array.isArray(result.measurements)
        || result.measurements.length !== (p.measurements?.length ?? 0) || result.measurements.some((m, i) => !m ||
          ['id', 'observations', 'count', 'excluded', 'errors', 'min', 'max', 'mean', 'sum', 'stdDev', 'firstError'].some(key => m[key] !== p.measurements?.[i][key as 'id'])))
        issues.push('Persisted tracked measurements differ from the pinned run snapshot.');
    }
    for (const [key, expected] of [['sampleCount', p.sampleCount], ['totalSamples', p.totalSamples], ['status', run.status], ['seed', run.seed],
      ['configHash', run.configHash], ['configVersion', run.configVersion], ['degreeOfParallelism', run.degreeOfParallelism],
      ['streamScheme', run.streamScheme]] as const)
      if (result[key] !== undefined && result[key] !== expected) issues.push(`Persisted ${key} differs from the run snapshot.`);
    for (const [key, expected] of [['rtp', p.runningRtp], ['runningRtp', p.runningRtp], ['stdErr', p.stdErr], ['hitFrequency', p.hitFrequency], ['maxWin', p.maxWin], ['volatility', p.sampleCount > 1 ? p.volatility : null]] as const)
      if (result[key] !== undefined && (expected === null ? result[key] !== null : !finite(result[key]) || !close(result[key], expected))) issues.push(`Persisted ${key} differs from the observed metrics.`);
    if (result.adaptiveHistogram !== undefined && (!Array.isArray(result.adaptiveHistogram) || result.adaptiveHistogram.length !== p.histogram.length
      || result.adaptiveHistogram.some((bin, i) => !bin || ['lo', 'hi', 'count'].some(key => bin[key] !== p.histogram[i][key as 'lo' | 'hi' | 'count']))))
      issues.push('Persisted payout histogram differs from the run snapshot.');
  }
  if (run.status === 'completed' && (!p || p.sampleCount !== p.totalSamples)) issues.push('A completed run does not contain every requested round.');
  return { issues, result, complete: run.status === 'completed' && !!p && p.sampleCount === p.totalSamples && issues.length === 0,
    error: typeof result?.error === 'string' ? result.error : null };
}
export function interval(p?: LiveProgress) {
  if (!p || p.execution?.carriesState || p.sampleCount < 2 || !finite(p.stdErr) || p.stdErr <= 0) return null;
  return [p.runningRtp - 1.96 * p.stdErr, p.runningRtp + 1.96 * p.stdErr] as const;
}
export function wilson(successes: number, n: number) {
  if (!Number.isSafeInteger(n) || n < 1 || successes < 0 || successes > n) return null;
  const z2 = 1.96 ** 2, p = successes / n, d = 1 + z2 / n;
  const centre = (p + z2 / (2 * n)) / d, half = 1.96 * Math.sqrt(p * (1 - p) / n + z2 / (4 * n * n)) / d;
  return [Math.max(0, centre - half), Math.min(1, centre + half)] as const;
}
export function planSamples(p: LiveProgress | undefined, tolerancePp: number) {
  if (!p || p.execution?.carriesState || p.sampleCount < 2 || !finite(tolerancePp) || tolerancePp <= 0 || !finite(p.volatility) || p.volatility <= 0) return null;
  const estimated = Math.ceil((1.96 * p.volatility / (tolerancePp / 100)) ** 2);
  if (!Number.isSafeInteger(estimated)) return null;
  return { total: Math.max(2, estimated), additional: Math.max(0, estimated - p.sampleCount), exceedsRunLimit: estimated > 10_000_000 };
}
export function assess(run: RunSnapshot, target: number | null, tolerancePp: number, inputVerified = true) {
  const check = inspectRun(run), p = run.progress, ci = interval(p);
  if (!inputVerified) return { kind: 'unverified', title: 'Pinned input unverified', note: 'The original model is missing or its fingerprint differs. Acceptance checks are withheld.' };
  if (check.issues.length) return { kind: 'invalid', title: 'Evidence needs attention', note: check.issues[0] };
  if (!check.complete) return { kind: 'partial', title: terminal(run.status) ? 'Incomplete evidence' : 'Run in progress',
    note: 'Acceptance checks require a completed run. Partial observations remain available for diagnosis.' };
  if (p?.execution?.carriesState) return { kind: 'insufficient', title: 'Dependent paid rounds', note: 'This run retains state between rounds. Use independent complete-session evidence or a justified dependence-aware reference; the ordinary round interval is withheld.' };
  if (!p || p.execution?.carriesState || p.sampleCount < 2) return { kind: 'insufficient', title: 'Insufficient observations', note: 'At least two rounds are required to estimate sampling uncertainty.' };
  if (p.volatility === 0) return { kind: 'insufficient', title: 'Variance unresolved', note: 'No variation was observed. A zero estimated interval does not rule out rare payouts.' };
  if (target == null || !finite(target)) return { kind: 'unverified', title: 'No acceptance target', note: 'This pinned model has no authored target. Calculate its reference to interpret observed RTP.' };
  if (!finite(tolerancePp) || tolerancePp <= 0 || !ci) return { kind: 'insufficient', title: 'Set a valid tolerance', note: 'Tolerance must be a positive number of percentage points.' };
  const lo = target - tolerancePp / 100, hi = target + tolerancePp / 100;
  if (ci[0] >= lo && ci[1] <= hi) return { kind: 'within', title: 'Within tolerance', note: 'The approximate 95% interval fits inside the chosen acceptance band. This is a sampling check, not a proof of the payout model.' };
  if (ci[1] < lo || ci[0] > hi) return { kind: 'outside', title: 'Outside tolerance', note: 'The approximate 95% interval lies outside the chosen acceptance band. Review pinned inputs and the exact reference before drawing conclusions.' };
  return { kind: 'inconclusive', title: 'More precision needed', note: 'The approximate 95% interval crosses the acceptance band. The available sample cannot resolve this tolerance.' };
}
export function quantileBounds(p: LiveProgress, q: number) {
  if (!p.sampleCount || !finite(q) || q <= 0 || q > 1) return null;
  const rank = Math.ceil(q * p.sampleCount), zeros = p.sampleCount - p.nonZeroCount;
  if (rank <= zeros) return { lo: 0, hi: 0 };
  let cumulative = 0;
  for (const bin of p.histogram) {
    cumulative += bin.count;
    if (cumulative >= rank) return { lo: bin.lo, hi: Math.min(bin.hi ?? p.maxWin, p.maxWin) };
  }
  return null;
}
/** Counts crossing a bin boundary are unknown, never interpolated. */
export function tailBounds(p: LiveProgress, threshold: number) {
  let lower = 0, upper = 0;
  for (const bin of p.histogram) {
    if (bin.lo >= threshold) { lower += bin.count; upper += bin.count; }
    else if ((bin.hi ?? Infinity) > threshold && p.maxWin >= threshold) upper += bin.count;
  }
  return { lower, upper, zeroEventUpper95: upper === 0 && p.sampleCount && !p.execution?.carriesState ? -Math.expm1(Math.log(.05) / p.sampleCount) : null };
}
export function compareRuns(a: RunEvidence, b: RunEvidence) {
  const ap = a.run.progress, bp = b.run.progress;
  const delta = ap && bp && ap.sampleCount && bp.sampleCount ? ap.runningRtp - bp.runningRtp : null;
  if (a.run.id === b.run.id) return { kind: 'unavailable', title: 'Choose another run', note: 'A run cannot validate itself.', delta, ci: null };
  if (!a.inputVerified || !b.inputVerified || !a.model.modelHash || a.model.modelHash !== b.model.modelHash)
    return { kind: 'different', title: 'Different or unverified inputs', note: 'Metrics can be inspected side by side. An agreement verdict requires the same model fingerprint and verified pinned inputs.', delta, ci: null };
  if (!inspectRun(a.run).complete || !inspectRun(b.run).complete || !ap || !bp)
    return { kind: 'partial', title: 'Incomplete comparison', note: 'Partial, failed or inconsistent runs cannot establish a replay or sampling agreement.', delta, ci: null };
  if (!sameExecution(a.run.execution, b.run.execution, false) || a.run.streamScheme !== b.run.streamScheme)
    return { kind: 'correlated', title: 'Stream schemes differ', note: 'Stream compatibility is not established; no replay or independent-difference interval is claimed.', delta, ci: null };
  if (a.run.seed === b.run.seed) {
    if (a.run.runtimeProvenance?.coreBinarySha256 !== b.run.runtimeProvenance?.coreBinarySha256) return { kind: 'different', title: 'Algorithm artifacts differ', note: 'The saved runs used different Core binaries. Inspect effects side by side; an original-artifact replay verdict is withheld.', delta, ci: null };
    if (ap.sampleCount !== bp.sampleCount) return { kind: 'correlated', title: 'Overlapping seeded streams', note: 'The seeds match but lengths differ. These observations are correlated and must not be pooled or treated as independent.', delta, ci: null };
    const fields = ['sampleCount', 'runningRtp', 'stdErr', 'hitFrequency', 'nonZeroCount', 'volatility', 'maxWin', 'capHits'] as const;
    const sameMeasurementPlan = (a.run.measurementHash ?? null) === (b.run.measurementHash ?? null);
    const measurementMatch = !sameMeasurementPlan || JSON.stringify(ap.measurements ?? []) === JSON.stringify(bp.measurements ?? []);
    const sessionMatch = JSON.stringify(ap.execution?.sessionMetrics ?? []) === JSON.stringify(bp.execution?.sessionMetrics ?? []);
    const identical = sessionMatch && measurementMatch && fields.every(key => ap[key] === bp[key]) && JSON.stringify(ap.histogram) === JSON.stringify(bp.histogram);
    return { kind: identical ? 'replay-match' : 'replay-mismatch', title: identical ? 'Replay matches' : 'Replay mismatch',
      note: `Same model, seed, length and stream scheme. Comparing payout aggregates, histogram and complete-session evidence; elapsed time and worker count are excluded. ${sameMeasurementPlan ? 'Matching collection plans also compare tracked aggregates.' : 'Collection plans differ, so scoped measurements are not compared.'}`, delta, ci: null };
  }
  if (ap.execution?.carriesState || bp.execution?.carriesState || ap.sampleCount < 2 || bp.sampleCount < 2 || ap.volatility === 0 || bp.volatility === 0)
    return { kind: 'insufficient', title: 'Variance unresolved', note: 'The available runs cannot estimate the uncertainty of their difference.', delta, ci: null };
  const half = 1.96 * Math.hypot(ap.stdErr, bp.stdErr), ci = [delta! - half, delta! + half] as const;
  return { kind: 'independent', title: ci[0] <= 0 && ci[1] >= 0 ? 'Difference includes zero' : 'Difference excludes zero',
    note: 'Approximate 95% interval assuming independent seeded runs of the same model. This does not prove that the implementation is correct.', delta, ci };
}
export const percent = formatPercent;
export const pp = (v: number | null | undefined) => v != null && finite(v) ? `${v >= 0 ? '+' : ''}${(v * 100).toFixed(3)} pp` : '—';
export const number = (v: number | null | undefined) => formatNumber(v, 2);
