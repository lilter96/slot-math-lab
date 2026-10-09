import { calculationRequest } from '../../lib/measurements/requests';
import { useEffect, useRef, useState } from 'react';
import type { MeasurementSnapshot } from '../../lib/measurements/model';
import { downloadReport } from '../../lib/results/export';

export function MeasurementWitnesses({ runId, value }: { runId: string; value?: MeasurementSnapshot }) {
  const [busy, setBusy] = useState(false), [error, setError] = useState(''), [result, setResult] = useState<unknown>(null), [status, setStatus] = useState('');
  const controller = useRef<AbortController | null>(null);
  useEffect(() => () => controller.current?.abort(), [runId]);
  if (!value?.witnesses?.length) return null;
  return <details className="measurement-witnesses"><summary>Reproducible witnesses · {value.witnesses.length} bounded examples</summary>
    <p>These deterministic round and observation coordinates retain the first invalid subject, duplicate award, unexpected support and sampled extrema. Extrema are observed examples. Replay reconstructs the logical stream prefix and retained state from the saved graph, plan and seed; it requires the original Core binary.</p>
    <div className="results-table-scroll"><table className="results-table"><thead><tr><th>Reason</th><th>Round index · zero based</th><th>Point / ordinal</th><th>Value / pair</th><th>Detail</th><th>Replay</th></tr></thead><tbody>{value.witnesses.map(w => <tr key={w.kind}><td>{w.kind}</td><td>{w.roundIndex.toLocaleString()}</td><td>{w.nodeId ?? 'Settled round'} / {w.observationOrdinal}</td><td>{w.value ?? '—'} / {w.pair ?? '—'}</td><td>{w.group}{w.detail && <p>{w.detail}</p>}</td><td><button type="button" className="btn" disabled={busy} aria-label={`Replay ${w.kind} witness for ${value.id}`} onClick={async () => {
      controller.current = new AbortController(); setBusy(true); setError(''); setResult(null);
      try {
        const body = await calculationRequest<{ runId: string; roundIndex: number; measurements: unknown[] }>(`/api/runs/${encodeURIComponent(runId)}/measurements/replay`, { roundIndex: w.roundIndex }, controller.current.signal, setStatus);
        if (body.runId !== runId || body.roundIndex !== w.roundIndex || !Array.isArray(body.measurements)) throw new Error('Replay coordinates do not match the requested witness.');
        setResult(body);
      } catch (err) { setError(err instanceof Error ? err.message : 'Replay failed.'); } finally { setBusy(false); }
    }}>Reconstruct</button></td></tr>)}</tbody></table></div>
    {busy && <p role="status">{status}</p>}{busy && <button type="button" className="btn" onClick={() => controller.current?.abort()}>Cancel reconstruction</button>}
    {error && <p role="alert" className="measurement-error">{error}</p>}
    {result != null && <div aria-label="Reconstructed witness"><pre>{JSON.stringify(result, null, 2)}</pre><button type="button" className="btn" onClick={() => downloadReport(`run-${runId}-witness.json`, JSON.stringify(result, null, 2), 'application/json')}>Export witness evidence</button></div>}
  </details>;
}
