import type { MeasurementSnapshot } from './model';
import { validAnalysis } from './validation';
export interface ExecutionOptions { samplingEngine?: 'auto' | 'reference'; regime: 'independentRounds' | 'persistent' | 'sessions'; persistentKeys: string[]; sessionLength: number; initialBankroll: number; wager: number; featureMetricId?: string | null; sessionStop?: 'fixedHorizon' | 'ruin' | 'profitTarget' | 'lossLimit' | 'featureFirst'; stopThreshold?: number; auditRandomStreams?: boolean }
export const defaultExecution = (): ExecutionOptions => ({ samplingEngine: 'auto', regime: 'independentRounds', persistentKeys: [], sessionLength: 100, initialBankroll: 100, wager: 1 });
export interface ExecutionSummary {
  sessionStop?: string; plannedRoundSlots?: number; randomStreams?: { expectedStreams: number; scheduledInitialWordCollisionProbability: number; collisionCandidatePairs: number; streams: number; complete: boolean; nonemptyStreams: number; comparablePrefixes: number; duplicates: { firstStream: number; otherStream: number; kind: string }[]; idealIndependentPrefixCollisionUpperBound: number; calibration: string; detail: string } | null;
  monetaryAccounting?: string | null;
  samplingEngine?: string; regime: string; attemptedRounds: number; completedRounds: number; interruptedRounds: number; cancelledRounds: number; failedRounds: number;
  completedSessions: number; interruptedSessions: number; carriesState: boolean; stateResetPolicy: string; sessionPolicy: string; sessionMetrics: MeasurementSnapshot[];
  loopTerminations?: { nodeId: string; completedInvocations: number; modelLimitCompletions: number; conditionCompletions: number; totalIterations: number; minimumIterations: number; maximumIterations: number; exitReasons?: Record<string, number> | null }[];
  loopTerminationsComplete?: boolean;
}
export function sameExecution(a?: ExecutionOptions | null, b?: ExecutionOptions | null, includeEngine = true): boolean {
  const canonical = (value?: ExecutionOptions | null) => { const v = value ?? defaultExecution(); return JSON.stringify([v.regime, v.persistentKeys, v.sessionLength, v.initialBankroll, v.wager, v.featureMetricId ?? null, includeEngine ? v.samplingEngine ?? 'auto' : null, v.sessionStop ?? 'fixedHorizon', v.stopThreshold ?? 10, v.auditRandomStreams ?? false]); };
  return canonical(a) === canonical(b);
}
export function validateExecution(value: ExecutionOptions, rounds: number, workers: number): void {
  if (!['fixedHorizon', 'ruin', 'profitTarget', 'lossLimit', 'featureFirst'].includes(value.sessionStop ?? 'fixedHorizon') || value.sessionStop && value.sessionStop !== 'fixedHorizon' && value.regime !== 'sessions' || value.sessionStop === 'featureFirst' && !value.featureMetricId || !Number.isFinite(value.stopThreshold ?? 10) || (value.stopThreshold ?? 10) <= 0) throw new Error('Choose a session stop rule, a positive threshold and a feature metric for feature-first stopping.');
  if (value.auditRandomStreams && (value.regime === 'sessions' ? rounds / value.sessionLength : Math.ceil(rounds / 65536)) > 65536) throw new Error('Complete raw RNG audits support at most 65,536 logical streams.');
  if (!['auto', 'reference'].includes(value.samplingEngine ?? 'auto')) throw new Error('Choose automatic compiled sampling or the canonical reference interpreter.');
  if (!['independentRounds', 'persistent', 'sessions'].includes(value.regime) || !Array.isArray(value.persistentKeys)
    || value.persistentKeys.length > 64 || value.persistentKeys.some(k => !k.trim() || k.length > 128) || new Set(value.persistentKeys).size !== value.persistentKeys.length) throw new Error('Choose an execution regime and distinct persistent state keys.');
  if (value.featureMetricId != null && (value.regime !== 'sessions' || typeof value.featureMetricId !== 'string' || value.featureMetricId.length < 1 || value.featureMetricId.length > 64)) throw new Error('Feature waiting requires session execution and a pinned round-activation metric ID.');
  if (value.regime === 'independentRounds' && value.persistentKeys.length || value.regime === 'persistent' && workers !== 1) throw new Error('Independent rounds reset state; a persistent trajectory needs one worker.');
  if (!Number.isInteger(value.sessionLength) || value.sessionLength < 1 || value.sessionLength > 65536 || value.regime === 'sessions' && (rounds % value.sessionLength || rounds / value.sessionLength > 10_000_000)) throw new Error('Use complete sessions with a horizon of 1–65,536 rounds and at most 10,000,000 sessions.');
  if (!Number.isFinite(value.initialBankroll) || value.initialBankroll < 0 || value.initialBankroll > 1e15 || !Number.isFinite(value.wager) || value.wager <= 0 || value.wager > 1e12) throw new Error('Use a finite nonnegative bankroll and positive external wager.');
}
export function validExecutionSummary(value: unknown, completed: number): value is ExecutionSummary {
  if (!value || typeof value !== 'object') return false;
  const v = value as ExecutionSummary, count = (x: number) => Number.isSafeInteger(x) && x >= 0;
  if (v.monetaryAccounting != null && (v.regime !== 'sessions' || v.monetaryAccounting !== 'decimal-roundtrip-v1')) return false;
  if (v.samplingEngine !== undefined && !['compiled-sampling-plan', 'reference-interpreter', 'unspecified'].includes(v.samplingEngine)) return false;
  if (!['independentRounds', 'persistent', 'sessions'].includes(v.regime) || v.completedRounds !== completed
    || ![v.attemptedRounds, v.interruptedRounds, v.cancelledRounds, v.failedRounds, v.completedSessions, v.interruptedSessions].every(count)
    || v.attemptedRounds < completed + v.interruptedRounds || v.interruptedRounds !== v.cancelledRounds + v.failedRounds
    || typeof v.carriesState !== 'boolean' || typeof v.stateResetPolicy !== 'string' || typeof v.sessionPolicy !== 'string'
    || !Array.isArray(v.sessionMetrics) || v.sessionMetrics.length > 20) return false;
  if (v.sessionStop !== undefined && !['fixedHorizon', 'ruin', 'profitTarget', 'lossLimit', 'featureFirst'].includes(v.sessionStop) || v.plannedRoundSlots !== undefined && (!count(v.plannedRoundSlots) || v.plannedRoundSlots < completed)) return false;
  if (v.randomStreams != null) {
    const r = v.randomStreams;
    if (!r || ![r.streams, r.nonemptyStreams, r.comparablePrefixes, r.expectedStreams, r.collisionCandidatePairs].every(count) || r.streams > 65536 || r.streams > r.expectedStreams || ![0, 1].includes(r.scheduledInitialWordCollisionProbability) || r.nonemptyStreams > r.streams || r.comparablePrefixes > r.nonemptyStreams
      || typeof r.complete !== 'boolean' || !Number.isFinite(r.idealIndependentPrefixCollisionUpperBound) || r.idealIndependentPrefixCollisionUpperBound < 0 || r.idealIndependentPrefixCollisionUpperBound > 1
      || typeof r.calibration !== 'string' || r.calibration.length > 2048 || typeof r.detail !== 'string' || r.detail.length > 2048 || !Array.isArray(r.duplicates) || r.duplicates.length > 256
      || !r.duplicates.every(d => d && count(d.firstStream) && count(d.otherStream) && d.firstStream !== d.otherStream && ['identical-initial-state', 'identical-first-four-raw-words', 'identical-length-and-sha256-transcript'].includes(d.kind))) return false;
  }
  const ids = new Set();
  if (v.loopTerminations !== undefined) {
    if (!Array.isArray(v.loopTerminations) || v.loopTerminations.length > 256 || typeof v.loopTerminationsComplete !== 'boolean') return false;
    const loops = new Set();
    for (const l of v.loopTerminations) {
      if (!l || typeof l.nodeId !== 'string' || loops.has(l.nodeId) || ![l.completedInvocations, l.modelLimitCompletions, l.conditionCompletions, l.totalIterations, l.minimumIterations, l.maximumIterations].every(count)
        || l.completedInvocations === 0 || l.completedInvocations !== l.modelLimitCompletions + l.conditionCompletions || l.maximumIterations < l.minimumIterations
        || l.totalIterations < l.minimumIterations * l.completedInvocations || l.totalIterations > l.maximumIterations * l.completedInvocations) return false;
      if (l.exitReasons != null && (Object.keys(l.exitReasons).some(k => !['condition', 'modelLimit', 'payoutCap', 'authoredStop', 'resourceExpiry'].includes(k)) || !Object.values(l.exitReasons).every(count) || Object.values(l.exitReasons).reduce((a, b) => a + b, 0) !== l.completedInvocations)) return false;
      loops.add(l.nodeId);
    }
  }
  return v.sessionMetrics.every(m => {
    if (!m || typeof m.id !== 'string' || ids.has(m.id) || !count(m.count) || m.count > v.completedSessions || m.observations !== v.completedSessions || !count(m.errors) || !count(m.excluded) || m.count + m.excluded + m.errors !== m.observations || !validAnalysis(m.analysis, m.count, m.mean, m.sum)) return false;
    ids.add(m.id); return true;
  });
}

/** Optional stopping and carried state invalidate ordinary independent paid-round inference. */
export function allowsRoundInference(summary?: Pick<ExecutionSummary, 'carriesState' | 'sessionStop'> | null): boolean {
  return !summary?.carriesState && (!summary?.sessionStop || summary.sessionStop === 'fixedHorizon');
}
