import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { startPinnedSimulation } from '../../hooks/useSimulation';
import type { Reference, RunEvidence } from '../../lib/results/model';
export function RunLaunchDialog({ evidence, reference, replay, plannedSamples, close }: {
  evidence: RunEvidence; reference?: Reference; replay: boolean; plannedSamples?: number; close(): void;
}) {
  const ref = useRef<HTMLDialogElement>(null), navigate = useNavigate();
  const [seed, setSeed] = useState(replay ? evidence.run.seed : crypto.getRandomValues(new Uint32Array(1))[0]);
  const [samples, setSamples] = useState(replay ? evidence.run.progress?.totalSamples ?? 100000 : Math.min(10_000_000, plannedSamples ?? 100000));
  const [workers, setWorkers] = useState(evidence.run.degreeOfParallelism), [busy, setBusy] = useState(false), [error, setError] = useState('');
  useEffect(() => { const dialog = ref.current!; dialog.showModal(); return () => dialog.close(); }, []);
  return <dialog ref={ref} className="results-launch-dialog" onCancel={event => { if (busy) event.preventDefault(); else close(); }} onClose={close} aria-labelledby="results-launch-title"><form onSubmit={async event => {
    event.preventDefault(); setBusy(true); setError('');
    try {
      await startPinnedSimulation({ configId: evidence.run.configId, configVersion: evidence.run.configVersion, configHash: evidence.run.configHash,
        model: evidence.model.name, target: evidence.model.targetRtp, seed, samples, workers,
        measurements: evidence.run.measurements, measurementHash: evidence.run.measurementHash,
        reference: reference?.rtp, referenceNote: reference?.note }); close(); navigate('/simulate');
    } catch (err) { setError(err instanceof Error ? err.message : 'Unable to launch the run.'); setBusy(false); }
  }}><header><h2 id="results-launch-title">{replay ? 'Replay pinned run' : 'Run the pinned model'}</h2><button type="button" className="btn" aria-label="Close run dialog" disabled={busy} onClick={close}>✕</button></header><p>Uses saved config version {evidence.run.configVersion}. Editor changes and newer versions do not affect this launch.</p>
    <label>Complete rounds<input aria-label="Pinned run rounds" type="number" min="1" max="10000000" step="1" value={samples} onChange={e => setSamples(Number(e.target.value))} disabled={busy} required /></label>
    <label>Seed<input aria-label="Pinned run seed" type="number" step="1" value={seed} onChange={e => setSeed(Number(e.target.value))} disabled={busy} required /></label>
    <label>Workers<select aria-label="Pinned run workers" value={workers} onChange={e => setWorkers(Number(e.target.value))} disabled={busy}>{[1, 2, 3, 4].map(n => <option key={n} value={n}>{n}</option>)}</select></label>
    {!replay && seed === evidence.run.seed && <p className="results-notice">This seed overlaps the selected run. Choose a different seed for an independent sample.</p>}
    {error && <p className="results-error" role="alert">{error}</p>}
    <footer><button type="button" className="btn" onClick={close} disabled={busy}>Cancel</button><button className="btn primary" type="submit" disabled={busy}>{busy ? 'Queuing…' : 'Start pinned run'}</button></footer>
  </form></dialog>;
}
