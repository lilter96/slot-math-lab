import { projectContentScope } from '../../lib/projectFiles';
import RawNumber from './RawNumber';
import { tableKeyboard } from '../../lib/tableKeyboard';
import { useAppStore } from '../../store';
import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { weightedSources, validateWeights, changeWeights, type DrawSource } from '../../lib/modelCommands';
import { useModelProjection } from '../../hooks/useModelProjection';
import { peekRootProject, rootProject, loadProject, openMechanic } from '../../lib/projectFiles';
import { canonicalJson } from '../../lib/hash';
import { graphRequest } from '../../lib/graphApi';
import type { ModelGraph } from '../../lib/modelProjection';
import { importWeightsCsv, weightsCsv } from '../../lib/weightCsv';

const context = projectContentScope;

const staged = new Map<string, { base: ModelGraph; weights: string[] }>();

function DrawTransaction({ graph, source }: { graph: ModelGraph; source: DrawSource }) {
  const draftKey = `${useAppStore(s => s.projectId)}:${graph.id ?? graph.name}:${source.key}`;
  const [weights, setWeights] = useState(() => staged.get(draftKey)?.weights ?? source.rows.map(row => String(row.weight)));
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const file = useRef<HTMLInputElement>(null);
  const mounted = useRef(false), csvTask = useRef(0), pending = useRef<AbortController | null>(null);
  useEffect(() => {
    mounted.current = true;
    let scope = context();
    const unsubscribe = useAppStore.subscribe((current, previous) => {
      if (current.projectId === previous.projectId && current.graphTrail === previous.graphTrail && current.nodes === previous.nodes && current.edges === previous.edges && current.tables === previous.tables) return;
      const next = context(); if (next === scope) return; scope = next;
      csvTask.current++; pending.current?.abort(); pending.current = null; setBusy(false);
    });
    return () => { mounted.current = false; csvTask.current++; pending.current?.abort(); pending.current = null; unsubscribe(); };
  }, []);
  const [csvPreview, setCsvPreview] = useState(false);
  const invalid = validateWeights(weights);
  const changed = source.rows.some((row, index) => String(row.weight) !== weights[index]);
  const sum = invalid ? 0 : weights.reduce((total, weight) => total + Number(weight), 0);
  const symbols = graph.symbols as { id: string; name: string }[] | undefined;
  // Keep an immutable base for the transaction; newer graph edits must never be overwritten.
  const [base, setBase] = useState(() => staged.get(draftKey)?.base ?? graph);
  const [undo, setUndo] = useState<{ previous: ModelGraph; committed: string } | null>(null);
  const commit = async () => {
    csvTask.current++; pending.current?.abort();
    const controller = new AbortController(), origin = context(); pending.current = controller;
    const current = () => mounted.current && pending.current === controller && !controller.signal.aborted && origin === context();
    setBusy(true); setError('');
    try {
      const next = changeWeights(base, source.key, weights);
      const checked = await graphRequest<{ isValid: boolean; errors: { message: string }[] }>('validate', { config: next }, controller.signal);
      if (!current()) return;
      if (!checked.isValid) throw new Error(checked.errors.map(item => item.message).join('; '));
      if (canonicalJson(peekRootProject()) !== canonicalJson(base)) throw new Error('The model changed while this table was open. Reset the table before applying.');
      pending.current = null; setBusy(false);
      rootProject(); loadProject(next);
      const committed = peekRootProject() as ModelGraph;
      setUndo({ previous: base, committed: canonicalJson(committed) }); setBase(committed); staged.delete(draftKey); setCsvPreview(false);
    } catch (reason) { if (current()) setError(reason instanceof Error ? reason.message : String(reason)); }
    finally { if (pending.current === controller) { pending.current = null; setBusy(false); } }
  };
  return <section className="weighted-editor" aria-label="Weighted outcome editor">
    <p className="source-path"><strong>Source</strong> {source.scope ? `mechanics[${JSON.stringify(source.scope)}]` : 'Full graph'}.nodes[{source.nodeId}].drawWeights</p>
    <p className="hint">Edit relative weights for this draw only. The probability preview is weight / total weight; it is not game RTP. Outcome IDs and payloads stay attached to their original rows.</p>
    <div className="data-view-switch"><button className="btn" disabled={busy || !!invalid} onClick={() => { const url = URL.createObjectURL(new Blob([weightsCsv(source.rows, weights)], { type: 'text/csv' })); const link = document.createElement('a'); link.href = url; link.download = 'draw-weights.csv'; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000); }}>Download weights CSV</button><button className="btn" disabled={busy} onClick={() => file.current?.click()}>Preview weights CSV</button><input ref={file} aria-label="Weights CSV file" type="file" accept=".csv,text/csv" hidden onChange={async event => { const selected = event.target.files?.[0]; if (!selected) return; event.target.value = ''; const token = ++csvTask.current, origin = context(); const current = () => mounted.current && token === csvTask.current && origin === context(); try { const text = await selected.text(); if (!current()) return; const next = importWeightsCsv(text, source.rows); setWeights(next); staged.set(draftKey, { base, weights: next }); setCsvPreview(true); setError(''); } catch (reason) { if (current()) setError(reason instanceof Error ? reason.message : String(reason)); } }} /></div>
    {csvPreview && <p role="status">CSV preview staged. Review the table and change summary, then Validate & apply weights to confirm. The model is unchanged until you apply.</p>}
    <div className="math-table-scroll"><table className="math-data-table" onKeyDown={tableKeyboard}><thead><tr><th>Outcome</th><th>Payload</th><th>Weight</th><th>Probability preview</th></tr></thead><tbody>{source.rows.map((row, index) => <tr key={row.outcomeId}><th scope="row">{symbols?.find(symbol => symbol.id === row.outcomeId)?.name ?? row.outcomeId}<small>ID {row.outcomeId}</small></th><td><RawNumber value={String(row.value)} label={`payload for ${row.outcomeId}`} /></td><td><input className="inp" inputMode="numeric" aria-label={`Weight for ${row.outcomeId}`} aria-invalid={!!invalid} data-changed={String(row.weight) !== weights[index]} value={weights[index]} disabled={busy} onChange={event => { csvTask.current++; setCsvPreview(false); const value = event.target.value; const next = weights.map((weight, i) => i === index ? value : weight); setWeights(next); staged.set(draftKey, { base, weights: next }); }} /></td><td>{invalid ? '—' : <RawNumber value={`${weights[index]}/${sum}`} label={`probability for ${row.outcomeId}`} display={`${(Number(weights[index]) / sum * 100).toFixed(4)}%`} detail={`${weights[index]} / ${sum}`} />}{invalid && <small>Fix weights to calculate</small>}</td></tr>)}</tbody><tfoot><tr><th colSpan={2}>Total weight</th><td>{invalid ? '—' : sum.toLocaleString()}</td><td>{invalid ? '—' : '100%'}</td></tr></tfoot></table></div>
    {changed && !invalid && <p className="hint">Unapplied changes: {source.rows.flatMap((row, index) => String(row.weight) !== weights[index] ? [`${row.outcomeId}: ${row.weight} → ${weights[index]}`] : []).join(', ')}. They are retained when you switch workspaces.</p>}
    {invalid && <p className="inline-validation" role="alert">{invalid}</p>}{error && <p className="inline-validation" role="alert">{error}</p>}
    <div className="editor-actions"><button className="btn primary" disabled={!changed || !!invalid || busy} onClick={() => void commit()}>{busy ? 'Validating…' : 'Validate & apply weights'}</button><button className="btn" disabled={busy} onClick={() => { csvTask.current++; setWeights(source.rows.map(row => String(row.weight))); setBase(graph); setError(''); staged.delete(draftKey); setCsvPreview(false); }}>Reset weights</button>{undo && <button className="btn" disabled={busy} onClick={() => {
      if (canonicalJson(peekRootProject()) !== undo.committed) { setError('The model changed after applying. Undo would overwrite newer edits.'); return; }
      rootProject(); loadProject(undo.previous); setBase(peekRootProject() as ModelGraph); staged.delete(draftKey); setWeights(undo.previous ? weightedSources(undo.previous).find(item => item.key === source.key)!.rows.map(row => String(row.weight)) : []); setUndo(null);
    }}>Undo weight change</button>}
    <Link className="btn" to="/build" onClick={() => { rootProject(); if (source.scope) openMechanic(source.scope); }}>Open source graph</Link></div>
    {undo && <p role="status">Weights applied to the canonical model. Export and simulation use these values.</p>}
  </section>;
}

export default function WeightedDrawEditor() {
  const graph = useModelProjection();
  const projectId = useAppStore(s => s.projectId);
  const [sourceKey, setSourceKey] = useState('');
  const sources = graph ? weightedSources(graph) : [];
  const source = sources.find(item => item.key === sourceKey) ?? sources[0];
  if (!graph || !source) return <div className="model-notice"><h2>No supported weighted table</h2><p>This editor needs finite inline integer weights and payloads representable exactly in the browser. Dynamic expressions and other numeric representations need a compatible source editor.</p><Link className="btn" to="/build">Open logic graph</Link></div>;
  return <><label className="draw-source-select">Weighted draw<select className="inp" aria-label="Weighted draw source" value={source.key} onChange={event => setSourceKey(event.target.value)}>{sources.map(item => <option key={item.key} value={item.key}>{item.label}</option>)}</select></label><DrawTransaction key={`${projectId}:${source.key}`} graph={graph} source={source} /></>;
}
