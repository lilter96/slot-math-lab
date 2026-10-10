import type { MeasurementDefinition, MeasurementSnapshot } from '../measurements/model';
import { LIMITS } from '../limits';
import { validProfile, sameProfile, type VerificationProfile } from '../measurements/profile';
import { validAnalysis, validWitnesses } from '../measurements/validation';
import { validExecutionSummary, validateExecution, sameExecution, type ExecutionOptions, type ExecutionSummary } from '../measurements/execution';
export type RunStatus = 'pending' | 'running' | 'cancelling' | 'completed' | 'cancelled' | 'failed';
export interface LiveProgress {
  execution?: ExecutionSummary | null;
  measurements?: MeasurementSnapshot[]; measurementHash?: string | null;
  streamEpoch?: string;
  runId: string; sequence: number; status: RunStatus; sampleCount: number; totalSamples: number;
  runningRtp: number; stdErr: number; elapsedMs: number; hitFrequency: number; nonZeroCount: number;
  volatility: number; maxWin: number; capHits: number; histogram: { lo: number; hi: number | null; count: number }[];
  resultJson?: string | null; completedAt?: string | null;
}
export interface RunSnapshot {
  verificationProfile?: VerificationProfile | null; verificationProfileHash?: string | null;
  runtimeProvenance?: { measurementContract: string; numericalMethods: string; framework: string; coreBinarySha256: string | null; apiBinarySha256: string | null } | null;
  execution?: ExecutionOptions | null;
  measurements?: MeasurementDefinition[]; measurementHash?: string | null;
  streamEpoch?: string;
  id: string; sequence?: number; seed: number; configId: string; configHash: string; configVersion: number;
  degreeOfParallelism: number; streamScheme: string; status: RunStatus; createdAt: string;
  progress?: LiveProgress; resultJson?: string | null; completedAt?: string | null;
}
export function terminal(status?: string): boolean { return ['completed', 'cancelled', 'failed'].includes(status ?? ''); }
const statuses = new Set(['pending', 'running', 'cancelling', 'completed', 'cancelled', 'failed']);
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object';
const uint = (v: unknown): v is number => typeof v === 'number' && Number.isSafeInteger(v) && v >= 0;
const nonnegative = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v) && v >= 0;

/** Each update is a complete cumulative snapshot. No deltas need replaying. */
export function validProgress(value: unknown): value is LiveProgress {
  if (object(value) && value.streamEpoch !== undefined && (typeof value.streamEpoch !== 'string' || !/^[a-f0-9]{32}$/.test(value.streamEpoch))) return false;
  if (!object(value) || typeof value.runId !== 'string' || !uint(value.sequence) || !uint(value.sampleCount)
    || !uint(value.totalSamples) || value.sampleCount > value.totalSamples || !statuses.has(String(value.status))) return false;
  for (const field of ['runningRtp', 'stdErr', 'elapsedMs', 'hitFrequency', 'volatility', 'maxWin'])
    if (!nonnegative(value[field])) return false;
  if ((value.hitFrequency as number) > 1 || !uint(value.nonZeroCount) || value.nonZeroCount > value.sampleCount
    || !uint(value.capHits) || value.capHits > value.sampleCount || !Array.isArray(value.histogram)) return false;
  if (value.execution != null && !validExecutionSummary(value.execution, value.sampleCount)) return false;
  let total = 0;
  for (const bin of value.histogram) {
    if (!object(bin) || !nonnegative(bin.lo) || !uint(bin.count) || (bin.hi !== null && (!nonnegative(bin.hi) || bin.hi <= bin.lo))) return false;
    total += bin.count;
  }
  if (total !== value.sampleCount) return false;
  if (value.measurementHash != null && (typeof value.measurementHash !== 'string' || !/^[a-f0-9]{64}$/.test(value.measurementHash))) return false;
  if (value.measurements !== undefined) {
    if (!Array.isArray(value.measurements) || value.measurements.length > 32) return false;
    const ids = new Set();
    for (const m of value.measurements) {
      if (!object(m) || typeof m.id !== 'string' || ids.has(m.id) || !uint(m.observations) || !uint(m.count) || !uint(m.excluded) || !uint(m.errors)
        || m.count + m.excluded + m.errors !== m.observations) return false;
      ids.add(m.id);
      if (!validWitnesses(m.witnesses)) return false;
      if (m.analysis != null && !validAnalysis(m.analysis, m.count, m.mean as number | null, m.sum as number | null)) return false;
      const interrupted = (m.analysis as MeasurementSnapshot['analysis'])?.interruptedLifecycle;
      if (interrupted && (interrupted.cancelled.interruptedRounds > ((value.execution as ExecutionSummary | undefined)?.cancelledRounds ?? 0)
        || interrupted.failed.interruptedRounds > ((value.execution as ExecutionSummary | undefined)?.failedRounds ?? 0))) return false;
      for (const field of ['min', 'max', 'mean', 'sum', 'stdDev']) {
        const v = m[field];
        if (v !== null && (typeof v !== 'number' || !Number.isFinite(v))) return false;
        if (field === 'stdDev' ? m.count < 2 ? v !== null : v === null || (v as number) < 0 : m.count === 0 ? v !== null : v === null) return false;
      }
      if (m.count > 0 && ((m.min as number) > (m.max as number) || (m.mean as number) < (m.min as number) - 1e-9 || (m.mean as number) > (m.max as number) + 1e-9)) return false;
      if (m.firstError != null && typeof m.firstError !== 'string') return false;
    }
  }
  if (value.resultJson != null) {
    if (!terminal(String(value.status)) || typeof value.resultJson !== 'string') return false;
    try {
      const result = JSON.parse(value.resultJson);
      if (!object(result) || (result.sampleCount !== undefined && result.sampleCount !== value.sampleCount)
        || (result.status !== undefined && result.status !== value.status)) return false;
    } catch { return false; }
  }
  return true;
}

