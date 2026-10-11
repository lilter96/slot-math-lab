import { useAppStore } from '../../store';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useModelProjection } from '../../hooks/useModelProjection';
import { clusterPayoutCapability, changeClusterPayouts } from '../../lib/payoutCommands';
import { applyValidatedModel } from '../../lib/validatedModelEdit';
import type { ModelGraph } from '../../lib/modelProjection';
import { canonicalJson } from '../../lib/hash';
import { peekRootProject, rootProject, loadProject } from '../../lib/projectFiles';
const staged = new Map<string, { base: ModelGraph; values: string[] }>();
function PayoutTransaction({ graph, symbolId }: { graph: ModelGraph; symbolId: string }) {
  const key = `${useAppStore(s => s.projectId)}:${graph.id}:${symbolId}`;
  const coins = (graph.initialState as { clusterPaytable: string[] }).clusterPaytable.slice((Number(symbolId) - 1) * 26, Number(symbolId) * 26);
  const [base, setBase] = useState(() => staged.get(key)?.base ?? graph);
  const [values, setValues] = useState(() => staged.get(key)?.values ?? coins);
  const [busy, setBusy] = useState(false), [error, setError] = useState('');
  const [undo, setUndo] = useState<{ previous: ModelGraph; committed: string } | null>(null);
  const changed = values.some((value, index) => value !== coins[index]);
  const valid = values.every(value => /^\d+$/.test(value) && Number.isSafeInteger(Number(value)) && Number(value) <= 2147483647);
  return <section className="weighted-editor"><p className="source-path"><strong>Source</strong> initialState.clusterPaytable[{(Number(symbolId) - 1) * 26}…{Number(symbolId) * 26 - 1}] and matching paytable metadata.</p><p className="hint">Enter authored coin amounts for cluster sizes 5–30. The source divides round coins by 20 into model win units. This preview is a payout parameter, not measured RTP. Multipliers and the existing round cap still apply.</p>
    <div className="math-table-scroll"><table className="math-data-table"><thead><tr><th>Cluster size</th><th>Authored coins</th><th>Model win units before multipliers</th></tr></thead><tbody>{values.map((value, index) => <tr key={index}><th scope="row">{index + 5} connected cells</th><td><input className="inp" inputMode="numeric" aria-label={`Coins for ${index + 5} cells`} value={value} disabled={busy} onChange={event => { const next = values.map((item, i) => i === index ? event.target.value : item); setValues(next); staged.set(key, { base, values: next }); }} /></td><td>{valid ? `${Number(value)} / 20 = ${Number(value) / 20}` : '—'}</td></tr>)}</tbody></table></div>
    {changed && <p className="hint">Unapplied changes: {values.flatMap((value, index) => value !== coins[index] ? [`${index + 5} cells: ${coins[index]} → ${value} coins`] : []).join('; ')}.</p>}{!valid && <p role="alert" className="inline-validation">Use non-negative whole coin amounts up to 2,147,483,647.</p>}{error && <p role="alert" className="inline-validation">{error}</p>}
    <div className="editor-actions"><button className="btn primary" disabled={busy || !changed || !valid} onClick={async () => { setBusy(true); setError(''); try { const committed = await applyValidatedModel(base, changeClusterPayouts(base, symbolId, values)); setUndo({ previous: base, committed: canonicalJson(committed) }); setBase(committed); staged.delete(key); } catch (reason) { setError(reason instanceof Error ? reason.message : String(reason)); } finally { setBusy(false); } }}>{busy ? 'Validating…' : 'Validate & apply payouts'}</button><button className="btn" disabled={busy} onClick={() => { setBase(graph); setValues(coins); staged.delete(key); setError(''); }}>Reset payouts</button>{undo && <button className="btn" disabled={busy} onClick={() => { if (canonicalJson(peekRootProject()) !== undo.committed) { setError('Newer edits prevent Undo. Reset to the current model.'); return; } rootProject(); loadProject(undo.previous); const previous = peekRootProject() as ModelGraph; setBase(previous); setValues((previous.initialState as { clusterPaytable: string[] }).clusterPaytable.slice((Number(symbolId) - 1) * 26, Number(symbolId) * 26)); staged.delete(key); setUndo(null); }}>Undo payout change</button>}<Link className="btn" to="/build">Inspect source logic</Link></div>{undo && <p role="status">Executable payout input and metadata updated together.</p>}
  </section>;
}
export default function ClusterPayoutEditor() {
  const graph = useModelProjection();
  const projectId = useAppStore(s => s.projectId);
  const table = graph ? clusterPayoutCapability(graph) : null;
  const [symbolId, setSymbolId] = useState('1');
  const symbols = (graph?.symbols ?? []) as { id: string; name: string }[];
  if (!graph || !table) return <div className="model-notice"><h2>No supported cluster payout mapping</h2><p>The executable lookup, unit conversion and metadata must match the guarded source pattern. Payout rules remain editable in the logic graph.</p><Link className="btn" to="/build">Inspect logic graph</Link></div>;
  return <><label className="draw-source-select">Paying symbol<select className="inp" aria-label="Payout symbol" value={symbolId} onChange={event => setSymbolId(event.target.value)}>{table.entries.map(entry => <option key={entry.symbolId} value={entry.symbolId}>{symbols.find(symbol => symbol.id === entry.symbolId)?.name ?? entry.symbolId} (ID {entry.symbolId})</option>)}</select></label><PayoutTransaction key={`${projectId}:${symbolId}`} graph={graph} symbolId={symbolId} /></>;
}
