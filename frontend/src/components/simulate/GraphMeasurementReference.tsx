import { calculationRequest } from '../../lib/measurements/requests';
import { useEffect, useRef, useState } from 'react';
import type { RunSnapshot } from '../../lib/realtime/runProtocol';
import { downloadReport } from '../../lib/results/export';
interface Occurrence { value: number; massPerPaidRound: string }
interface JointLaw { pairedPerRound: string; support: { x: number; y: number; massPerPaidRound: string }[]; complete: boolean; meanX: string | null; meanY: string | null; varianceX: string | null; varianceY: string | null; covariance: string | null; varianceSum: string | null; varianceDifference: string | null }
interface CohortLaw { key: string; validPerRound: string; knownSumPerRound: string; conditionalMean: string | null; support: Occurrence[]; supportComplete: boolean; pair: JointLaw | null }
interface ReferenceReport { retention?: { retained: boolean; note: string }; configHash: string; measurementHash: string | null; runtimeProvenance: { coreBinarySha256: string | null }; report: {
  status: string; completedPaths: number; operations: number; retainedRoundMass: string; unresolvedRoundMass: string; roundMean: { lower: string; upper: string };
  maximumKnownPayout: string | null; reachableMaximum: string | null; numericalSemantics: string; measurements: { id: string; eligiblePerRound: string; validPerRound: string; excludedPerRound: string; invalidPerRound: string;
    knownSumPerRound: string; conditionalMean: string | null; support: Occurrence[]; supportComplete: boolean; firstError: string | null; groups: CohortLaw[]; groupsComplete: boolean; pair: JointLaw | null; weighting: string; knownAssertionViolationsPerRound: string | null; assertionStatus: string | null }[];
} }
function OccurrenceTable({ support }: { support: Occurrence[] }) {
  return <div className="results-table-scroll"><table className="results-table"><thead><tr><th>Value</th><th>Expected occurrences per paid round</th></tr></thead><tbody>{support.map(p => <tr key={p.value}><td>{p.value}</td><td>{p.massPerPaidRound}</td></tr>)}</tbody></table></div>;
}
function JointEvidence({ value }: { value: JointLaw | null }) {
  if (!value) return null;
  return <details><summary>Paired joint law · {value.complete ? 'complete' : 'incomplete'}</summary><p>Expected paired subjects per paid round {value.pairedPerRound} · conditional means X / Y {value.meanX ?? 'withheld'} / {value.meanY ?? 'withheld'}</p>
    <p>Population variances X / Y {value.varianceX ?? 'withheld'} / {value.varianceY ?? 'withheld'} · covariance {value.covariance ?? 'withheld'} · variance of sum / difference {value.varianceSum ?? 'withheld'} / {value.varianceDifference ?? 'withheld'}</p>
    <div className="results-table-scroll"><table className="results-table"><thead><tr><th>X</th><th>Y</th><th>Expected paired occurrences / round</th></tr></thead><tbody>{value.support.map(p => <tr key={`${p.x}:${p.y}`}><td>{p.x}</td><td>{p.y}</td><td>{p.massPerPaidRound}</td></tr>)}</tbody></table></div></details>;
}
/** Enumeration is tied to the saved graph and plan, never the current editor draft. */
export function GraphMeasurementReference({ run }: { run: RunSnapshot }) {
  const [result, setResult] = useState<ReferenceReport | null>(null), [error, setError] = useState(''), [busy, setBusy] = useState(false), [status, setStatus] = useState('');
  const [paths, setPaths] = useState(10000), controller = useRef<AbortController | null>(null);
  useEffect(() => () => controller.current?.abort(), [run.id]);
  return <details className="simulation-card graph-measurement-reference"><summary>Enumerate the pinned graph · complete measurement laws for small models</summary>
    <p>Traverse the canonical compiled graph without sampling. Reveal and feature laws retain their own exposure per paid round. Frontier, path and operation limits preserve unresolved probability instead of renormalizing it. Compare this implementation evidence with an independently authored reference.</p>
    <div className="measurement-analysis-toolbar"><label>Completed-path budget<input aria-label="Graph enumeration path budget" type="number" min="1" max="100000" value={paths} onChange={e => { setPaths(Number(e.target.value)); setResult(null); }} disabled={busy} /></label>
    <button type="button" className="btn primary" disabled={busy || !!run.execution?.persistentKeys.length} onClick={async () => {
      if (!Number.isInteger(paths) || paths < 1 || paths > 100000) { setError('Use a path budget of 1–100,000.'); return; }
      controller.current = new AbortController(); setBusy(true); setError(''); setResult(null);
      try {
        const body = await calculationRequest<ReferenceReport>(`/api/runs/${encodeURIComponent(run.id)}/measurements/reference`, { maximumPaths: paths, maximumOperations: 250000, maximumFrontier: 2048 }, controller.current.signal, setStatus, 30000);
        if (body.configHash !== run.configHash || (body.measurementHash ?? null) !== (run.measurementHash ?? null)) throw new Error('Enumeration fingerprints do not match this pinned run.');
        setResult(body);
      } catch (err) { setError(err instanceof Error ? err.message : 'Enumeration failed.'); } finally { setBusy(false); }
    }}>{busy ? 'Enumerating…' : 'Enumerate pinned measurements'}</button>{busy && <p role="status">{status}</p>}{busy && <button type="button" className="btn" onClick={() => controller.current?.abort()}>Cancel enumeration</button>}</div>
    {!!run.execution?.persistentKeys.length && <p>This run retains state between rounds. Use an explicit finite-state model for its session or stationary law.</p>}
    {result?.retention && <p role="status">{result.retention.note}</p>}
    {error && <p role="alert" className="measurement-error">{error}</p>}
    {result && <div className="measurement-reference" aria-label="Pinned graph enumeration result"><h3>{result.report.status} · {result.report.completedPaths.toLocaleString()} complete paths</h3><p>Retained probability {result.report.retainedRoundMass} · unresolved probability {result.report.unresolvedRoundMass} · round expectation [{result.report.roundMean.lower}, {result.report.roundMean.upper}]</p>
<p>Maximum known payout {result.report.maximumKnownPayout ?? 'unavailable'} · proven reachable maximum {result.report.reachableMaximum ?? 'withheld · traversal incomplete'}</p>
      <p>{result.report.numericalSemantics}</p><div className="results-table-scroll"><table className="results-table"><thead><tr><th>Measurement</th><th>Valid / eligible exposure per round</th><th>Excluded / invalid exposure</th><th>Expected sum / round</th><th>Conditional mean</th><th>Full support</th></tr></thead><tbody>{result.report.measurements.map(m => <tr key={m.id}><td>{run.measurements?.find(d => d.id === m.id)?.name ?? m.id}</td><td>{m.validPerRound} / {m.eligiblePerRound}</td><td>{m.excludedPerRound} / {m.invalidPerRound}</td><td>{m.knownSumPerRound}</td><td>{m.conditionalMean ?? 'Withheld'}</td><td>{m.supportComplete ? `${m.support.length} values` : 'Incomplete'}</td></tr>)}</tbody></table></div>
      {result.report.measurements.map(m => <details key={m.id}><summary>{run.measurements?.find(d => d.id === m.id)?.name ?? m.id} · probability support</summary>{m.firstError && <p role="alert">{m.firstError}</p>}<p>{m.weighting}</p><OccurrenceTable support={m.support} /><small>Mass sums to expected valid exposure; a reveal population can exceed one occurrence per paid round. Conditional probabilities divide by that exposure.</small><JointEvidence value={m.pair} />
        {m.assertionStatus && <p role="status">Exact assertion · {m.assertionStatus} · known violations per paid round {m.knownAssertionViolationsPerRound}. This is a statement about the enumerated compiled model.</p>}
        {!m.groupsComplete && <p role="alert">Complete cohort coverage is withheld because a path or cohort budget was exhausted.</p>}
        {m.groups.map(g => <details key={g.key}><summary>Cohort {g.key} · {g.supportComplete ? 'complete' : 'incomplete'} value law</summary><p>Expected valid subjects / round {g.validPerRound} · expected sum / round {g.knownSumPerRound} · conditional mean {g.conditionalMean ?? 'withheld'}</p><OccurrenceTable support={g.support} /><JointEvidence value={g.pair} /></details>)}
      </details>)}
      <p className="measurement-fingerprint">Algorithm binary SHA-256 <code>{result.runtimeProvenance.coreBinarySha256 ?? 'Unavailable'}</code></p>
      <button type="button" className="btn" onClick={() => downloadReport(`run-${run.id}-enumerated-measurements.json`, JSON.stringify({ runId: run.id, definitions: run.measurements, reference: result }, null, 2), 'application/json')}>Export enumerated evidence</button>
    </div>}
  </details>;
}
