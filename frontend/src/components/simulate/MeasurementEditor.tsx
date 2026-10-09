import { MeasurementDesigner } from './MeasurementDesigner';
import { useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { peekRootProject } from '../../lib/projectFiles';
import { createDogHouseGraph } from '../../games/doghouse/graph';
import { useAppStore } from '../../store';
import { definition, metricExpression, newMetric, reducers, reducerNames, type MetricDraft, type MeasurementSchema } from '../../lib/measurements/model';
import ExpressionTreeEditor from '../editor/ExpressionTreeEditor';
import { saveMetric } from '../../lib/measurements/store';

export function MeasurementEditor({ initial, close }: { initial?: MetricDraft; close(): void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [draft, setDraft] = useState<MetricDraft>(() => initial ?? newMetric()), [error, setError] = useState(''), [search, setSearch] = useState(''), [busy, setBusy] = useState(false);
  // Reading the schema neither starts a simulation nor navigates out of a mechanic.
  const [config] = useState(() => useAppStore.getState().verificationSource ?? (useAppStore.getState().nodes.length ? peekRootProject() : createDogHouseGraph()));
  const schema = useQuery<MeasurementSchema>({ queryKey: ['measurement-schema', config], queryFn: async ({ signal }) => {
    const response = await fetch('/api/runs/measurements/schema', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ config }), signal });
    const value = await response.json();
    if (!response.ok) throw new Error(value.errors?.map((e: { message: string }) => e.message).join('; ') ?? value.error ?? `HTTP ${response.status}`);
    return value;
  }, retry: false, staleTime: Infinity });
  useEffect(() => { const element = dialog.current!; element.showModal(); return () => element.close(); }, []);
  const update = (patch: Partial<MetricDraft>) => { setDraft(d => ({ ...d, ...patch })); setError(''); };
  const changeExpressionMode = (part: 'value' | 'filter', mode: MetricDraft['valueMode'] | MetricDraft['filterMode']) => {
    try {
      const current = part === 'value' ? draft.valueMode : draft.filterMode;
      const text = part === 'value' ? draft.expression : draft.filter;
      const next = mode === 'visual' ? JSON.stringify(text.trim() ? metricExpression(text, current === 'payout' || current === 'all' ? 'expression' : current) :
        { exprType: 'constant', kind: part === 'filter' || draft.options?.source === 'event' ? 'Boolean' : 'Integer', value: part === 'filter' ? 'true' : draft.options?.source === 'event' ? 'false' : '0' }) : text;
      if (part === 'value') update({ valueMode: mode as MetricDraft['valueMode'], expression: next });
      else update({ filterMode: mode as MetricDraft['filterMode'], filter: next });
    } catch (err) { setError(`Fix the current expression before changing editor: ${err instanceof Error ? err.message : 'Invalid expression.'}`); }
  };
  const expressionSource = !draft.options || ['value', 'event'].includes(draft.options.source);
  const points = schema.data?.points.filter(p => `${p.label} ${p.nodeId}`.toLowerCase().includes(search.toLowerCase())) ?? [];
  let preview = '', syntax = '';
  try { const d = definition(draft); preview = `${d.nodeId ? 'Every visit to ' + d.nodeId : 'Once per completed round'} · ${d.filter ? 'matching the filter' : 'all observations'} · ${draft.reducers.map(r => reducerNames[r]).join(', ')}`; }
  catch (e) { syntax = e instanceof Error ? e.message : 'Invalid measurement.'; }
  return <dialog ref={dialog} className="measurement-dialog" aria-labelledby="measurement-title" onCancel={e => { if (busy) e.preventDefault(); else close(); }} onClose={close}>
    <form onSubmit={async e => { e.preventDefault(); try {
      definition(draft);
      if (!schema.data) throw new Error('Load the graph schema before saving a measurement.');
      if (draft.nodeId && !schema.data.points.some(p => p.nodeId === draft.nodeId)) throw new Error('Choose an observation point in this graph.');
      setBusy(true);
      const response = await fetch('/api/runs/measurements/schema', { method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ config, measurements: [definition(draft)] }), signal: AbortSignal.timeout(10000) });
      const value = await response.json();
      if (!response.ok) throw new Error(value.errors?.map((e: { message: string }) => e.message).join('; ') ?? value.error ?? `HTTP ${response.status}`);
      saveMetric(draft); close();
    } catch (err) { setError(err instanceof Error ? err.message : 'Invalid measurement.'); setBusy(false); } }}>
      <header><div><span className="sim-eyebrow">COLLECTION PLAN / NEXT RUN</span><h2 id="measurement-title">{initial ? 'Edit measurement' : 'Track a metric'}</h2></div><button type="button" className="btn" onClick={close} disabled={busy} aria-label="Close measurement editor">✕</button></header>
      <p className="measurement-intro">Choose where to observe, what to measure, and which observations belong in the statistic. The plan is pinned at launch.</p>
      <fieldset className="measurement-form-grid" disabled={busy}><label className="measurement-wide">Metric name<input aria-label="Metric name" maxLength={80} value={draft.name} onChange={e => update({ name: e.target.value })} placeholder="Sticky FS payout" required /></label>
      <fieldset className="measurement-wide"><legend>1 · Observation point</legend><label>Observe<select aria-label="Metric observation level" disabled={!schema.data} value={draft.nodeId ? 'node' : 'round'} onChange={e => update({ nodeId: e.target.value === 'node' ? schema.data?.points[0]?.nodeId ?? '' : '', valueMode: e.target.value === 'node' ? 'expression' : 'payout' })}><option value="round">Completed paid round</option><option value="node" disabled={!schema.data?.points.length}>Each visit to a graph node</option></select></label>
      {draft.nodeId && <><label>Find a node<input aria-label="Find measurement node" value={search} onChange={e => setSearch(e.target.value)} placeholder="Search name or path, e.g. free-spin" /></label><label>Node<select aria-label="Metric graph node" value={draft.nodeId} onChange={e => update({ nodeId: e.target.value })}>{!points.some(p => p.nodeId === draft.nodeId) && <option value={draft.nodeId}>{draft.nodeId}</option>}{points.map(p => <option key={p.nodeId} value={p.nodeId}>{p.label} · {p.nodeId}</option>)}</select></label><small>Reads state immediately before this node executes. A node in a loop can produce several observations per paid round. Choose the next node after a value is written.</small></>}
      {!draft.nodeId && <small>One observation per complete paid round, including the full bonus. Settled payout includes the win cap; state expressions retain their authored units.</small>}
      {schema.isPending && <p role="status">Compiling the graph schema…</p>}{schema.error && <p role="alert">{schema.error.message} <button type="button" className="btn" onClick={() => void schema.refetch()}>Retry schema</button></p>}
      </fieldset>
      <MeasurementDesigner draft={draft} schema={schema.data} update={update} />
      <fieldset className="measurement-wide"><legend>2 · Observed value</legend>{expressionSource && <label>Value source<select aria-label="Metric value source" value={draft.valueMode} onChange={e => changeExpressionMode('value', e.target.value as MetricDraft['valueMode'])}>{!draft.nodeId && <option value="payout">Settled round payout · stake multiples</option>}<option value="expression">State expression</option><option value="visual">Visual expression builder</option><option value="ast">Advanced · expression AST</option></select></label>}
      {expressionSource && draft.valueMode === 'visual' && <ExpressionTreeEditor allowSettlement label="Metric value" value={metricExpression(draft.expression, 'visual')} onChange={value => update({ expression: JSON.stringify(value) })} />}
      {expressionSource && draft.valueMode !== 'payout' && draft.valueMode !== 'visual' && <label>{draft.valueMode === 'ast' ? 'Numeric expression AST' : draft.options?.source === 'event' ? 'Boolean event predicate' : 'Numeric expression'}<textarea aria-label="Metric numeric expression" rows={draft.valueMode === 'ast' ? 7 : 2} value={draft.expression} onChange={e => update({ expression: e.target.value })} placeholder={draft.valueMode === 'ast' ? '{ "exprType": "fieldAccess", "target": "state", "path": ["spinWin"] }' : 'state.spinWin / state.bet'} required /></label>}
      {expressionSource && draft.valueMode === 'expression' && <label>Insert numeric field<select aria-label="Insert metric value field" value="" onChange={e => { if (e.target.value) update({ expression: e.target.value.startsWith('measurement.') ? e.target.value : `state[${JSON.stringify(e.target.value)}]` }); }}><option value="">Choose a field from this graph…</option>{!draft.nodeId && ['payout', 'rawPayout', 'capDeduction', 'cost', 'net'].map(key => <option key={key} value={`measurement.${key}`}>measurement.{key} · read-only settlement</option>)}{schema.data?.fields.filter(f => f.type === 'Number').map(f => <option key={f.name}>{f.name}</option>)}</select></label>}
      <label>Display unit<input aria-label="Metric unit" maxLength={24} value={draft.unit} onChange={e => update({ unit: e.target.value })} placeholder="coins, × stake, spins…" /></label><small>Units are labels. Normalize the value in the expression when measuring a ratio or stake multiple. AST mode supports the constructor's full bounded expression grammar, including folds.</small></fieldset>
      <fieldset className="measurement-wide"><legend>3 · Scope / filter</legend><label>Include<select aria-label="Metric filter mode" value={draft.filterMode} onChange={e => changeExpressionMode('filter', e.target.value as MetricDraft['filterMode'])}><option value="all">All observations at this point</option><option value="expression">Only matching a Boolean expression</option><option value="visual">Visual Boolean builder</option><option value="ast">Advanced · Boolean expression AST</option></select></label>
      {draft.filterMode === 'visual' && <ExpressionTreeEditor allowSettlement label="Metric filter" value={metricExpression(draft.filter, 'visual')} onChange={filter => update({ filter: JSON.stringify(filter) })} />}
      {draft.filterMode !== 'all' && draft.filterMode !== 'visual' && <><label>Filter<textarea aria-label="Metric filter expression" rows={draft.filterMode === 'ast' ? 6 : 2} value={draft.filter} onChange={e => update({ filter: e.target.value })} placeholder='state.fsType == "sticky" && state.multiplier >= 2' required /></label><label>Insert scope field<select aria-label="Insert metric scope field" value="" onChange={e => { const field = schema.data?.fields.find(f => f.name === e.target.value); if (field) update({ filter: `state[${JSON.stringify(field.name)}] ${field.type === 'String' ? '== "sticky"' : field.type === 'Boolean' ? '== true' : '> 0'}` }); }}><option value="">Choose a field to filter…</option>{schema.data?.fields.filter(f => f.type !== 'Array').map(f => <option key={f.name} value={f.name}>{f.name} · {f.type}</option>)}</select></label></>}
      <small>For one FS type, filter the field your graph uses for that type. Filters run before the value expression. Average uses matching observations; skipped and invalid values never become zero.</small></fieldset>
      <fieldset className="measurement-wide"><legend>4 · Presentation</legend><div className="measurement-reducers">{reducers.map(r => <label key={r}><input type="checkbox" disabled={!draft.options && reducers.indexOf(r) >= 7} checked={draft.reducers.includes(r)} onChange={e => update({ reducers: e.target.checked ? [...draft.reducers, r] : draft.reducers.filter(v => v !== r) })} />{reducerNames[r]}</label>)}</div><label className="measurement-checkbox"><input type="checkbox" checked={draft.chart} onChange={e => update({ chart: e.target.checked })} />Show cumulative average chart (observed range available)</label><small>Statistics are always collected together. You can change the displayed statistics while running. Matching share uses visits at this observation point as its denominator.</small></fieldset></fieldset>
      {(error || syntax && draft.name) && <p className="measurement-error" role="alert">{error || syntax}</p>}
      {preview && <p className="measurement-preview">{preview}</p>}
      <footer><button type="button" className="btn" onClick={close} disabled={busy}>Cancel</button><button className="btn primary" type="submit" disabled={busy || schema.isPending || !schema.data}>{busy ? 'Validating…' : 'Save measurement'}</button></footer>
    </form>
  </dialog>;
}