export function validSnapshot(value: unknown): value is RunSnapshot {
  if (!object(value) || typeof value.id !== 'string' || typeof value.configId !== 'string'
    || typeof value.configHash !== 'string' || !/^[a-f0-9]{64}$/.test(value.configHash)
    || !uint(value.configVersion) || value.configVersion === 0 || typeof value.seed !== 'number' || !Number.isSafeInteger(value.seed)
    || !uint(value.degreeOfParallelism) || value.degreeOfParallelism < 1 || value.degreeOfParallelism > LIMITS.maxRunWorkers
    || typeof value.streamScheme !== 'string' || typeof value.createdAt !== 'string' || !statuses.has(String(value.status))) return false;
  if (value.runtimeProvenance != null && (!object(value.runtimeProvenance) || ['measurementContract', 'numericalMethods', 'framework'].some(key => typeof (value.runtimeProvenance as Record<string, unknown>)[key] !== 'string')
    || ['coreBinarySha256', 'apiBinarySha256'].some(key => { const hash = (value.runtimeProvenance as Record<string, unknown>)[key]; return hash != null && (typeof hash !== 'string' || !/^[a-f0-9]{64}$/.test(hash)); }))) return false;
  if (value.verificationProfile != null ? !validProfile(value.verificationProfile) || typeof value.verificationProfileHash !== 'string' || !/^[a-f0-9]{64}$/.test(value.verificationProfileHash) : value.verificationProfileHash != null) return false;
  if (value.sequence !== undefined && !uint(value.sequence)) return false;
  if (value.measurements !== undefined && (!Array.isArray(value.measurements) || value.measurements.length > 32
    || value.measurements.some(d => !object(d) || typeof d.id !== 'string' || typeof d.name !== 'string'))) return false;
  if ((value.measurementHash ?? null) !== (object(value.progress) ? value.progress.measurementHash ?? null : null)) return false;
  if (Array.isArray(value.measurements) && (value.measurements.length !== (object(value.progress) && Array.isArray(value.progress.measurements) ? value.progress.measurements.length : 0)
    || value.measurements.some((d, i) => (d as Record<string, unknown>).id !== (value.progress as LiveProgress).measurements?.[i].id))) return false;
  if (!validProgress(value.progress) || value.progress.runId !== value.id || value.progress.status !== value.status) return false;
  if (value.execution != null) {
    try { validateExecution(value.execution as ExecutionOptions, value.progress.totalSamples, value.degreeOfParallelism); }
    catch { return false; }
  }
  if (value.sequence !== undefined && value.sequence !== value.progress.sequence) return false;
  if (value.streamEpoch !== value.progress.streamEpoch) return false;
  return value.resultJson == null || value.resultJson === value.progress.resultJson;
}

