import { MeasurementComparison } from '../components/results/MeasurementComparison';
import { DiagnosticEvidence } from '../components/results/DiagnosticEvidence';
import { ExecutionReport } from '../components/simulate/ExecutionConfiguration';
import { GraphMeasurementReference } from '../components/simulate/GraphMeasurementReference';
import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { useSimulation, terminal } from '../hooks/useSimulation';
import { useAppStore } from '../store';
import { loadProject } from '../lib/projectFiles';
import { useRunArchive, useRunEvidence, useResultSnapshot, usePinnedReference } from '../lib/results/api';
import { assess, compareRuns, inspectRun, interval, number, percent, planSamples, pp, wilson, type RunEvidence, type RunSummary } from '../lib/results/model';
import { downloadReport, evidenceBundle, reportCsv, reportHtml } from '../lib/results/export';
import { Distribution } from '../components/results/Distribution';
import { IntervalPlot } from '../components/results/IntervalPlot';
import { RunLaunchDialog } from '../components/results/RunLaunchDialog';
import { snapshotDecision, type RunSnapshot } from '../lib/realtime/runProtocol';
import { HttpFailure } from '../lib/realtime/RunConnection';
import { Measurements } from '../components/results/Measurements';
import './results.css';
const sections = ['Overview', 'Distribution', 'Compare', 'Reproducibility'] as const;
function date(value: string) { return new Date(value).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' }); }
function duration(ms: number) { return ms < 60000 ? `${(ms / 1000).toFixed(2)}s` : `${Math.floor(ms / 60000)}m ${Math.floor(ms / 1000) % 60}s`; }
function newest(saved: RunSnapshot, live?: RunSnapshot | null) {
  return live && ['accept', 'duplicate', 'reset'].includes(snapshotDecision(saved, saved.progress ?? null, live)) ? live : saved;
}
function useDebounced(value: string) {
  const [settled, setSettled] = useState(value);
  useEffect(() => { const timer = window.setTimeout(() => setSettled(value), 250); return () => clearTimeout(timer); }, [value]);
  return settled;
}
export default function Results() {
  const session = useSimulation(), [params, setParams] = useSearchParams();
  const [search, setSearch] = useState(''), [filter, setFilter] = useState('all');
  const settledSearch = useDebounced(search), searchPending = search !== settledSearch;
  const archive = useRunArchive(settledSearch, filter);
  const runs = useMemo(() => {
    const seen = new Set<string>(); return archive.data?.pages.flatMap(page => page.items).filter(run => !seen.has(run.id) && !!seen.add(run.id)) ?? [];
  }, [archive.data]);
  const defaultRun = useRunEvidence(!params.get('run') && session.run?.id && !runs.some(run => run.id === session.run?.id) ? session.run.id : undefined);
  const missingDefault = defaultRun.error instanceof HttpFailure && defaultRun.error.status === 404;
  const selected = params.get('run') ?? (missingDefault ? runs[0]?.id : session.run?.id ?? runs[0]?.id);
  const counts = archive.data?.pages[0];
  function select(id: string) { const next = new URLSearchParams(params); next.set('run', id); if (next.get('compare') === id) next.delete('compare'); setParams(next); }
  return <div className="workspace results-workspace"><div className="results-shell">
    <header className="results-heading"><div><p className="results-eyebrow">MATH LAB / SAVED EVIDENCE</p><h1>Results <span>Make the numbers accountable.</span></h1><p>Inspect pinned runs, resolve uncertainty, and reproduce the calculation.</p></div><div className="results-header-actions"><button className="btn" onClick={() => void archive.refetch()} disabled={archive.isFetching}>{archive.isFetching ? 'Refreshing…' : '↻ Refresh archive'}</button><Link className="btn primary" to="/simulate">＋ New simulation</Link></div></header>
    <div className="results-layout"><aside className="results-archive" aria-label="Run archive" aria-busy={searchPending || archive.isFetching}><header><h2>Run archive <span>{counts?.total ?? '—'}</span></h2><p>Saved on the server · available across sessions</p></header>
      <label className="results-search">Search runs<input aria-label="Search saved runs" placeholder="Model, run ID, hash or seed" value={search} onChange={e => setSearch(e.target.value)} /></label>
      <label className="results-filter">Status<select aria-label="Filter saved runs" value={filter} onChange={e => setFilter(e.target.value)}><option value="all">All runs</option><option value="completed">Completed</option><option value="partial">Cancelled / partial</option><option value="failed">Failed / interrupted</option><option value="active">In progress</option></select></label>
      {counts && <div className="results-archive-counts"><span><i className="completed" />{counts.completed} complete</span><span><i className="running" />{counts.active} active</span><span><i className="cancelled" />{counts.partial + counts.failed} partial / failed</span></div>}
      {archive.isPending && <p className="results-loading" role="status">Loading saved runs…</p>}
      {archive.error && <div className="results-error" role="alert">{archive.error.message}<button className="btn" onClick={() => void archive.refetch()}>Retry archive</button></div>}
      {!archive.isPending && !archive.error && !runs.length && <div className="results-archive-empty"><strong>{search || filter !== 'all' ? 'No matching runs' : 'No saved runs yet'}</strong><p>{search || filter !== 'all' ? 'Try another name, seed or status.' : 'Run the constructor model in Simulate to create reproducible evidence.'}</p></div>}
      <ol className="results-run-list">{runs.map(run => <li key={run.id}><button className={selected === run.id ? 'selected' : ''} aria-current={selected === run.id ? 'true' : undefined} onClick={() => select(run.id)} aria-label={`Inspect ${run.model.name}, seed ${run.seed}, run ${run.id}`}><div><strong>{run.model.name}</strong><span className={`results-status ${run.status}`}>{run.status === 'cancelled' ? 'partial' : run.status}</span></div><p>Seed {run.seed} · v{run.configVersion} · {date(run.createdAt)}</p><footer><span>{run.sampleCount.toLocaleString()} rounds</span><b>{percent(run.rtp)}</b></footer><code>{run.id.slice(0, 12)}</code></button></li>)}</ol>
      {archive.hasNextPage && <button className="btn results-load-more" onClick={() => void archive.fetchNextPage()} disabled={searchPending || archive.isFetching}>{archive.isFetchingNextPage ? 'Loading…' : 'Load older runs'}</button>}
      <footer className="results-archive-footer">{runs.length} loaded{counts ? ` · ${counts.total} matching` : ''}</footer>
    </aside><section className="results-detail" aria-label="Selected run evidence">{selected ? <SavedRun key={selected} id={selected} candidates={runs} /> : <div className="results-empty results-welcome"><span className="results-empty-icon">◎</span><h2>Evidence starts with a run.</h2><p>Results is the record of what was calculated: the saved input, observed payouts, mathematical reference, and uncertainty. Your current editor remains a separate draft.</p><Link className="btn primary" to="/simulate">Run the constructor model →</Link><div className="results-welcome-grid"><article><strong>Understand</strong><p>Examine payout ranges, rare outcomes, and sampling precision.</p></article><article><strong>Compare</strong><p>Distinguish seeded replay from independent measurements.</p></article><article><strong>Reproduce</strong><p>Keep the exact input version, seed, and evidence together.</p></article></div></div>}</section></div>
  </div></div>;
}
function SavedRun({ id, candidates }: { id: string; candidates: RunSummary[] }) {
  const s = useSimulation(), evidenceQuery = useRunEvidence(id), snapshotQuery = useResultSnapshot(id, s.run);
  const baselineId = useSearchParams()[0].get('compare') ?? undefined;
  const baselineQuery = useRunEvidence(baselineId), baselineSnapshot = useResultSnapshot(baselineId, s.run);
  const data = evidenceQuery.data;
  const snapshot = data ? newest(data.run, s.run?.id === id ? s.run : snapshotQuery.data) : undefined;
  const evidence = data && snapshot ? { ...data, run: snapshot } : undefined;
  const baseline = baselineQuery.data ? { ...baselineQuery.data, run: newest(baselineQuery.data.run, s.run?.id === baselineId ? s.run : baselineSnapshot.data) } : undefined;
  if (evidenceQuery.isPending) return <div className="results-loading" role="status">Loading pinned run and evidence…</div>;
  if (evidenceQuery.error || !evidence) return <div className="results-empty"><h2>Run could not be retrieved</h2><p role="alert">{evidenceQuery.error?.message ?? 'This run is unavailable.'}</p><code>{id}</code><button className="btn" onClick={() => void evidenceQuery.refetch()}>Retry this run</button><Link to="/simulate">Open simulation history</Link></div>;
  return <RunReport evidence={evidence} baseline={baseline} candidates={candidates} snapshotError={snapshotQuery.error?.message}
    comparisonError={baselineQuery.error?.message ?? baselineSnapshot.error?.message} />;
}
function RunReport({ evidence, baseline, candidates, snapshotError, comparisonError }: {
  evidence: RunEvidence; baseline?: RunEvidence; candidates: RunSummary[]; snapshotError?: string; comparisonError?: string;
}) {
  const { run, model } = evidence, p = run.progress, check = inspectRun(run), navigate = useNavigate(), session = useSimulation();
  const [params, setParams] = useSearchParams(), [tolerance, setTolerance] = useState('0.5'), [notice, setNotice] = useState('');
  const [launch, setLaunch] = useState<'replay' | 'new' | null>(null);
  const section = sections.find(item => item.toLowerCase() === params.get('view')) ?? 'Overview';
  const referenceQuery = usePinnedReference(evidence), reference = referenceQuery.data;
  const referencePause = referenceQuery.isFetching && referenceQuery.failureReason instanceof HttpFailure && referenceQuery.failureReason.status === 429
    ? Math.max(1, Math.ceil(referenceQuery.failureReason.retryAfterMs / 1000)) : null;
  const goal = model.targetRtp ?? reference?.rtp ?? null, toleranceValue = Number(tolerance);
  const assessment = assess(run, goal, toleranceValue, evidence.inputVerified), ci = interval(p), plan = planSamples(p, toleranceValue);
  const hasData = !!p?.sampleCount, safe = !check.issues.length, hitCi = p && !p.execution?.carriesState ? wilson(p.nonZeroCount, p.sampleCount) : null;
  const referenceConsistent = safe && check.complete && p && p.volatility > 0 && ci && reference?.rtp != null ? reference.rtp >= ci[0] && reference.rtp <= ci[1] : null;
  const activeElsewhere = session.starting || !!session.run && !terminal(session.run.status) && session.connection !== 'unavailable';
  function show(view: typeof section) { const next = new URLSearchParams(params); next.set('run', idOf(run)); next.set('view', view.toLowerCase()); setParams(next, { replace: true }); }
  function openGraph(proof = false) {
    const config = proof ? reference?.proof : evidence.pinnedConfig;
    if (!config) return;
    const editor = useAppStore.getState();
    if (!editor.resultsDraft) useAppStore.setState({ resultsDraft: structuredClone({ nodes: editor.nodes, edges: editor.edges,
      tables: editor.tables, graphTrail: editor.graphTrail, verificationSource: editor.verificationSource, configName: editor.configName }) });
    loadProject(config); useAppStore.setState({ graphTrail: [], verificationSource: proof ? evidence.pinnedConfig : null }); navigate('/build');
  }
  async function copyLink() {
    const url = new URL(location.href); url.searchParams.set('run', run.id);
    try { await navigator.clipboard.writeText(url.toString()); setNotice('Run link copied. The recipient must have access to this server.'); }
    catch { setNotice(`Copy this run link: ${url}`); }
  }
  const exportBundle = () => downloadReport(`run-${run.id}-evidence.json`, JSON.stringify(evidenceBundle(evidence, reference, toleranceValue, baseline), null, 2), 'application/json');
  return <>
    <header className="results-report-heading"><div><p className="results-eyebrow">SAVED INPUT / VERSION {run.configVersion}</p><h2>{model.name}</h2><p><time>{date(run.createdAt)}</time><span>·</span><code>{run.id.slice(0, 12)}</code><span className={`results-status ${run.status}`}>{run.status === 'cancelled' ? 'cancelled · partial' : run.status}</span></p></div><div className="results-report-actions"><button className="btn" onClick={() => void copyLink()}>↗ Copy run link</button><button className="btn primary" onClick={exportBundle}>↓ Evidence JSON</button></div></header>
    {notice && <p className="results-notice" role="status">{notice}<button aria-label="Dismiss message" onClick={() => setNotice('')}>✕</button></p>}
    {snapshotError && <p className="results-error" role="alert">Status refresh failed: {snapshotError}. The last retrieved observation is retained.</p>}
    {check.error && <div className="results-error" role="alert"><strong>{run.status === 'failed' ? 'Run interrupted or failed' : 'Run diagnostic'}</strong><p>{check.error}</p></div>}
    {!!check.issues.length && <div className="results-error" role="alert"><strong>Evidence integrity needs attention</strong><ul>{check.issues.map(issue => <li key={issue}>{issue}</li>)}</ul><p>Acceptance and comparison verdicts are withheld.</p></div>}
    {!evidence.inputVerified && <div className="results-error" role="alert">The original input is missing or its fingerprint does not match. Replay and reference calculation are disabled.</div>}
    <div className="results-report-strip"><span><i className={evidence.inputVerified ? 'verified' : ''} />{evidence.inputVerified ? 'Pinned input verified' : 'Input unverified'}</span><span>Sampled · {number(p?.sampleCount)} / {number(p?.totalSamples)} rounds</span><span>Seed {run.seed}</span><span>{run.degreeOfParallelism} workers · {duration(p?.elapsedMs ?? 0)}</span>{!terminal(run.status) && <Link to={`/simulate?run=${run.id}`}>Open live simulation ↗</Link>}</div>
    <nav className="results-section-tabs" role="tablist" aria-label="Result sections">{sections.map(item => <button key={item} id={`result-tab-${item}`} aria-controls={`result-panel-${item}`} role="tab" aria-selected={section === item} tabIndex={section === item ? 0 : -1} onClick={() => show(item)} onKeyDown={event => {
      let index = sections.indexOf(item); if (event.key === 'ArrowRight') index = (index + 1) % sections.length; else if (event.key === 'ArrowLeft') index = (index + sections.length - 1) % sections.length; else if (event.key === 'Home') index = 0; else if (event.key === 'End') index = sections.length - 1; else return;
      event.preventDefault(); show(sections[index]); document.getElementById(`result-tab-${sections[index]}`)?.focus();
    }}>{item}</button>)}</nav>
    <section className="results-panel" role="tabpanel" id={`result-panel-${section}`} aria-labelledby={`result-tab-${section}`}>
    {section === 'Overview' && <>
      <div className="results-outcome"><div className="results-rtp-hero"><span>Observed return to player <b>SAMPLED</b></span><strong data-testid="results-rtp">{safe && hasData ? percent(p.runningRtp) : '—'}</strong><p>Complete paid-round payout / stake</p><div><span>Authored target <b>{percent(model.targetRtp)}</b></span><span>Exact expectation <b>{percent(reference?.rtp)}</b></span></div></div><div className={`results-assessment ${assessment.kind}`}><span className="results-eyebrow">SAMPLING PRECISION CHECK</span><h3 data-testid="results-assessment">{assessment.title}</h3><p>{assessment.note}</p><label>Acceptance tolerance<input aria-label="RTP tolerance in percentage points" type="number" min="0.000001" max="100" step="0.1" value={tolerance} onChange={event => setTolerance(event.target.value)} /> <span>percentage points</span></label>{model.targetRtp == null && reference?.rtp != null && <small>Using the calculated expectation as the precision target.</small>}</div></div>
      <div className="results-card results-confidence"><div className="results-card-head"><div><h3>Where the estimate stands</h3><p>Approximate 95% interval from completed observations. Target and reference are separate evidence.</p></div><strong>{safe && ci ? `${percent(ci[0])} – ${percent(ci[1])}` : 'Interval unavailable'}</strong></div>{safe && hasData && <IntervalPlot mean={p.runningRtp} ci={ci} target={goal} tolerance={Number.isFinite(toleranceValue) && toleranceValue > 0 ? toleranceValue : 0} reference={reference?.rtp} />}<div className="results-metrics-row"><Metric label="CI half-width" value={safe && ci ? `±${(1.96 * p!.stdErr * 100).toFixed(3)} pp` : '—'} note={p && p.sampleCount < 2 ? 'Needs at least 2 rounds' : 'Normal approximation'} /><Metric label="Hit frequency" value={safe && hasData ? percent(p.hitFrequency) : '—'} note={safe && hitCi ? `95% Wilson interval ${percent(hitCi[0])}–${percent(hitCi[1])}` : 'Winning rounds / all rounds'} /><Metric label="Payout volatility" value={safe && p && p.sampleCount > 1 ? `${number(p.volatility)}×` : '—'} note="Sample standard deviation" /><Metric label="Maximum observed" value={safe && hasData ? `${number(p.maxWin)}×` : '—'} note={`Declared cap ${model.winCap != null ? number(model.winCap) + '×' : 'unavailable'}`} /></div></div>
      <div className="results-two-column"><section className="results-card results-reference"><div className="results-card-head"><div><h3>Mathematical reference</h3><p>Calculated from this saved input, independent of editor changes.</p></div><span className="results-source-tag">{reference?.kind ?? 'NOT CALCULATED'}</span></div>
        {referenceQuery.error && <p className="results-error" role="alert">{referenceQuery.error.message}</p>}
        {referencePause != null && <p className="results-notice" role="status">The server requested a {referencePause}s pause. The reference will retry automatically.</p>}
        {reference ? <><div className="results-reference-value">{reference.kind === 'ExactInterval' ? `${percent(reference.lower)} – ${percent(reference.upper)}` : reference.rtp != null ? percent(reference.rtp) : 'Unavailable'}</div><p className="results-body-note">{reference.note}</p>{referenceConsistent != null && <p className={`results-reference-verdict ${referenceConsistent ? 'inside' : 'outside'}`}>{referenceConsistent ? 'Reference lies inside the sampling interval' : 'Reference lies outside the sampling interval'} · a consistency check, not certification</p>}{reference.rational && <details className="results-rational"><summary>Inspect the exact rational value</summary><code>{reference.rational}</code></details>}{reference.components && <div className="results-contributions">{reference.components.map(component => <div key={component.name}><span>{component.name}</span><strong>{percent(component.rtp)}</strong><i style={{ width: `${reference.rtp ? Math.max(0, component.rtp / reference.rtp * 100) : 0}%` }} /></div>)}</div>}</> : <p className="results-body-note">An authored target is a design intent. Calculate a rational expectation or conservative bound before treating it as a mathematical reference.</p>}
        <footer className="results-card-actions"><button className="btn" disabled={!evidence.inputVerified || referenceQuery.isFetching} onClick={() => void referenceQuery.refetch()}>{referenceQuery.isFetching ? 'Calculating reference…' : reference ? 'Recalculate reference' : 'Calculate reference'}</button>{reference?.proof && <button className="btn" onClick={() => openGraph(true)}>Open proof in constructor ↗</button>}</footer>
      </section><section className="results-card results-next-run"><div className="results-card-head"><div><h3>Plan the next measurement</h3><p>Estimate the fixed sample size needed for the chosen half-width.</p></div><span className="results-source-tag">PLANNING ESTIMATE</span></div><div className="results-plan-number">{plan ? number(plan.total) : '—'}<span>complete rounds</span></div><p className="results-body-note">{plan ? `About ${number(plan.additional)} additional observations at the current estimated variance. This plans interval width; it cannot guarantee an acceptance outcome.` : 'A positive tolerance and observed variation are needed. Zero sample variance cannot establish that rare payouts are impossible.'}</p>{plan?.exceedsRunLimit && <p className="results-body-note">This exceeds the 10,000,000-round limit per run. The launch is capped at that limit; matching-seed runs must not be pooled as independent evidence.</p>}<footer className="results-card-actions"><button className="btn primary" disabled={!evidence.inputVerified || activeElsewhere} onClick={() => setLaunch('new')}>New seed, same model</button><button className="btn" disabled={!evidence.inputVerified || activeElsewhere} onClick={() => setLaunch('replay')}>Replay pinned run</button></footer>{activeElsewhere && <p className="results-body-note">Finish or cancel the active simulation before launching another run.</p>}</section></div>
      {safe && <><ExecutionReport value={p?.execution} /><GraphMeasurementReference key={run.id} run={run} /><Measurements run={run} /><DiagnosticEvidence evidence={evidence} /></>}
      <div className="results-card results-evidence-checks"><div className="results-card-head"><h3>Evidence checks</h3><span className="results-source-tag">OBSERVABLE CONTRACTS</span></div><div className="results-check-grid"><Check good={evidence.inputVerified} label="Pinned input" detail={evidence.inputVerified ? 'SHA-256 matches the recorded run input' : 'Missing or mismatched saved model'} /><Check good={safe} label="Result consistency" detail={safe ? 'Snapshot, persisted metrics and histogram agree' : check.issues[0]} /><Check good={check.complete} label="Round coverage" detail={check.complete ? 'Every requested round completed' : `${number(p?.sampleCount)} of ${number(p?.totalSamples)} rounds`} /><Check good={reference?.kind === 'ExactExpectation' || reference?.kind === 'ExactDistribution'} label="Reference" detail={reference ? reference.kind : 'Explicit calculation required'} /></div></div>
    </>}
    {section === 'Distribution' && <><div className="results-card">{safe ? <Distribution progress={p} complete={check.complete} /> : <p className="results-body-note">Repair the evidence inconsistency before interpreting its distribution.</p>}</div><div className="results-card results-cap-note"><h3>What this distribution can establish</h3><p>Histogram counts describe the observed model. Percentiles inside a bin are displayed as ranges; values are not interpolated. Maximum observed is distinct from the theoretical cap.</p><p>Sampler clipping count: <strong>{number(p?.capHits)}</strong>. Cap enforcement upstream in the compiled graph is not instrumented by this counter, so it does not measure all cap events.</p></div></>}
    {section === 'Compare' && <Comparison evidence={evidence} baseline={baseline} candidates={candidates} error={comparisonError} />}
    {section === 'Reproducibility' && <>
      <div className="results-card results-reproduction"><div className="results-card-head"><div><h3>Reproduce this calculation</h3><p>Pin the input first, then the random stream and completed-round budget.</p></div><button className="btn" disabled={!evidence.inputVerified || activeElsewhere} onClick={() => setLaunch('replay')}>Replay pinned run</button></div><dl>{[
        ['Run ID', run.id], ['Config ID / version', `${run.configId} / v${run.configVersion}`], ['Config SHA-256', run.configHash], ['Model SHA-256', model.modelHash ?? 'Unavailable'],
        ['Seed', String(run.seed)], ['Logical stream', run.streamScheme], ['Workers', String(run.degreeOfParallelism)], ['Requested / completed rounds', `${number(p?.totalSamples)} / ${number(p?.sampleCount)}`],
        ['Run status', run.status], ['Created', date(run.createdAt)], ['Completed', run.completedAt ? date(run.completedAt) : 'Not completed'],
        ['Stream epoch / revision', `${run.streamEpoch ?? 'legacy'} / ${run.sequence ?? p?.sequence ?? 0}`], ['Sampling engine', String(check.result?.samplingEngine ?? 'Not recorded')],
        ['Measurement algorithms', run.runtimeProvenance?.numericalMethods ?? 'Not recorded'], ['Core binary SHA-256', run.runtimeProvenance?.coreBinarySha256 ?? 'Not recorded'],
        ['API binary SHA-256', run.runtimeProvenance?.apiBinarySha256 ?? 'Not recorded'], ['Execution regime', run.execution?.regime ?? 'independentRounds'],
      ].map(([name, value]) => <div key={name}><dt>{name}</dt><dd>{value}</dd></div>)}</dl><p className="results-body-note">The model fingerprint excludes only the document ID. All other serialized configuration fields participate. Same-seed comparisons require the same model, stream scheme, and length; worker counts may differ.</p></div>
      <section className="results-card results-exports"><div className="results-card-head"><div><h3>Portable evidence</h3><p>Exports use this saved run. The current editor is not substituted.</p></div></div><div className="results-card-actions"><button className="btn primary" onClick={exportBundle}>Download evidence JSON</button><button className="btn" onClick={() => downloadReport(`run-${run.id}.csv`, reportCsv(evidence, reference), 'text/csv;charset=utf-8')}>Download metrics CSV</button><button className="btn" onClick={() => downloadReport(`run-${run.id}.html`, reportHtml(evidence, reference, toleranceValue), 'text/html;charset=utf-8')}>Printable report HTML</button><button className="btn" disabled={!evidence.pinnedConfig} onClick={() => downloadReport(`pinned-model-${run.configHash}.json`, JSON.stringify(evidence.pinnedConfig, null, 2), 'application/json')}>Download pinned model</button></div></section>
      <section className="results-card results-input"><div className="results-card-head"><div><h3>Pinned constructor model</h3><p>Opening it loads the saved model into Build and preserves your current draft with a restore action.</p></div><button className="btn" disabled={!evidence.inputVerified} onClick={() => openGraph()}>Open pinned graph ↗</button></div><details><summary>Inspect saved graph JSON</summary><pre>{JSON.stringify(evidence.pinnedConfig, null, 2)}</pre></details><details><summary>Inspect persisted result JSON</summary><pre>{run.resultJson ?? 'The run has not produced a terminal result yet.'}</pre></details></section>
    </>}
    </section>
    <footer className="results-report-footer"><span>Input v{run.configVersion} · {run.configHash.slice(0, 16)}</span><span>{run.status === 'completed' ? 'Saved run evidence' : 'Diagnostic observations'} · sampling does not certify the model</span></footer>
    {launch && <RunLaunchDialog evidence={evidence} reference={reference} replay={launch === 'replay'} plannedSamples={plan?.total} close={() => setLaunch(null)} />}
  </>;
}
function idOf(run: RunEvidence['run']) { return run.id; }
function Metric({ label, value, note }: { label: string; value: string; note: string }) { return <div><span>{label}</span><strong>{value}</strong><small>{note}</small></div>; }
function Check({ good, label, detail }: { good: boolean; label: string; detail: string }) { return <article><i className={good ? 'good' : ''}>{good ? '✓' : '○'}</i><div><strong>{label}</strong><p>{detail}</p></div></article>; }
function Comparison({ evidence, baseline, candidates, error }: { evidence: RunEvidence; baseline?: RunEvidence; candidates: RunSummary[]; error?: string }) {
  const [params, setParams] = useSearchParams(), selected = params.get('compare') ?? '';
  const comparison = baseline ? compareRuns(evidence, baseline) : null;
  const ap = evidence.run.progress, bp = baseline?.run.progress;
  const rows = [['Observed RTP', percent(ap?.sampleCount ? ap.runningRtp : null), percent(bp?.sampleCount ? bp.runningRtp : null)],
    ['Complete rounds', number(ap?.sampleCount), number(bp?.sampleCount)], ['Requested rounds', number(ap?.totalSamples), number(bp?.totalSamples)],
    ['Seed', String(evidence.run.seed), baseline ? String(baseline.run.seed) : '—'], ['Workers', String(evidence.run.degreeOfParallelism), baseline ? String(baseline.run.degreeOfParallelism) : '—'],
    ['Hit frequency', percent(ap?.sampleCount ? ap.hitFrequency : null), percent(bp?.sampleCount ? bp.hitFrequency : null)], ['Payout standard deviation', ap && ap.sampleCount > 1 ? `${number(ap.volatility)}×` : '—', bp && bp.sampleCount > 1 ? `${number(bp.volatility)}×` : '—'],
    ['Maximum observed', ap?.sampleCount ? `${number(ap.maxWin)}×` : '—', bp?.sampleCount ? `${number(bp.maxWin)}×` : '—'],
    ['Status', evidence.run.status, baseline?.run.status ?? '—'], ['Model fingerprint', evidence.model.modelHash?.slice(0, 16) ?? 'Unavailable', baseline?.model.modelHash?.slice(0, 16) ?? '—']];
  return <section className="results-card results-comparison"><div className="results-card-head"><div><h3>Compare run evidence</h3><p>Inspect differences before interpreting them as a mathematical regression.</p></div></div><label className="results-baseline-select">Baseline run<select aria-label="Comparison baseline run" value={selected} onChange={event => {
    const next = new URLSearchParams(params); next.set('run', evidence.run.id); if (event.target.value) next.set('compare', event.target.value); else next.delete('compare'); setParams(next, { replace: true });
  }}><option value="">Choose a saved run</option>{selected && !candidates.some(run => run.id === selected) && <option value={selected}>Linked run {selected.slice(0, 12)}</option>}{candidates.filter(run => run.id !== evidence.run.id).map(run => <option key={run.id} value={run.id}>{run.model.modelHash === evidence.model.modelHash ? 'Same model · ' : ''}{run.model.name} · seed {run.seed} · {run.sampleCount.toLocaleString()} rounds · {run.id.slice(0, 8)}</option>)}</select></label>
    {error && <p role="alert" className="results-error">{error}</p>}{selected && !baseline && !error && <p role="status" className="results-loading">Loading the baseline evidence…</p>}
    {comparison && <div className={`results-comparison-verdict ${comparison.kind}`}><h3 data-testid="results-comparison-verdict">{comparison.title}</h3><p>{comparison.note}</p><div><span>RTP difference <strong>{pp(comparison.delta)}</strong></span>{comparison.ci && <span>95% interval of difference <strong>{pp(comparison.ci[0])} to {pp(comparison.ci[1])}</strong></span>}</div></div>}
    <div className="results-table-scroll"><table className="results-table"><thead><tr><th>Metric</th><th>Selected run {evidence.run.id.slice(0, 8)}</th><th>Baseline {baseline?.run.id.slice(0, 8) ?? 'not selected'}</th></tr></thead><tbody>{rows.map(([label, a, b]) => <tr key={label}><td>{label}</td><td>{a}</td><td>{b}</td></tr>)}</tbody></table></div>{baseline && <MeasurementComparison selected={evidence.run} baseline={baseline.run} />}<p className="results-body-note">Same-seed streams can overlap and are not independent. Different model fingerprints receive descriptive comparisons, without an agreement verdict. No runs are pooled automatically.</p>
  </section>;
}
