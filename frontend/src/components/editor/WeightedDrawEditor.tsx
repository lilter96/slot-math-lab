import { useState } from 'react';
import { Link } from 'react-router-dom';
import { weightedSources, validateWeights, changeWeights, type DrawSource } from '../../lib/modelCommands';
import { useModelProjection } from '../../hooks/useModelProjection';
import { peekRootProject, rootProject, loadProject, openMechanic } from '../../lib/projectFiles';
import { canonicalJson } from '../../lib/hash';
import { graphRequest } from '../../lib/graphApi';
import type { ModelGraph } from '../../lib/modelProjection';

const staged = new Map<string, { base: ModelGraph; weights: string[] }>();

function DrawTransaction({ graph, source }: { graph: ModelGraph; source: DrawSource }) {
  const draftKey = `${graph.id ?? graph.name}:${source.key}`;
  const [weights, setWeights] = useState(() => staged.get(draftKey)?.weights ?? source.rows.map(row => String(row.weight)));
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const invalid = validateWeights(weights);
  const changed = source.rows.some((row, index) => String(row.weight) !== weights[index]);
  const sum = invalid ? 0 : weights.reduce((total, weight) => total + Number(weight), 0);
  const symbols = graph.symbols as { id: string; name: string }[] | undefined;
  // Keep an immutable base for the transaction; newer graph edits must never be overwritten.
  const [base, setBase] = useState(() => staged.get(draftKey)?.base ?? graph);
  const [undo, setUndo] = useState<{ previous: ModelGraph; committed: string } | null>(null);
  const commit = async () => {
    setBusy(true); setError('');
    try {
      const next = changeWeights(base, source.key, weights);
      const checked = await graphRequest<{ isValid: boolean; errors: { message: string }[] }>('validate', { config: next });
      if (!checked.isValid) throw new Error(checked.errors.map(item => item.message).join('; '));
      if (canonicalJson(peekRootProject()) !== canonicalJson(base)) throw new Error('The model changed while this table was open. Reset the table before applying.');
      rootProject(); loadProject(next);
      const committed = peekRootProject() as ModelGraph;
      setUndo({ previous: base, committed: canonicalJson(committed) }); setBase(committed); staged.delete(draftKey);
    } catch (reason) { setError(reason instanceof Error ? reason.message : String(reason)); }
    finally { setBusy(false); }
  };
  return <section className="weighted-editor" aria-label="Weighted outcome editor">
    <p className="source-path"><strong>Source</strong> {source.scope ? `mechanics[${JSON.stringify(source.scope)}]` : 'Full graph'}.nodes[{source.nodeId}].drawWeights</p>
    <p className="hint">Edit relative weights for this draw only. The probability preview is weight / total weight; it is not game RTP. Outcome IDs and payloads stay attached to their original rows.</p>
    <div className="math-table-scroll"><table className="math-data-table"><thead><tr><th>Outcome</th><th>Payload</th><th>Weight</th><th>Probability preview</th></tr></thead><tbody>{source.rows.map((row, index) => <tr key={row.outcomeId}><th scope="row">{symbols?.find(symbol => symbol.id === row.outcomeId)?.name ?? row.outcomeId}<small>ID {row.outcomeId}</small></th><td>{row.value}</td><td><input className="inp" inputMode="numeric" aria-label={`Weight for ${row.outcomeId}`} aria-invalid={!!invalid} value={weights[index]} disabled={busy} onChange={event => { const value = event.target.value; const next = weights.map((weight, i) => i === index ? value : weight); setWeights(next); staged.set(draftKey, { base, weights: next }); }} /></td><td>{invalid ? '—' : `${(Number(weights[index]) / sum * 100).toFixed(4)}%`}<small>{invalid ? 'Fix weights to calculate' : `${Number(weights[index])} / ${sum}`}</small></td></tr>)}</tbody><tfoot><tr><th colSpan={2}>Total weight</th><td>{invalid ? '—' : sum.toLocaleString()}</td><td>{invalid ? '—' : '100%'}</td></tr></tfoot></table></div>
    {changed && !invalid && <p className="hint">Unapplied changes: {source.rows.flatMap((row, index) => String(row.weight) !== weights[index] ? [`${row.outcomeId}: ${row.weight} → ${weights[index]}`] : []).join(', ')}. They are retained when you switch workspaces.</p>}
    {invalid && <p className="inline-validation" role="alert">{invalid}</p>}{error && <p className="inline-validation" role="alert">{error}</p>}
    <div className="editor-actions"><button className="btn primary" disabled={!changed || !!invalid || busy} onClick={() => void commit()}>{busy ? 'Validating…' : 'Validate & apply weights'}</button><button className="btn" disabled={busy} onClick={() => { setWeights(source.rows.map(row => String(row.weight))); setBase(graph); setError(''); staged.delete(draftKey); }}>Reset weights</button>{undo && <button className="btn" disabled={busy} onClick={() => {
      if (canonicalJson(peekRootProject()) !== undo.committed) { setError('The model changed after applying. Undo would overwrite newer edits.'); return; }
      rootProject(); loadProject(undo.previous); setBase(peekRootProject() as ModelGraph); staged.delete(draftKey); setWeights(undo.previous ? weightedSources(undo.previous).find(item => item.key === source.key)!.rows.map(row => String(row.weight)) : []); setUndo(null);
    }}>Undo weight change</button>}
    <Link className="btn" to="/build" onClick={() => { rootProject(); if (source.scope) openMechanic(source.scope); }}>Open source graph</Link></div>
    {undo && <p role="status">Weights applied to the canonical model. Export and simulation use these values.</p>}
  </section>;
}

export default function WeightedDrawEditor() {
  const graph = useModelProjection();
  const [sourceKey, setSourceKey] = useState('');
  const sources = graph ? weightedSources(graph) : [];
  const source = sources.find(item => item.key === sourceKey) ?? sources[0];
  if (!graph || !source) return <div className="model-notice"><h2>No supported weighted table</h2><p>This model has no finite inline draw weights. Dynamic expressions and other structures remain editable in the logic graph.</p><Link className="btn" to="/build">Open logic graph</Link></div>;
  return <><label className="draw-source-select">Weighted draw<select className="inp" aria-label="Weighted draw source" value={source.key} onChange={event => setSourceKey(event.target.value)}>{sources.map(item => <option key={item.key} value={item.key}>{item.label}</option>)}</select></label><DrawTransaction key={source.key} graph={graph} source={source} /></>;
}