export type Decision = 'accept' | 'duplicate' | 'stale' | 'wrong-run' | 'invalid' | 'epoch-change' | 'reset';
export function progressDecision(run: RunSnapshot | null, current: LiveProgress | null, next: unknown): Decision {
  if (!validProgress(next)) return 'invalid';
  if (!run || next.runId !== run.id) return 'wrong-run';
  if (next.status === 'completed' && next.resultJson && (JSON.parse(next.resultJson).verificationProfileHash ?? null) !== (run.verificationProfileHash ?? null)) return 'invalid';
  if ((next.measurementHash ?? null) !== (run.measurementHash ?? null) || (run.measurements?.length ?? 0) !== (next.measurements?.length ?? 0)
    || run.measurements?.some((d, i) => d.id !== next.measurements?.[i].id)) return 'invalid';
  if (run.streamEpoch && next.streamEpoch !== run.streamEpoch) return terminal(run.status) ? 'stale' : 'epoch-change';
  if (run.measurements?.some((d, i) => (d.options?.subject === 'round' || !d.nodeId && !['episode', 'transition'].includes(d.options?.subject ?? '')) && next.measurements?.[i].observations !== next.sampleCount)) return 'invalid';
  if (current?.measurements?.some((m, i) => next.measurements && ['count', 'observations', 'excluded', 'errors'].some(field => next.measurements![i][field as 'count'] < m[field as 'count']))) return 'stale';
  if (current?.measurements?.some((m, i) => {
    const before = m.analysis?.interruptedLifecycle, after = next.measurements?.[i].analysis?.interruptedLifecycle;
    return before && (!after || (['cancelled', 'failed', 'resourceExpiry'] as const).some(reason => {
      const b = before[reason], a = after[reason]; return b && (!a || (['interruptedRounds', 'entries', 'exits', 'openInstances'] as const).some(field => a[field] < b[field]) || !b.complete && a.complete); }));
  })) return 'stale';
  const revision = current?.sequence ?? run.sequence ?? -1;
  if (next.sequence < revision || next.sampleCount < (current?.sampleCount ?? 0)) return 'stale';
  if (terminal(run.status) && next.status !== run.status) return 'stale';
  if (run.status === 'cancelling' && (next.status === 'pending' || next.status === 'running')) return 'stale';
  if (run.status === 'running' && next.status === 'pending') return 'stale';
  if (next.sequence === revision && next.status !== run.status) return 'stale';
  return next.sequence === revision ? 'duplicate' : 'accept';
}

export function snapshotDecision(run: RunSnapshot | null, current: LiveProgress | null, next: unknown): Decision {
  if (!validSnapshot(next)) return 'invalid';
  if (!run || next.id !== run.id) return 'wrong-run';
  for (const field of ['configId', 'configHash', 'configVersion', 'seed', 'degreeOfParallelism', 'streamScheme', 'createdAt'] as const)
    if (next[field] !== run[field]) return 'invalid';
  if ((next.measurementHash ?? null) !== (run.measurementHash ?? null)) return 'invalid';
  if ((next.verificationProfileHash ?? null) !== (run.verificationProfileHash ?? null) || !sameProfile(run.verificationProfile, next.verificationProfile)) return 'invalid';
  if (!sameExecution(run.execution, next.execution) || ['measurementContract', 'numericalMethods', 'framework', 'coreBinarySha256', 'apiBinarySha256'].some(key => run.runtimeProvenance?.[key as 'framework'] !== next.runtimeProvenance?.[key as 'framework'])) return 'invalid';
  if (run.streamEpoch && next.streamEpoch !== run.streamEpoch) {
    if (terminal(run.status)) return 'stale';
    try { return next.status === 'failed' && JSON.parse(next.resultJson ?? '{}').code === 'RUN_INTERRUPTED' ? 'reset' : 'invalid'; }
    catch { return 'invalid'; }
  }
  return progressDecision(run, current, next.progress);
}
