import { MeasurementCalibration } from './MeasurementCalibration';
import { MeasurementWitnesses } from './MeasurementWitnesses';
import { MetricCatalogue } from './MetricCatalogue';
import { GraphMeasurementReference } from './GraphMeasurementReference';
import { MeasurementAnalysis } from './MeasurementAnalysis';
import { useMemo, useState } from 'react';
import type { LivePoint, RunSnapshot } from '../../hooks/useSimulation';
import { useMeasurementWorkspace, widgets, saveMetric, removeMetric } from '../../lib/measurements/store';
import { samePlan, definition, draftFromDefinition, reducers, reducerNames, statistic, formatStatistic, measurementPopulation, type MetricDraft, type MeasurementDefinition, type MeasurementSnapshot } from '../../lib/measurements/model';
import { MeasurementEditor } from './MeasurementEditor';
import { MeasurementChart } from './MeasurementChart';

const emptyPlan: MeasurementDefinition[] = [];

export function MeasurementWorkspace({ run, values, points, active }: { run: RunSnapshot | null; values?: MeasurementSnapshot[]; points: LivePoint[]; active: boolean }) {
  const workspace = useMeasurementWorkspace(), [editor, setEditor] = useState<MetricDraft | 'new' | null>(null), [display, setDisplay] = useState(false);
  const pinned = run?.measurements ?? emptyPlan;
  const hasRun = !!run;
  const pending = useMemo(() => !samePlan(workspace.metrics.map(definition), pinned), [workspace.metrics, pinned]);
  const [presentations, setPresentations] = useState<Record<string, Partial<MetricDraft>>>({});
  const source = useMemo(() => hasRun ? pinned.map(d => {
    const saved = workspace.metrics.find(m => m.id === d.id);
    return { ...draftFromDefinition(d), ...(saved ? { reducers: saved.reducers, chart: saved.chart, hidden: saved.hidden } : {}), ...presentations[d.id] };
  }) : workspace.metrics, [hasRun, pinned, workspace.metrics, presentations]);
  function present(metric: MetricDraft, patch: Partial<MetricDraft>) {
    setPresentations(p => ({ ...p, [metric.id]: { ...p[metric.id], ...patch } }));
    const saved = workspace.metrics.find(m => m.id === metric.id); if (saved) saveMetric({ ...saved, ...patch });
  }
  return <section className="measurement-workspace" aria-label="Measurement workspace"><div className="measurement-workspace-head"><div><span className="sim-eyebrow">YOUR MEASUREMENT WORKSPACE</span><h2>Track what matters</h2><p>Observe a numeric value anywhere in the graph. Scope it to the feature or FS type you need.</p></div><div className="measurement-actions"><button className="btn" onClick={() => setDisplay(v => !v)} aria-expanded={display}>Customize dashboard</button><button className="btn primary" disabled={workspace.metrics.length >= 32} onClick={() => setEditor('new')}>＋ Track metric</button></div></div>
    <MetricCatalogue configure={setEditor} limitReached={workspace.metrics.length >= 32} runId={run?.id} />
    {run && !active && <GraphMeasurementReference key={run.id} run={run} />}
    {display && <div className="measurement-display"><strong>Display now</strong><p>Change the dashboard without restarting collection. Connection health and run controls stay visible.</p><div className="measurement-reducers">{widgets.map(w => <label key={w.id}><input type="checkbox" checked={!workspace.hiddenWidgets.includes(w.id)} onChange={e => useMeasurementWorkspace.setState(s => ({ hiddenWidgets: e.target.checked ? s.hiddenWidgets.filter(id => id !== w.id) : [...s.hiddenWidgets, w.id] }))} />{w.name}</label>)}</div>{source.map(metric => <label className="measurement-checkbox" key={metric.id}><input type="checkbox" checked={!metric.hidden} onChange={e => present(metric, { hidden: !e.target.checked })} />{metric.name}</label>)}<button className="btn" onClick={() => { useMeasurementWorkspace.setState({ hiddenWidgets: [] }); source.forEach(m => present(m, { hidden: false })); }}>Show all widgets</button></div>}
    {!!run && pending && <div className="measurement-next-note">The saved run keeps its original collection plan. Your edited plan applies to the next run.</div>}
    {!source.length && <div className="measurement-empty"><div><strong>Need min, max and average for just one free-spin type?</strong><p>Choose a node in the FS sequence, a payout expression, and a filter such as <code>state.fsType == "sticky"</code>. The fields come from your constructor model.</p></div><button className="btn" onClick={() => setEditor('new')}>Configure first metric →</button></div>}
    <div className="measurement-cards">{source.filter(m => !m.hidden).map(metric => <TrackedMetric key={`${run?.id}-${metric.id}`} run={run ?? undefined} runId={run?.id} metric={metric} definition={pinned.find(d => d.id === metric.id)} value={values?.find(v => v.id === metric.id)} points={run ? points : []}
      customize={patch => present(metric, patch)} edit={() => setEditor(workspace.metrics.find(m => m.id === metric.id) ?? metric)} />)}</div>
    {source.length > 0 && source.every(m => m.hidden) && <p className="measurement-next-note">All tracked metric widgets are hidden. Collection continues; restore them in Customize dashboard.</p>}
    <details className="measurement-plan"><summary>Next-run collection plan · {workspace.metrics.length} / 32 metrics{active ? ' · editable while running' : ''}</summary><p>Collection definitions are pinned at launch. Display settings can change immediately. Node visits and paid rounds have different denominators.</p>{workspace.metrics.length ? <ul>{workspace.metrics.map(m => <li key={m.id}><div><strong>{m.name}</strong><span>{m.nodeId || 'Completed paid round'} · {m.filterMode === 'all' ? 'all observations' : 'filtered'} · {m.reducers.map(r => reducerNames[r]).join(', ')}</span></div><button className="btn" onClick={() => setEditor(m)}>Edit</button><button className="btn" onClick={() => removeMetric(m.id)} aria-label={`Remove ${m.name} from next run`}>Remove</button></li>)}</ul> : <p>No additional measurements configured. The standard round metrics are always collected.</p>}
    {run && pinned.length > 0 && <button className="btn" onClick={() => useMeasurementWorkspace.setState({ metrics: pinned.map(d => workspace.metrics.find(m => m.id === d.id) && samePlan([definition(workspace.metrics.find(m => m.id === d.id)!)], [d]) ? workspace.metrics.find(m => m.id === d.id)! : draftFromDefinition(d)) })}>Use this run’s collection plan</button>}</details>
    {run?.measurementHash && <p className="measurement-fingerprint">Pinned measurement SHA-256 <code>{run.measurementHash}</code></p>}
    {editor && <MeasurementEditor initial={editor === 'new' ? undefined : editor} close={() => setEditor(null)} />}
  </section>;
}
function TrackedMetric({ run, runId, metric, definition: pinned, value, points, customize, edit }: { run?: RunSnapshot; runId?: string; metric: MetricDraft; definition?: MeasurementDefinition; value?: MeasurementSnapshot; points: LivePoint[]; customize(patch: Partial<MetricDraft>): void; edit(): void }) {
  const [settings, setSettings] = useState(false);
  return <article className="simulation-card tracked-metric" data-testid={`tracked-metric-${metric.id}`} aria-label={`Tracked metric ${metric.name}`}><div className="simulation-card-head"><div><h3>{metric.name}</h3><p>{metric.options?.subject === 'episode' ? 'Complete feature episodes' : metric.options?.subject === 'round' ? 'Complete paid rounds · absent features included' : metric.nodeId ? `Node visits · ${metric.nodeId}` : 'Completed paid rounds'}{pinned?.filter || metric.filterMode !== 'all' ? ' · filtered scope' : ' · all observations'}</p></div><button className="btn" onClick={() => setSettings(v => !v)} aria-label={`Display settings for ${metric.name}`} aria-expanded={settings}>Display</button></div>
    {settings && <div className="measurement-display"><div className="measurement-reducers">{reducers.map(r => <label key={r}><input type="checkbox" checked={metric.reducers.includes(r)} disabled={metric.reducers.length === 1 && metric.reducers.includes(r) || !metric.options && reducers.indexOf(r) >= 7 || ['covariance', 'correlation', 'ratio', 'meanDifference', 'varianceSum', 'varianceDifference'].includes(r) && !metric.options?.pair && metric.options?.pairRole !== 'wager' && metric.options?.subject !== 'transition' || r === 'eventReciprocal' && metric.options?.source !== 'event'} onChange={e => customize({ reducers: e.target.checked ? [...metric.reducers, r] : metric.reducers.filter(v => v !== r) })} />{reducerNames[r]}</label>)}</div><label className="measurement-checkbox"><input type="checkbox" checked={metric.chart} onChange={e => customize({ chart: e.target.checked })} />Show average / range chart</label><button className="btn" onClick={edit}>Edit collection for next run</button></div>}
    <dl className="tracked-statistics">{metric.reducers.map(r => <div key={r}><dt>{reducerNames[r]}</dt><dd data-statistic={r}>{formatStatistic(statistic(value, r), r, metric.unit)}</dd></div>)}</dl>
    <div className="measurement-counts"><span><b>{(value?.count ?? 0).toLocaleString()}</b> matching</span><span>{(value?.excluded ?? 0).toLocaleString()} excluded</span><span className={value?.errors ? 'measurement-error' : ''}>{(value?.errors ?? 0).toLocaleString()} invalid</span><span>{(value?.observations ?? 0).toLocaleString()} eligible {measurementPopulation(metric)}</span></div>
    {value && !value.count && <p className="measurement-next-note">{value.observations ? 'No matching valid observations. Min, max and average are undefined.' : 'This observation point has not been reached in the completed rounds.'}</p>}
    {value?.errors ? <p className="measurement-error" role="alert">Invalid observations are excluded from statistics. First error: {value.firstError}</p> : null}
    <MeasurementAnalysis analysis={value?.analysis} unit={metric.unit} />
    {run && pinned && <MeasurementCalibration run={run} definition={pinned} />}
    {runId && <MeasurementWitnesses runId={runId} value={value} />}
    {metric.chart && <MeasurementChart id={metric.id} name={metric.name} unit={metric.unit} points={points} />}
    <details className="measurement-definition"><summary>What this measures</summary><p>Observation population: {measurementPopulation(metric)}. {metric.nodeId ? 'Child values are read before this graph node executes.' : 'Values are read at completed paid-round settlement.'} Unfinished paid rounds are discarded. Advanced reductions and lifecycle boundaries belong to the pinned plan.</p><strong>Value</strong><pre>{pinned ? pinned.value ? JSON.stringify(pinned.value, null, 2) : 'Settled round payout (× stake)' : metric.valueMode === 'payout' ? 'Settled round payout (× stake)' : metric.expression}</pre><strong>Include when</strong><pre>{pinned ? pinned.filter ? JSON.stringify(pinned.filter, null, 2) : 'All observations' : metric.filterMode === 'all' ? 'All observations' : metric.filter}</pre><p>Average = sum / matching count. Invalid observations are visible but do not enter the average. Matching share = matching count / eligible observations.</p></details>
  </article>;
}
