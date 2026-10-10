import { SamplePlanning } from '../components/simulate/SamplePlanning';
import { defaultExecution } from '../lib/measurements/execution';
import { ExecutionConfiguration, ExecutionReport } from '../components/simulate/ExecutionConfiguration';
import { ReferenceWorkbench } from '../components/simulate/ReferenceWorkbench';
import { useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, useSearchParams } from 'react-router-dom';
import { useAppStore } from '../store';
import { useSimulation, startSimulation, cancelSimulation, openSimulation, reconnectSimulation, terminal } from '../hooks/useSimulation';
import { LiveChart } from '../components/simulate/LiveChart';
import { count } from '../components/simulate/format';
import { LiveHistogram } from '../components/simulate/LiveHistogram';
import { downloadJson } from '../games/doghouse/api';
import { MeasurementWorkspace } from '../components/simulate/MeasurementWorkspace';
import { useMeasurementWorkspace, type Widget } from '../lib/measurements/store';
import { readSimulationEvidence } from '../lib/results/api';
import { formatNumber, formatPercent } from '../lib/numberFormat';
import { simulationEvidence } from '../lib/results/evidence';
import './simulate.css';
const pct = formatPercent;
const duration = (ms: number) => ms < 60000 ? `${(ms / 1000).toFixed(1)}s` : `${Math.floor(ms / 60000)}m ${Math.floor(ms / 1000) % 60}s`;
export default function Simulate() {
  const [query, setQuery] = useSearchParams();
  const requestedRun = query.get('run');
  const measurementWorkspace = useMeasurementWorkspace();
  const visible = (widget: Widget) => !measurementWorkspace.hiddenWidgets.includes(widget);
  const s = useSimulation(), currentName = useAppStore(x => x.configName);
  const authoredTarget = useAppStore(x => Number((x.tables.initialState as Record<string, unknown> | undefined)?.targetRtpPercent) / 100);
  const target = s.run ? s.target : Number.isFinite(authoredTarget) ? authoredTarget : null;
  const [execution, setExecution] = useState(() => s.run?.execution ?? defaultExecution());
  const [seed, setSeed] = useState(s.run?.seed ?? 42), [samples, setSamples] = useState(s.progress?.totalSamples || 100000), [workers, setWorkers] = useState(s.run?.degreeOfParallelism ?? 2);
  const p = s.progress, hasData = !!p?.sampleCount, active = s.starting || (!!s.run && !terminal(s.run.status) && s.connection !== 'unavailable');
  const [now, setNow] = useState(Date.now);
  const selectedRunId = s.run?.id;
  const exportRequest = useRef<AbortController | null>(null);
  const [exportState, setExportState] = useState<{ runId: string; busy: boolean; message: string; error: string } | null>(null);
  const exporting = !!exportState && exportState.runId === selectedRunId && exportState.busy;
  const exportError = exportState && exportState.runId === selectedRunId ? exportState.error : '';
  useEffect(() => () => { exportRequest.current?.abort(); }, [selectedRunId]);
  const linked = useQuery<{ run: import('../lib/realtime/runProtocol').RunSnapshot; model: { name: string; targetRtp: number | null } }>({
    queryKey: ['simulate-linked-run', requestedRun], enabled: !!requestedRun && requestedRun !== selectedRunId && !active, retry: false,
    queryFn: ({ signal }) => readSimulationEvidence(requestedRun!, signal, () => {}),
  });
  const loadingRun = linked.isFetching;
  const queryError = linked.error?.message;
  useEffect(() => {
    if (!linked.data || active || requestedRun === selectedRunId || linked.data.run.id !== requestedRun) return;
    void openSimulation(linked.data.run, { model: linked.data.model.name, target: linked.data.model.targetRtp });
  }, [linked.data, requestedRun, selectedRunId, active]);
  useEffect(() => {
    if (!active) return;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [active]);
  const syncAge = s.health.lastConfirmedAt ? Math.max(0, now - s.health.lastConfirmedAt) : null;
  const hasSampleVariance = !!p && p.sampleCount > 1;
  const hasVariance = hasSampleVariance && p.stdErr > 0 && !p.execution?.carriesState;
  const status = s.starting ? 'starting' : s.connection === 'unavailable' ? 'unavailable' : s.run?.status ?? 'ready';
  const rate = p && p.elapsedMs > 0 ? p.sampleCount * 1000 / p.elapsedMs : 0;
  const percent = p?.totalSamples ? Math.min(100, p.sampleCount / p.totalSamples * 100) : 0;
  const pinnedExecution = s.run?.execution ?? execution;
  const population = pinnedExecution.regime === 'persistent' ? 'one persistent trajectory · dependent paid rounds'
    : pinnedExecution.regime === 'sessions' ? `independent sessions of ${pinnedExecution.sessionLength.toLocaleString()} paid rounds${pinnedExecution.persistentKeys.length ? ' · state retained inside each session' : ''}`
    : 'independent paid rounds, including the complete bonus';
  const reference = s.reference;
  const inBand = hasVariance && reference != null ? Math.abs(p.runningRtp - reference) <= 1.96 * p.stdErr : null;
  const download = async () => {
    if (!s.run || exportRequest.current && !exportRequest.current.signal.aborted) return;
    const run = s.run, controller = new AbortController();
    exportRequest.current = controller;
    setExportState({ runId: run.id, busy: true, message: 'Fetching the authoritative run and pinned model…', error: '' });
    try {
      const evidence = await readSimulationEvidence(run.id, controller.signal, message => {
        if (!controller.signal.aborted) setExportState({ runId: run.id, busy: true, message, error: '' });
      });
      controller.signal.throwIfAborted();
      const bundle = simulationEvidence(evidence, run, { points: s.points, reference, referenceNote: s.referenceNote, dashboard: measurementWorkspace });
      downloadJson(`simulation-${run.id}-seed-${run.seed}.json`, bundle);
      setExportState(null);
    } catch (error) {
      if (!controller.signal.aborted) setExportState({ runId: run.id, busy: false, message: '',
        error: `Evidence export failed. ${error instanceof Error ? error.message : 'Try again when the connection recovers.'} No file was downloaded.` });
    } finally { if (exportRequest.current === controller) exportRequest.current = null; }
  };
  return <div className="workspace simulation-workspace"><div className="simulation-dashboard">
    <header className="simulation-heading"><div><div className="sim-eyebrow">MATHEMATICAL VERIFICATION / MONTE CARLO</div><h1>Simulation lab <span className={`run-status ${status}`}>{status}</span></h1><p>{s.model || currentName || 'Full constructor model'} <span>· {population}</span></p></div>
      <div className="stream-state" data-testid="stream-status"><i className={active && s.connection === 'live' ? 'live' : ''} />{active ? s.connection === 'live' ? 'WebSocket live' : s.connection === 'auth-required' ? 'Sign in required' : `Stream ${s.connection}` : s.connection === 'unavailable' ? 'Run unavailable' : 'Stream idle'}<small>{active ? syncAge != null ? `Server confirmed ${duration(syncAge)} ago${s.health.roundTripMs != null ? ` · ${Math.round(s.health.roundTripMs)} ms` : ''}` : 'Waiting for server acknowledgement' : s.run && terminal(s.run.status) ? 'Result saved on server' : 'Ready to connect'}</small>{active && <small>{s.connection === 'live' ? `Automatic recovery · ${s.health.reconnects} reconnects` : s.connection === 'offline' ? 'Network offline · observations retained' : s.connection === 'auth-required' ? 'Observations retained · reconnect after sign in' : s.health.nextRetryAt ? `Retry in ${Math.max(0, Math.ceil((s.health.nextRetryAt - now) / 1000))}s · HTTP recovery active` : 'Recovering authoritative snapshots'}</small>}{active && s.connection === 'recovering' && <button className="btn" onClick={reconnectSimulation}>Reconnect now</button>}</div></header>
    {s.run && <div className="simulation-run-links"><Link to={`/simulate?run=${s.run.id}`}>Permalink to this run ↗</Link><Link to={`/results?run=${s.run.id}`}>Saved evidence and comparisons ↗</Link></div>}
    <section className="run-controls" aria-label="Simulation configuration"><label>Complete rounds<input aria-label="Simulation spins" type="number" min="1" max="10000000" step="10000" value={samples} disabled={active} onChange={e => setSamples(Number(e.target.value))} /></label>
      <label>Replay seed<input id="run-seed" type="number" value={seed} disabled={active} onChange={e => setSeed(Number(e.target.value))} /></label>
      <label>Workers<select aria-label="Simulation workers" value={workers} disabled={active} onChange={e => setWorkers(Number(e.target.value))}>{[1, 2, 3, 4].map(n => <option key={n} value={n}>{n} {n === 1 ? 'worker' : 'workers'}</option>)}</select></label>
      <div className="run-budget"><strong>Sampled</strong><span>5-minute execution budget<br />Deterministic chunk reduction</span></div>
      <div className="run-actions">{active ? <button className="btn cancel-run" disabled={status === 'cancelling'} onClick={() => void cancelSimulation()}>{status === 'cancelling' ? 'Cancelling…' : s.starting ? 'Cancel launch' : '■ Cancel run'}</button> : <button className="btn primary start-run" disabled={loadingRun} onClick={() => { setQuery({}, { replace: true }); void startSimulation(seed, samples, workers, execution); }}>▶ {s.run ? 'Start new run' : 'Start run'}</button>}<button className="btn" disabled={!s.run || exporting} onClick={() => void download()}>{exporting ? 'Preparing evidence…' : '↓ Export evidence'}</button></div>
    </section>
    {exporting && <p role="status" className="measurement-next-note">{exportState?.message} <button className="btn" onClick={() => { exportRequest.current?.abort(); setExportState(null); }}>Cancel export</button></p>}
    {exportError && <p role="alert" className="simulation-error">{exportError} <button className="btn" onClick={() => void download()}>Retry export</button></p>}
    <ExecutionConfiguration value={execution} change={setExecution} disabled={active} workers={workers} setWorkers={setWorkers} rounds={samples} />
    <ExecutionReport value={p?.execution} />
    {requestedRun && requestedRun !== selectedRunId && active && <p className="measurement-next-note">Another simulation is active. Finish or cancel it before opening this linked run. <Link to={`/results?run=${encodeURIComponent(requestedRun)}`}>Inspect its saved results ↗</Link></p>}
    {loadingRun && <p role="status" className="measurement-next-note">Opening the pinned run and its measurement plan…</p>}
    {queryError && <p role="alert" className="simulation-error">{queryError} <button className="btn" onClick={() => void linked.refetch()}>Retry saved run</button></p>}
    {!active && <p className="simulation-next-model">Next run uses the current constructor draft: <strong>{currentName || "Dog House · 98%"}</strong>. Saved runs retain their pinned graph and collection plan.</p>}
    {s.launchNote && <p role="status" className="measurement-next-note">{s.launchNote}</p>}
    {s.error && <div className="simulation-error" role="alert">{s.error}</div>}
    <section className="simulation-progress" aria-label="Run progress"><div><strong data-testid="sample-count">{p?.sampleCount.toLocaleString() ?? '0'}</strong><span> / {(p?.totalSamples || samples).toLocaleString()} rounds</span><b>{percent.toFixed(1)}%</b></div><div className="run-progress-track" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent}><div style={{ width: `${percent}%` }} /></div><footer><span>{status === 'pending' ? 'Queued for a worker' : hasData ? `${duration(p.elapsedMs)} elapsed` : 'No observations yet'}</span><span>{active && rate > 0 ? `~${duration((p!.totalSamples - p!.sampleCount) / rate * 1000)} remaining` : status === 'cancelled' ? 'Partial result · stopped by cancellation or execution budget' : 'Full game graph pinned at launch'}</span></footer></section>
    <MeasurementWorkspace run={s.run} values={p?.measurements} points={s.points} active={active} />
    <ReferenceWorkbench key={s.run?.id ?? 'draft'} runId={s.run?.id} />
    <SamplePlanning />
    <section className="simulation-metrics" aria-label="Live metrics">
      {visible('rtp') && <Metric label="Observed RTP" value={hasData ? pct(p.runningRtp) : '—'} detail={reference != null ? `Exact reference ${pct(reference)}` : target != null ? `Target ${pct(target)}` : 'Per stake, complete round'} accent="blue" testid="live-rtp" />}
      {visible('precision') && <Metric label="95% CI half-width" value={hasVariance ? `±${formatNumber(1.96 * p.stdErr * 100, 3)} pp` : '—'} detail={p?.execution?.carriesState ? "Dependent rounds · use session inference" : hasData && !hasSampleVariance ? "Requires at least 2 rounds" : hasSampleVariance && p.stdErr === 0 ? "Zero observed variance · rare payouts unresolved" : "Normal approximation · not a guarantee"} accent="violet" />}
      {visible('hit') && <Metric label="Hit frequency" value={hasData ? pct(p.hitFrequency) : '—'} detail={hasData ? `${p.nonZeroCount.toLocaleString()} winning rounds` : 'Rounds with payout > 0'} testid="live-hit-frequency" />}
      {visible('max') && <Metric label="Maximum observed" value={hasData ? `${formatNumber(p.maxWin, 2)}×` : '—'} detail={hasData ? `${p.capHits.toLocaleString()} cap reaches` : 'Stake multiples'} />}
      {visible('speed') && <Metric label="Throughput" value={rate ? `${count(rate)}/s` : '—'} detail={s.run ? `${s.run.degreeOfParallelism} workers · fixed PRNG streams` : 'Actual worker speed'} accent="mint" testid="live-throughput" />}
      {visible('volatility') && <Metric label="Payout volatility" value={hasSampleVariance ? `${formatNumber(p.volatility, 3)}×` : '—'} detail="Sample standard deviation" />}
    </section>
    {visible('convergence') && <section className="simulation-card convergence-card"><div className="simulation-card-head"><div><h2>RTP convergence</h2><p>Each observation aggregates completed rounds. Hover or use arrow keys to inspect.</p></div><div className="chart-key"><span className="key-blue">Observed</span>{!p?.execution?.carriesState && <span className="key-band">95% CI</span>}{target != null && <span className="key-target">Target {pct(target)}</span>}{reference != null && <span className="key-mint">Exact {pct(reference)}</span>}</div></div><LiveChart points={s.points} inference={!p?.execution?.carriesState} reference={reference} target={target} /></section>}
    <div className="simulation-two-columns">{visible('distribution') && <section className="simulation-card"><div className="simulation-card-head"><div><h2>Payout distribution</h2><p>Real observed wins, streamed during execution.</p></div><span className="observed-tag">LIVE COUNTS</span></div><LiveHistogram progress={p} /></section>}
      {visible('reference') && <section className="simulation-card verification-card"><div className="simulation-card-head"><div><h2>Reference check</h2><p>Target and mathematical evidence are distinct.</p></div><span className={`reference-state ${inBand === true ? 'inside' : ''}`}>{inBand === null ? 'Awaiting evidence' : inBand ? 'Reference inside CI' : 'Reference outside CI'}</span></div><dl><div><dt>Target RTP</dt><dd>{target != null ? pct(target) : '—'}</dd></div><div><dt>Exact expectation</dt><dd className="mint">{reference != null ? pct(reference) : '—'}</dd></div><div><dt>Observed RTP</dt><dd className="blue">{hasData ? pct(p.runningRtp) : '—'}</dd></div><div><dt>95% confidence interval</dt><dd>{hasVariance ? `${pct(p.runningRtp - 1.96 * p.stdErr)} – ${pct(p.runningRtp + 1.96 * p.stdErr)}` : '—'}</dd></div><div><dt>Deviation from reference</dt><dd>{hasData && reference != null ? `${formatNumber((p.runningRtp - reference) * 100, 3)} pp` : '—'}</dd></div></dl><p className="reference-note">{s.referenceNote} Confidence intervals describe sampling uncertainty; high volatility can require millions of rounds.</p><Link to="/build">Open the visual constructor ↗</Link></section>}</div>
    <div className="simulation-two-columns compact-charts">{visible('throughput') && <section className="simulation-card"><div className="simulation-card-head"><h2>Worker throughput</h2><span>rounds / second</span></div><LiveChart points={s.points} kind="rate" /></section>}{visible('uncertainty') && !p?.execution?.carriesState && <section className="simulation-card"><div className="simulation-card-head"><h2>Sampling precision</h2><span>95% CI half-width · percentage points</span></div><LiveChart points={s.points.filter(point => point.n > 1 && point.stdErr > 0)} kind="precision" /></section>}</div>
    <div className="simulation-two-columns"><section className="simulation-card run-evidence"><div className="simulation-card-head"><h2>Reproducibility</h2><span>PINNED INPUT</span></div><dl><div><dt>Run / config version</dt><dd>{s.run ? `#${s.run.id} / v${s.run.configVersion}` : '—'}</dd></div><div><dt>Seed / logical stream</dt><dd>{s.run ? `${s.run.seed} / ${s.run.streamScheme}` : '—'}</dd></div><div><dt>Graph SHA-256</dt><dd className="graph-hash">{s.run?.configHash ?? 'Available after launch'}</dd></div></dl></section><section className="simulation-card"><div className="simulation-card-head"><h2>Run activity</h2><span>{s.events.length} events</span></div><ol className="run-event-log">{s.events.length ? s.events.toReversed().map((event, i) => <li key={`${event.time}-${i}`}><time>{event.time}</time><span>{event.text}</span></li>) : <li><span>Launch a run to see worker and connection events.</span></li>}</ol></section></div>
    {!!s.history.length && <section className="simulation-card"><div className="simulation-card-head"><h2>Recent results</h2><span>Saved in this browser · server results retained</span></div><div className="run-history">{s.history.map(run => <button key={run.id} disabled={active} onClick={() => { setQuery({}, { replace: true }); void openSimulation(run); }}><strong>Run #{run.id}</strong><span>{run.status}</span><span>Seed {run.seed}</span><span>{run.progress?.sampleCount.toLocaleString()} rounds</span><b>{run.progress?.sampleCount ? pct(run.progress.runningRtp) : '—'}</b></button>)}</div></section>}
  </div></div>;
}
function Metric({ label, value, detail, accent = '', testid }: { label: string; value: string; detail: string; accent?: string; testid?: string }) {
  return <div className={`simulation-metric ${accent}`}><span>{label}</span><strong data-testid={testid}>{value}</strong><small>{detail}</small></div>;
}
