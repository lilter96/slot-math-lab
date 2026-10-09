import { calculationRequest } from '../../lib/measurements/requests';
import { useEffect, useRef, useState } from 'react';
import type { RunSnapshot } from '../../lib/realtime/runProtocol';
import type { MeasurementDefinition } from '../../lib/measurements/model';
import { downloadReport } from '../../lib/results/export';
interface Result { retention?: { retained: boolean; note: string }; runId: string; measurementHash: string; measurementId: string; report: { method: string; statistic: string; subjects: number; observedStatistic: number | null; pValue: number; evaluations: number; minimumResolvablePValue: number | null; monteCarloTailInterval: { lower: number; upper: number } | null; assumptions: string } }
export function MeasurementCalibration({ run, definition }: { run: RunSnapshot; definition: MeasurementDefinition }) {
  const [statistic, setStatistic] = useState('cdf'), [replicates, setReplicates] = useState(2000), [seed, setSeed] = useState(42), [busy, setBusy] = useState(false), [error, setError] = useState(''), [result, setResult] = useState<Result | null>(null), [status, setStatus] = useState('');
  const controller = useRef<AbortController | null>(null); useEffect(() => () => controller.current?.abort(), [run.id]);
  if (!definition.options?.referenceDistribution.length) return null;
  return <details className="measurement-calibration"><summary>Calibrate a discrete distribution discrepancy · sparse counts supported</summary>
    <p>Compare the complete saved count law with its pinned, prespecified PMF. Small null laws are enumerated; larger supported jobs use separate reproducible null simulations. This is a fixed-count goodness-of-fit diagnostic. Non-rejection does not establish equivalence.</p>
    <div className="measurement-form-grid"><label>Null statistic<select aria-label={`Null statistic for ${definition.id}`} value={statistic} onChange={e => { setStatistic(e.target.value); setResult(null); }}><option value="cdf">Maximum discrete CDF distance</option><option value="pearson">Pearson statistic · discrete calibration</option></select></label><label>Null replicates<input aria-label={`Null replicates for ${definition.id}`} type="number" min="100" max="20000" value={replicates} onChange={e => { setReplicates(Number(e.target.value)); setResult(null); }} /></label><label>Diagnostic seed<input aria-label={`Null seed for ${definition.id}`} type="number" step="1" value={seed} onChange={e => { setSeed(Number(e.target.value)); setResult(null); }} /></label></div>
    <button type="button" className="btn" disabled={busy || run.status !== 'completed'} onClick={async () => {
      controller.current = new AbortController(); setBusy(true); setError(''); setResult(null);
      try {
        const body = await calculationRequest<Result>(`/api/runs/${encodeURIComponent(run.id)}/measurements/calibration`, { measurementId: definition.id, statistic, replicates, seed }, controller.current.signal, setStatus);
        if (body.runId !== run.id || body.measurementHash !== run.measurementHash || body.measurementId !== definition.id || !Number.isFinite(body.report?.pValue) || body.report.pValue < 0 || body.report.pValue > 1) throw new Error('Calibration fingerprints or p-value are invalid.');
        setResult(body);
      } catch (err) { setError(err instanceof Error ? err.message : 'Calibration failed.'); } finally { setBusy(false); }
    }}>{busy ? 'Calibrating…' : 'Calibrate pinned distribution'}</button>{busy && <p role="status">{status}</p>}{busy && <button type="button" className="btn" onClick={() => controller.current?.abort()}>Cancel calibration</button>}
    {result?.retention && <p role="status">{result.retention.note}</p>}
    {error && <p role="alert" className="measurement-error">{error}</p>}
    {result && <div aria-label={`Calibrated distribution for ${definition.id}`}><h4>{result.report.method}</h4><p>{result.report.subjects.toLocaleString()} subjects · {result.report.statistic} statistic {result.report.observedStatistic ?? 'structural mismatch'} · inclusive-tail p-value {result.report.pValue.toPrecision(6)} · {result.report.evaluations.toLocaleString()} null evaluations.</p><p>Minimum Monte Carlo p-value {result.report.minimumResolvablePValue ?? 'not applicable'} · Monte Carlo tail interval {result.report.monteCarloTailInterval ? `[${result.report.monteCarloTailInterval.lower}, ${result.report.monteCarloTailInterval.upper}]` : 'not applicable'}.</p><small>{result.report.assumptions}</small><button type="button" className="btn" onClick={() => downloadReport(`run-${run.id}-${definition.id}-calibration.json`, JSON.stringify(result, null, 2), 'application/json')}>Export calibration evidence</button></div>}
  </details>;
}
