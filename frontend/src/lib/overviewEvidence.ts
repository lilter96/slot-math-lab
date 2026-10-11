import { validateRunEvidence } from './results/evidence';
import { inspectRun, interval } from './results/model';

/** The server fingerprints the submitted model with its canonical serializer.
 * Document ID is excluded; every other persisted model field must match. */
export function verifiedOverviewRun(value: unknown) {
  if (!value || typeof value !== 'object' || !('modelHash' in value) || typeof value.modelHash !== 'string'
    || !/^[a-f0-9]{64}$/.test(value.modelHash) || !('evidence' in value)) throw new Error('The saved-model lookup returned invalid evidence.');
  if (value.evidence === null) return null;
  const raw = value.evidence as { run?: { id?: string } };
  if (typeof raw?.run?.id !== 'string') throw new Error('The saved run identity is missing.');
  const evidence = validateRunEvidence(raw, raw.run.id);
  const progress = evidence.run.progress;
  if (!evidence.inputVerified || evidence.model.modelHash !== value.modelHash || !inspectRun(evidence.run).complete
    || !progress || progress.sampleCount < 1 || !evidence.run.completedAt || !Number.isFinite(Date.parse(evidence.run.completedAt)))
    throw new Error('The saved result could not be verified for the current model.');
  return { evidence, progress, rtpInterval: interval(progress) };
}
