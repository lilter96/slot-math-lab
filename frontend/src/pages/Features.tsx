import { useAppStore } from '../store';
import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useModelProjection } from '../hooks/useModelProjection';
import { mechanicLabel, referencedMechanics, type ModelGraph } from '../lib/modelProjection';
import { freeSpinCapability, changeFreeSpinCount } from '../lib/featureCommands';
import { applyValidatedModel } from '../lib/validatedModelEdit';
import { canonicalJson } from '../lib/hash';
import { rootProject, openMechanic, peekRootProject, loadProject } from '../lib/projectFiles';
const staged = new Map<string, { base: ModelGraph; value: string }>();
function FreeSpinForm({ graph, count }: { graph: ModelGraph; count: number }) {
  const draftKey = `${useAppStore(s => s.projectId)}:${graph.id ?? graph.name}`;
  const [value, setValue] = useState(() => staged.get(draftKey)?.value ?? String(count));
  const [base, setBase] = useState(() => staged.get(draftKey)?.base ?? graph);
  const [busy, setBusy] = useState(false), [error, setError] = useState('');
  const pending = useRef<AbortController | null>(null);
  useEffect(() => () => { pending.current?.abort(); pending.current = null; }, []);
  const [undo, setUndo] = useState<{ previous: ModelGraph; committed: string } | null>(null);
  return <section className="feature-form"><h2>Awarded free spins</h2><p>Change the awarded count and its matching loop ceiling together. Trigger conditions, round cap and bonus-buy price retain their current source values.</p>
    <label>Free-spin count<input className="inp" aria-label="Awarded free spins" inputMode="numeric" value={value} disabled={busy} onChange={event => { setValue(event.target.value); staged.set(draftKey, { base, value: event.target.value }); }} /></label>
    <p className="hint">Source mapping: initialState.freeSpins; bonus.expressions.lives; bonus.nodes[free-spins].maxIterations; base-spin.expressions[initial-lives].thenExpr.</p>
    <div className="editor-actions"><button className="btn primary" disabled={busy || value === String(count)} onClick={async () => {
      pending.current?.abort(); const controller = new AbortController(); pending.current = controller; setBusy(true); setError(''); try { const next = changeFreeSpinCount(base, value); const committed = await applyValidatedModel(base, next, controller.signal); if (controller.signal.aborted || pending.current !== controller) return; setUndo({ previous: base, committed: canonicalJson(committed) }); setBase(committed); staged.delete(draftKey); } catch (reason) { if (!controller.signal.aborted && pending.current === controller && !(reason instanceof DOMException && reason.name === 'AbortError')) setError(reason instanceof Error ? reason.message : String(reason)); } finally { if (pending.current === controller) { pending.current = null; setBusy(false); } }
    }}>{busy ? 'Validating…' : 'Validate & apply free spins'}</button><button className="btn" disabled={busy} onClick={() => { setBase(graph); setValue(String(count)); setError(''); staged.delete(draftKey); }}>Reset free spins</button>{undo && <button className="btn" disabled={busy} onClick={() => { if (canonicalJson(peekRootProject()) !== undo.committed) { setError('Newer edits prevent this Undo. Reset to the current model.'); return; } rootProject(); loadProject(undo.previous); setBase(peekRootProject() as ModelGraph); setValue(String(freeSpinCapability(undo.previous))); setUndo(null); staged.delete(draftKey); }}>Undo free-spin change</button>}</div>
    {error && <p className="inline-validation" role="alert">{error}</p>}{undo && <p role="status">Free-spin sources updated together. Run analysis to measure the edited model.</p>}
  </section>;
}
export default function Features() {
  const graph = useModelProjection();
  const projectId = useAppStore(s => s.projectId);
  const [selected, setSelected] = useState('');
  const definitions = graph?.mechanics ?? {};
  const definitionIds = [...new Set(['bonus', 'base-spin', 'cascade-step', 'round-entry', ...Object.keys(definitions)])].filter(key => key in definitions);
  const id = selected in definitions ? selected : definitionIds[0];
  const definition = definitions[id];
  const count = graph ? freeSpinCapability(graph) : null;
  return <section className="features-page"><header className="page-heading"><div><h1>Features & rules</h1><p>Read source definitions and edit only supported parameter mappings.</p></div><Link className="btn" to="/build">Explore logic graph</Link></header>
    <label className="draw-source-select">Mechanic definition<select className="inp" aria-label="Feature definition" value={id ?? ''} onChange={event => setSelected(event.target.value)}>{definitionIds.map(key => <option key={key} value={key}>{mechanicLabel(key)} ({key})</option>)}</select></label>
    {definition ? <section className="feature-summary"><h2>{mechanicLabel(id)}</h2><p>{definition.description ?? 'No description in this definition.'}</p><p>{definition.nodes.length} authored nodes. Uses {referencedMechanics(definition.nodes).map(mechanicLabel).join(', ') || 'atomic nodes only'}.</p><Link className="btn" to="/build" onClick={() => { rootProject(); openMechanic(id); }}>Open underlying graph</Link></section> : <p>No named feature definitions. Open the graph to author a mechanic.</p>}
    {graph && count != null ? <FreeSpinForm key={`${projectId}:${graph.id ?? graph.name}`} graph={graph} count={count} /> : <div className="model-notice"><h2>No supported feature form</h2><p>This graph does not match the guarded free-spin source mapping. Its mechanics remain editable in the graph; no inferred rules will overwrite its expressions.</p></div>}
  </section>;
}
