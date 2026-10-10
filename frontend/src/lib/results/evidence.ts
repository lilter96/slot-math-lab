import { samePlan } from '../measurements/model';
import { restoreTrendPoints, type MeasurementTrendPoint } from '../measurements/trends';
import { snapshotDecision, validSnapshot, type RunSnapshot } from '../realtime/runProtocol';
import { inspectRun, type RunEvidence } from './model';
import type { components } from '../../api/generated-types';

/** A diagnostic calculator may differ from the original producer. Its source
 * must still identify the exact producer and population of the saved sample. */
export function matchesEvidenceSource(source: components['schemas']['AccountingEvidenceSource'] | null | undefined, run: RunSnapshot): boolean {
  if (!source || source.configHash !== run.configHash || (source.measurementHash ?? null) !== (run.measurementHash ?? null)
    || source.sequence !== run.sequence || source.paidRounds !== run.progress?.sampleCount) return false;
  if (source.producer == null || run.runtimeProvenance == null) return source.producer == null && run.runtimeProvenance == null;
  return ['measurementContract', 'numericalMethods', 'framework', 'coreBinarySha256', 'apiBinarySha256'].every(key =>
    source.producer![key as 'framework'] === run.runtimeProvenance![key as 'framework']);
}

export function validateRunEvidence(value: unknown, id: string): RunEvidence {
  if (!value || typeof value !== 'object') throw new Error('The server returned an invalid evidence record.');
  const evidence = value as RunEvidence;
  if (!validSnapshot(evidence.run) || evidence.run.id !== id || !evidence.model || typeof evidence.model.name !== 'string'
    || typeof evidence.inputVerified !== 'boolean') throw new Error('The server returned an invalid or different run evidence record.');
  if (evidence.inputVerified && (!evidence.pinnedConfig || typeof evidence.pinnedConfig !== 'object' || Array.isArray(evidence.pinnedConfig)
    || evidence.computedConfigHash !== evidence.run.configHash || !/^[a-f0-9]{64}$/.test(evidence.model.modelHash ?? '')))
    throw new Error('Pinned input verification is inconsistent.');
  return evidence;
}

/** Export one authoritative population. Never combine an older browser result
 * with newer server metadata, or substitute a partial file on a failed read. */
export function simulationEvidence(value: unknown, expected: RunSnapshot, context: {
  points: MeasurementTrendPoint[]; reference: number | null; referenceNote: string; dashboard: unknown;
}) {
  const evidence = validateRunEvidence(value, expected.id);
  if (!evidence.inputVerified) throw new Error('The pinned model could not be verified. Restore its saved version before exporting evidence.');
  const decision = snapshotDecision(expected, expected.progress ?? null, evidence.run);
  if (!['accept', 'duplicate'].includes(decision) || !samePlan(expected.measurements ?? [], evidence.run.measurements ?? []))
    throw new Error('The evidence changed its pinned inputs, producer identity or observation sequence. Retry after the run has synchronized.');
  const { run } = evidence, integrity = inspectRun(run);
  if (integrity.issues.length) throw new Error('The server evidence is inconsistent: ' + integrity.issues[0]);
  return { schemaVersion: 'slotmath.simulation.v2', exportedAt: new Date().toISOString(), run, progress: run.progress,
    convergence: restoreTrendPoints(context.points).filter(point => point.n <= run.progress!.sampleCount),
    convergenceSource: 'Bounded browser chart history; authoritative aggregates are in progress.',
    targetRtp: evidence.model.targetRtp, exactReference: context.reference, referenceNote: context.referenceNote,
    pinnedGraph: { config: evidence.pinnedConfig, inputVerified: true, computedConfigHash: evidence.computedConfigHash,
      configId: run.configId, version: run.configVersion, modelHash: evidence.model.modelHash },
    diagnostics: evidence.diagnostics ?? [], integrity,
    dashboard: context.dashboard, dashboardSource: 'Current browser presentation; pinned collection definitions are in run.measurements.' };
}
