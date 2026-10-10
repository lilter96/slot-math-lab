import type { MeasurementSnapshot } from './model';
import { chartMeasurements, scalarMeasurements, type MeasurementTrend } from './trends';
import type { LiveProgress, RunSnapshot } from '../realtime/runProtocol';

export { chartMeasurements } from './trends';
const progressCheckpoint = (p: LiveProgress | null | undefined) => p == null ? p : { ...p, execution: p.execution ? { ...p.execution, sessionMetrics: [] } : p.execution, measurements: scalarMeasurements(p.measurements), resultJson: null };
const runCheckpoint = (run: RunSnapshot) => ({ ...run, progress: progressCheckpoint(run.progress), resultJson: null });

/** Server snapshots retain full evidence. The browser keeps identity, the exact pinned plan,
/// scalar last-known observations and a bounded trend. A fresh HTTP snapshot enriches the
/// final report after reload; cached partial evidence is never presented as a final report. */
export function serializeCheckpoint<T extends { run: RunSnapshot | null; progress: LiveProgress | null; history: RunSnapshot[]; points: { measurements?: (MeasurementSnapshot | MeasurementTrend)[] }[] }>(session: T): string {
  const checkpoint = { ...session, checkpointFormat: 4, run: session.run ? runCheckpoint(session.run) : null,
    progress: progressCheckpoint(session.progress), history: session.history.filter(r => r.id !== session.run?.id).map(runCheckpoint),
    points: session.points.map(point => ({ ...point, measurements: chartMeasurements(point.measurements) })) };
  let json = JSON.stringify(checkpoint);
  while (json.length > 1_500_000 && checkpoint.history.length) { checkpoint.history.pop(); json = JSON.stringify(checkpoint); }
  while (json.length > 1_500_000 && checkpoint.points.length > 2) { checkpoint.points.splice(0, checkpoint.points.length, ...checkpoint.points.filter((_, i, values) => i % 2 === 0 || i === values.length - 1)); json = JSON.stringify(checkpoint); }
  if (json.length > 1_500_000) throw new Error('Pinned run checkpoint exceeds the browser storage budget. Server evidence remains available.');
  return json;
}
