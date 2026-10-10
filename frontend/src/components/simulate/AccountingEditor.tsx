import { useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import ExpressionTreeEditor from '../editor/ExpressionTreeEditor';
import { peekRootProject } from '../../lib/projectFiles';
import { createDogHouseGraph } from '../../games/doghouse/graph';
import { useAppStore } from '../../store';
import { useMeasurementWorkspace } from '../../lib/measurements/store';
import { accountingPlan, payoutExpression } from '../../lib/measurements/accounting';
import { definition, type MeasurementSchema } from '../../lib/measurements/model';
import { calculationRequest } from '../../lib/measurements/requests';
import type { ExpressionAst } from '../../lib/expressionParser';

const field = (target: string, name: string): ExpressionAst => ({ exprType: 'fieldAccess', target, path: [name] });
export function AccountingEditor({ close }: { close(): void }) {
  const dialog = useRef<HTMLDialogElement>(null), validation = useRef<AbortController | null>(null);
  const [name, setName] = useState('Payout components'), [nodeId, setNode] = useState(''), [unit, setUnit] = useState('× stake'), [stake, setStake] = useState(1);
  const [total, setTotal] = useState<ExpressionAst>(payoutExpression), [components, setComponents] = useState<{ name: string; value: ExpressionAst | null }[]>([{ name: 'Base', value: null }, { name: 'Feature', value: null }]);
  const [filter, setFilter] = useState<ExpressionAst | null>(null), [group, setGroup] = useState<ExpressionAst | null>(null), [support, setSupport] = useState(32), [groups, setGroups] = useState(4);
  const [busy, setBusy] = useState(false), [error, setError] = useState(''), [status, setStatus] = useState('');
  const [config] = useState(() => useAppStore.getState().verificationSource ?? (useAppStore.getState().nodes.length ? peekRootProject() : createDogHouseGraph()));
  const schema = useQuery<MeasurementSchema>({ queryKey: ['measurement-schema', config], queryFn: ({ signal }) => calculationRequest('/api/runs/measurements/schema', { config }, signal, () => {}, 10000), retry: false, staleTime: Infinity });
  useEffect(() => { const el = dialog.current!; el.showModal(); return () => { validation.current?.abort(); el.close(); }; }, []);
  const dismiss = () => { validation.current?.abort(); close(); };
  const added = components.length + components.length * (components.length - 1) / 2 + 2;
  const update = (i: number, patch: Partial<typeof components[number]>) => setComponents(c => c.map((value, j) => j === i ? { ...value, ...patch } : value));
  return <dialog ref={dialog} className="measurement-dialog" aria-labelledby="accounting-editor-title" onCancel={dismiss} onClose={dismiss}><form onSubmit={async e => {
    e.preventDefault(); try {
      if (!schema.data) throw new Error('Load the graph schema before saving.');
      if (components.some(c => !c.value)) throw new Error('Choose or build a value for every component.');
      const current = useMeasurementWorkspace.getState().metrics;
      const plan = accountingPlan({ name, nodeId: nodeId || null, unit, stake, total, components: components.map(c => ({ name: c.name, value: c.value! })), filter, group, supportLimit: support, groupLimit: groups });
      const proposed = [...current, ...plan.metrics];
      if (proposed.length > 32) throw new Error(`This accounting plan adds ${added} metrics; the complete collection plan exceeds 32. Remove unused measurements or use fewer components.`);
      setBusy(true); setError(''); validation.current = new AbortController();
      await calculationRequest('/api/runs/measurements/schema', { config, measurements: proposed.map(definition) }, validation.current.signal, setStatus, 10000);
      validation.current.signal.throwIfAborted();
      if (useMeasurementWorkspace.getState().metrics !== current) throw new Error('The collection plan changed during validation. Review it before saving again.');
      useMeasurementWorkspace.setState({ metrics: proposed }); close();
    } catch (err) { if (!validation.current?.signal.aborted) { setError(err instanceof Error ? err.message : 'Invalid accounting plan.'); setBusy(false); } }
  }}><header><div><span className="sim-eyebrow">COMPONENT ACCOUNTING / NEXT RUN</span><h2 id="accounting-editor-title">Reconcile payout components</h2></div><button type="button" className="btn" aria-label="Close accounting editor" onClick={dismiss}>✕</button></header>
    <p>Measure total and every component on the same observation. The plan collects every pair covariance and an exact total-minus-components assertion. Define completed feature totals in the constructor before observing their boundary.</p>
    <fieldset className="measurement-form-grid" disabled={busy}>
      <label>Reconciliation name<input aria-label="Reconciliation name" value={name} maxLength={64} required onChange={e => setName(e.target.value)} /></label>
      <label>Common observation point<select aria-label="Accounting observation point" value={nodeId} disabled={!schema.data} onChange={e => setNode(e.target.value)}><option value="">Completed paid-round settlement</option>{schema.data?.points.map(p => <option key={p.nodeId} value={p.nodeId}>{p.label} · {p.nodeId}</option>)}</select></label>
      <label>Unit<input aria-label="Accounting unit" value={unit} maxLength={24} onChange={e => setUnit(e.target.value)} /></label>
      <label>External cost per paid round<input aria-label="Accounting external cost" type="number" min="0.00000001" step="any" value={stake} onChange={e => setStake(Number(e.target.value))} /></label>
      <p className="measurement-wide">{nodeId ? 'State is read before the selected node executes; every component must already be available there.' : 'One observation per completed paid round, including absent-feature zeros and the payout cap.'} Cost uses the same units as the values. Paid turnover includes all completed paid rounds, even when a filter selects a feature population.</p>
      <fieldset className="measurement-wide"><legend>Total payout</legend><AccountingValue label="Accounting total" value={total} change={v => { if (v) setTotal(v); }} schema={schema.data} settlement={!nodeId} /></fieldset>
      <fieldset className="measurement-wide"><legend>Components · {components.length} / 6</legend>{components.map((c, i) => <div className="accounting-component" key={i}><label>Component {i + 1} name<input aria-label={`Component ${i + 1} name`} maxLength={40} value={c.name} required onChange={e => update(i, { name: e.target.value })} /></label><AccountingValue label={`Component ${i + 1}`} value={c.value} change={value => update(i, { value })} schema={schema.data} settlement={!nodeId} />{components.length > 2 && <button type="button" className="btn" onClick={() => setComponents(c => c.filter((_, j) => j !== i))}>Remove component {i + 1}</button>}</div>)}<button type="button" className="btn" disabled={components.length >= 6} onClick={() => setComponents(c => [...c, { name: '', value: null }])}>Add component</button></fieldset>
      <fieldset className="measurement-wide"><legend>Common scope</legend><label className="measurement-checkbox"><input aria-label="Accounting filter enabled" type="checkbox" checked={!!filter} onChange={e => setFilter(e.target.checked ? { exprType: 'constant', kind: 'Boolean', value: 'true' } : null)} />Include only observations matching a predicate</label>{filter && <ExpressionTreeEditor label="Accounting filter" allowSettlement={!nodeId} value={filter} onChange={setFilter} />}
      <label className="measurement-checkbox"><input aria-label="Accounting cohorts enabled" type="checkbox" checked={!!group} onChange={e => setGroup(e.target.checked ? { exprType: 'constant', kind: 'String', value: 'all' } : null)} />Retain a cohort ledger, such as an authored FS type</label>{group && <ExpressionTreeEditor label="Accounting cohort" allowSettlement={!nodeId} value={group} onChange={setGroup} />}</fieldset>
      <label>Distinct values per metric / cohort<input aria-label="Accounting support limit" type="number" min="1" max="1024" value={support} onChange={e => setSupport(Number(e.target.value))} /></label><label>Maximum cohorts<input aria-label="Accounting group limit" type="number" min="1" max="32" value={groups} onChange={e => setGroups(Number(e.target.value))} /></label>
      <p className="measurement-wide">Adds {added} measurements: total, {components.length} components, {components.length * (components.length - 1) / 2} pairs and one exact residual. Pair cards start hidden; their covariance evidence remains available. Full-support retention is bounded; moments remain collected when distinct support fills.</p>
    </fieldset>
    {schema.isPending && <p role="status">Compiling graph schema…</p>}{schema.error && <p className="measurement-error" role="alert">{schema.error.message} <button type="button" className="btn" onClick={() => void schema.refetch()}>Retry schema</button></p>}
    {busy && <p role="status">Validating the complete collection plan · {status}</p>}{error && <p role="alert" className="measurement-error">{error}</p>}
    <footer><button type="button" className="btn" onClick={dismiss}>Cancel</button><button type="submit" className="btn primary" disabled={busy || !schema.data}>{busy ? 'Validating…' : 'Save accounting plan'}</button></footer>
  </form></dialog>;
}
function AccountingValue({ label, value, change, schema, settlement }: { label: string; value: ExpressionAst | null; change(v: ExpressionAst | null): void; schema?: MeasurementSchema; settlement: boolean }) {
  const [visual, setVisual] = useState(false);
  const fields = [...(settlement ? ['payout', 'rawPayout', 'capDeduction', 'cost', 'net'].map(name => ({ key: `measurement:${name}`, title: `measurement.${name}`, value: field('measurement', name) })) : []),
    ...(schema?.fields.filter(f => f.type === 'Number').map(f => ({ key: `state:${f.name}`, title: `state.${f.name}`, value: field('state', f.name) })) ?? [])];
  const selected = !visual && value?.exprType === 'fieldAccess' ? `${value.target}:${(value.path as string[]).join('.')}` : visual ? 'visual' : '';
  return <><label>Value<select aria-label={`${label} value`} value={selected} onChange={e => { if (e.target.value === 'visual') { setVisual(true); change(value ?? field('state', schema?.fields.find(f => f.type === 'Number')?.name ?? 'chooseField')); } else { setVisual(false); change(fields.find(f => f.key === e.target.value)?.value ?? null); } }}><option value="">Choose an authored numeric field…</option>{fields.map(f => <option key={f.key} value={f.key}>{f.title}</option>)}<option value="visual">Build an expression visually…</option></select></label>{visual && value && <ExpressionTreeEditor label={label} value={value} onChange={change} allowSettlement={settlement} />}</>;
}
