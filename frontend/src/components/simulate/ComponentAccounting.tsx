import { useEffect, useMemo, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import type { RunSnapshot } from '../../lib/realtime/runProtocol';
import { accountingCandidates, type AccountingEvidence, type AccountingReport, type AccountingRequest, type RetainedAccounting } from '../../lib/measurements/accounting';
import { useRunEvidence } from '../../lib/results/api';
import { calculationRequest } from '../../lib/measurements/requests';
import { downloadReport } from '../../lib/results/export';
import type { MeasurementDefinition } from '../../lib/measurements/model';

const labels: Record<string, string> = { noObservedViolations: 'No observed violations', discrepancy: 'Reconciliation discrepancy', invalid: 'Evidence invalid', insufficient: 'Insufficient observations', numericalResolution: 'Numeric resolution insufficient' };
const numeric = (value: number | null | undefined) => value == null ? '—' : value.toLocaleString(undefined, { maximumSignificantDigits: 8 });
const emptyPlan: MeasurementDefinition[] = [];
export function ComponentAccounting({ run }: { run: RunSnapshot }) {
  const plan = run.measurements ?? emptyPlan, candidates = useMemo(() => accountingCandidates(plan), [plan]);
  const [selected, setSelected] = useState(() => candidates[0]?.residualMeasurementId ?? ''), [name, setName] = useState('Component reconciliation');
  const single = plan.filter(d => d.options?.subject === 'observation' && !d.options.pair && d.options.assertion !== 'zero');
  const [totalId, setTotal] = useState(() => single[0]?.id ?? ''), [componentIds, setComponents] = useState<string[]>([]), [residualId, setResidual] = useState(''), [groupKey, setGroup] = useState<string | null>(null);
  const [busy, setBusy] = useState(false), [status, setStatus] = useState(''), [error, setError] = useState(''), [result, setResult] = useState<RetainedAccounting | null>(null);
  const controller = useRef<AbortController | null>(null); useEffect(() => () => controller.current?.abort(), [run.id]);
  const archive = useRunEvidence(run.status === 'completed' && candidates.length > 0 ? run.id : undefined), client = useQueryClient();
  const candidate = candidates.find(c => c.residualMeasurementId === selected);
  const input: AccountingRequest = { ...(candidate ?? { name, totalMeasurementId: totalId, componentMeasurementIds: componentIds, residualMeasurementId: residualId }), ...(groupKey !== null ? { groupKey } : {}) };
  const cohorts = Object.keys(run.progress?.measurements?.find(m => m.id === input.totalMeasurementId)?.analysis?.groups ?? {});
  const sameInput = (v: unknown) => {
    if (!v || typeof v !== 'object') return false;
    const a = v as AccountingRequest;
    return a.totalMeasurementId === input.totalMeasurementId && a.residualMeasurementId === input.residualMeasurementId && a.name === input.name && (a.groupKey ?? null) === groupKey && JSON.stringify(a.componentMeasurementIds) === JSON.stringify(input.componentMeasurementIds);
  };
  const retained = archive.data?.diagnostics?.filter(a => a.kind === 'component-accounting' && sameInput(a.input)).at(-1);
  const saved = retained?.output as AccountingEvidence | undefined;
  const calculated = result?.report;
  const output = calculated ?? saved;
  const verified = output?.runId === run.id && output.source?.configHash === run.configHash && output.source?.measurementHash === run.measurementHash && output.source?.sequence === run.sequence && output.source?.paidRounds === run.progress?.sampleCount;
  const change = (fn: () => void) => { controller.current?.abort(); fn(); setResult(null); setError(''); };
  if (!plan.length) return null;
  return <details className="simulation-card component-accounting" id="component-accounting"><summary>Component accounting · reconcile means, all covariances and exact payout identities</summary>
    <p>Choose total and 2–6 components measured on the same observation population. Every pair covariance and a zero assertion of total minus the ordered component sum must have been collected before launch. Completed feature totals can be observed at their authored boundary.</p>
    <div className="measurement-form-grid"><label>Accounting plan<select aria-label="Accounting plan" value={selected} disabled={busy} onChange={e => change(() => setSelected(e.target.value))}><option value="">Select pinned metrics manually</option>{candidates.map(c => <option key={c.residualMeasurementId} value={c.residualMeasurementId}>{c.name}</option>)}</select></label>
      <label>Cohort<select aria-label="Accounting result cohort" value={groupKey === null ? 'overall' : `cohort:${groupKey}`} disabled={busy} onChange={e => change(() => setGroup(e.target.value === 'overall' ? null : e.target.value.slice(7)))}><option value="overall">Entire matching population</option>{cohorts.map(key => <option key={key} value={`cohort:${key}`}>{key || '(empty key)'}</option>)}</select></label>
      {!candidate && <><label>Check name<input aria-label="Accounting check name" value={name} maxLength={64} disabled={busy} onChange={e => change(() => setName(e.target.value))} /></label><label>Total measurement<select aria-label="Accounting total measurement" value={totalId} disabled={busy} onChange={e => change(() => { setTotal(e.target.value); setComponents(c => c.filter(id => id !== e.target.value)); })}>{single.map(d => <option key={d.id} value={d.id}>{d.name}</option>)}</select></label>
        <fieldset className="measurement-wide" disabled={busy}><legend>Ordered components · {componentIds.length} / 6</legend><p>Components follow selection order, which must match the authored residual tree.</p><div className="measurement-reducers">{single.filter(d => d.id !== totalId).map(d => <label key={d.id}><input type="checkbox" checked={componentIds.includes(d.id)} disabled={!componentIds.includes(d.id) && componentIds.length >= 6} onChange={e => change(() => setComponents(c => e.target.checked ? [...c, d.id] : c.filter(id => id !== d.id)))} />{d.name}</label>)}</div></fieldset>
        <label>Exact zero residual<select aria-label="Accounting residual measurement" value={residualId} disabled={busy} onChange={e => change(() => setResidual(e.target.value))}><option value="">Choose a pinned assertion…</option>{plan.filter(d => d.options?.assertion === 'zero').map(d => <option key={d.id} value={d.id}>{d.name}</option>)}</select></label></>}
    </div>
    {!candidates.length && <p>Create an accounting collection plan for the next run, or select a compatible manually authored plan. Means from different visit populations cannot be added.</p>}
    {run.status !== 'completed' && <p className="measurement-next-note">Collecting component statistics uses the live socket dashboard. Final accounting is available after every requested paid round completes; interrupted prefixes do not receive a completed-population verdict.</p>}
    <button type="button" className="btn primary" disabled={busy || run.status !== 'completed' || input.componentMeasurementIds.length < 2 || !input.totalMeasurementId || !input.residualMeasurementId} onClick={async () => {
      const attempt = new AbortController(); controller.current = attempt; setBusy(true); setError(''); setResult(null);
      try {
        const body = await calculationRequest<RetainedAccounting>(`/api/runs/${encodeURIComponent(run.id)}/measurements/accounting`, input, attempt.signal, setStatus);
        attempt.signal.throwIfAborted();
        if (body.runId !== run.id || body.report?.runId !== run.id || body.report.source?.configHash !== run.configHash || body.report.source?.measurementHash !== run.measurementHash || body.report.source?.sequence !== run.sequence || body.report.source?.paidRounds !== run.progress?.sampleCount || !labels[body.report.report?.status]) throw new Error('Accounting evidence does not match this pinned completed run.');
        setResult(body); void client.invalidateQueries({ queryKey: ['run-evidence', run.id] });
      } catch (err) { if (!attempt.signal.aborted) setError(err instanceof Error ? err.message : 'Accounting calculation failed.'); }
      finally { if (controller.current === attempt) setBusy(false); }
    }}>{busy ? 'Reconciling…' : 'Reconcile pinned components'}</button>
    {busy && <><p role="status">{status}</p><button type="button" className="btn" onClick={() => { controller.current?.abort(); setBusy(false); }}>Cancel reconciliation</button></>}{error && <p role="alert" className="measurement-error">{error}</p>}
    {output && !verified && <p role="alert" className="measurement-error">Retained reconciliation identity does not match the selected run. Recalculate from pinned evidence.</p>}
    {output && verified && <><AccountingResult report={output.report} /><p role="status">{result?.retention.note ?? 'Retained server calculation restored from the saved run.'}</p><p className="measurement-fingerprint">Producer Core <code>{output.source.producer?.coreBinarySha256 ?? 'Unavailable'}</code> · snapshot revision {output.source.sequence} · {output.source.paidRounds.toLocaleString()} completed paid rounds.</p><button type="button" className="btn" onClick={() => downloadReport(`run-${run.id}-component-accounting.json`, JSON.stringify(result ?? retained, null, 2), 'application/json')}>Export component reconciliation</button></>}
  </details>;
}
export function AccountingResult({ report }: { report: AccountingReport }) {
  const shortName = (value: string) => value.startsWith(report.name + ' · ') ? value.slice(report.name.length + 3) : value;
  const name = (id: string) => shortName(report.components.find(c => c.measurementId === id)?.name ?? id);
  return <section aria-label="Component reconciliation result" data-accounting-status={report.status}>
    <h3>{report.name} · {labels[report.status] ?? report.status}</h3><p>{report.count.toLocaleString()} matching observations · {report.groupKey != null ? `cohort ${report.groupKey || '(empty key)'}` : 'entire matching population'} · {report.nodeId ?? 'completed paid-round settlement'}</p>
    <dl className="tracked-statistics"><div><dt>Total mean</dt><dd>{numeric(report.totalMean)} {report.unit}</dd></div><div><dt>Component mean sum</dt><dd>{numeric(report.componentMeanSum)} {report.unit}</dd></div><div><dt>Mean residual</dt><dd data-accounting="meanResidual">{numeric(report.meanResidual)}</dd></div><div><dt>Exact observation violations</dt><dd data-accounting="exactViolations">{numeric(report.exactViolations)}</dd></div></dl>
    <div className="results-table-scroll" tabIndex={0} aria-label="Component payout ledger"><table className="results-table"><caption>Observed components · variance and covariance use squared units ({report.unit})²</caption><thead><tr><th>Component</th><th>Matching</th><th>Mean</th><th>Sum</th><th>Sample variance</th><th>Payout / all paid turnover</th></tr></thead><tbody>{report.components.map(c => <tr key={c.measurementId}><th scope="row" title={c.name}>{shortName(c.name)}</th><td>{c.count.toLocaleString()}</td><td>{numeric(c.mean)}</td><td>{numeric(c.sum)}</td><td>{numeric(c.sampleVariance)}</td><td>{numeric(c.paidTurnoverContribution)}</td></tr>)}</tbody></table></div>
    <div className="results-table-scroll" tabIndex={0} aria-label="Pair covariance ledger"><table className="results-table"><caption>All cross terms · no independence assumption</caption><thead><tr><th>Left component</th><th>Right component</th><th>Sample covariance</th><th>Twice covariance</th></tr></thead><tbody>{report.covariances.map((c, i) => <tr key={i}><th scope="row">{name(c.leftMeasurementId)}</th><td>{name(c.rightMeasurementId)}</td><td>{numeric(c.covariance)}</td><td>{numeric(c.covariance == null ? null : 2 * c.covariance)}</td></tr>)}</tbody></table></div>
    <dl className="tracked-statistics"><div><dt>Component variance sum</dt><dd data-accounting="diagonal">{numeric(report.componentVarianceSum)}</dd></div><div><dt>Twice all covariances</dt><dd data-accounting="cross">{numeric(report.twiceCovarianceSum)}</dd></div><div><dt>Reconstructed total variance</dt><dd data-accounting="reconstructed">{numeric(report.reconstructedVariance)}</dd></div><div><dt>Observed total variance</dt><dd data-accounting="actual">{numeric(report.totalSampleVariance)}</dd></div><div><dt>Variance residual</dt><dd data-accounting="varianceResidual">{numeric(report.varianceResidual)}</dd></div><div><dt>Roundoff tolerance · squared units</dt><dd>{numeric(report.numericalTolerance)}</dd></div></dl>
    <p>{report.detail}</p><small>{report.algorithmVersion} · Descriptive accounting on the included sample. Zero observed mismatches does not certify an unobserved population, independent observations or the intended game rules.</small>
  </section>;
}
