import type { MeasurementSnapshot } from './model';
import { validAnalysis } from './validation';
export interface ExecutionOptions { samplingEngine?: 'auto' | 'reference'; regime: 'independentRounds' | 'persistent' | 'sessions'; persistentKeys: string[]; sessionLength: number; initialBankroll: number; wager: number; featureMetricId?: string | null }
export const defaultExecution = (): ExecutionOptions => ({ samplingEngine: 'auto', regime: 'independentRounds', persistentKeys: [], sessionLength: 100, initialBankroll: 100, wager: 1 });
export interface ExecutionSummary {
  samplingEngine?: string; regime: string; attemptedRounds: number; completedRounds: number; interruptedRounds: number; cancelledRounds: number; failedRounds: number;
  completedSessions: number; interruptedSessions: number; carriesState: boolean; stateResetPolicy: string; sessionPolicy: string; sessionMetrics: MeasurementSnapshot[];
  loopTerminations?: { nodeId: string; completedInvocations: number; modelLimitCompletions: number; conditionCompletions: number; totalIterations: number; minimumIterations: number; maximumIterations: number }[];
  loopTerminationsComplete?: boolean;
}
export function sameExecution(a?: ExecutionOptions | null, b?: ExecutionOptions | null, includeEngine = true): boolean {
  const canonical = (value?: ExecutionOptions | null) => { const v = value ?? defaultExecution(); return JSON.stringify([v.regime, v.persistentKeys, v.sessionLength, v.initialBankroll, v.wager, v.featureMetricId ?? null, includeEngine ? v.samplingEngine ?? 'auto' : null]); };
  return canonical(a) === canonical(b);
}
export function validateExecution(value: ExecutionOptions, rounds: number, workers: number): void {
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
  if (v.samplingEngine !== undefined && !['compiled-sampling-plan', 'reference-interpreter', 'unspecified'].includes(v.samplingEngine)) return false;
  if (!['independentRounds', 'persistent', 'sessions'].includes(v.regime) || v.completedRounds !== completed
    || ![v.attemptedRounds, v.interruptedRounds, v.cancelledRounds, v.failedRounds, v.completedSessions, v.interruptedSessions].every(count)
    || v.attemptedRounds < completed + v.interruptedRounds || v.interruptedRounds !== v.cancelledRounds + v.failedRounds
    || typeof v.carriesState !== 'boolean' || typeof v.stateResetPolicy !== 'string' || typeof v.sessionPolicy !== 'string'
    || !Array.isArray(v.sessionMetrics) || v.sessionMetrics.length > 12) return false;
  const ids = new Set();
  if (v.loopTerminations !== undefined) {
    if (!Array.isArray(v.loopTerminations) || v.loopTerminations.length > 256 || typeof v.loopTerminationsComplete !== 'boolean') return false;
    const loops = new Set();
    for (const l of v.loopTerminations) {
      if (!l || typeof l.nodeId !== 'string' || loops.has(l.nodeId) || ![l.completedInvocations, l.modelLimitCompletions, l.conditionCompletions, l.totalIterations, l.minimumIterations, l.maximumIterations].every(count)
        || l.completedInvocations === 0 || l.completedInvocations !== l.modelLimitCompletions + l.conditionCompletions || l.maximumIterations < l.minimumIterations
        || l.totalIterations < l.minimumIterations * l.completedInvocations || l.totalIterations > l.maximumIterations * l.completedInvocations) return false;
      loops.add(l.nodeId);
    }
  }
  return v.sessionMetrics.every(m => {
    if (!m || typeof m.id !== 'string' || ids.has(m.id) || !count(m.count) || m.count > v.completedSessions || m.observations !== v.completedSessions || !count(m.errors) || !count(m.excluded) || m.count + m.excluded + m.errors !== m.observations || !validAnalysis(m.analysis, m.count, m.mean, m.sum)) return false;
    ids.add(m.id); return true;
  });
}
