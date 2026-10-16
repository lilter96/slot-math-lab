import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAppStore } from '../../store';
import { useModelProjection } from '../../hooks/useModelProjection';
import { canonicalJson } from '../../lib/hash';
import { changeReels, reelSources, validateReels, type ReelSource } from '../../lib/reelCommands';
import { importReelsCsv, reelsCsv } from '../../lib/reelCsv';
import { applyValidatedModel } from '../../lib/validatedModelEdit';
import { loadProject, peekRootProject, rootProject } from '../../lib/projectFiles';
import type { ModelGraph } from '../../lib/modelProjection';
import './ReelStripEditor.css';

interface Draft { base: ModelGraph; strips: string[][]; undo?: { previous: ModelGraph; committed: string }; preview?: boolean }
const drafts = new Map<string, Draft>();
const pageSize = 25;
function ReelTransaction({ graph, source, projectId }: { graph: ModelGraph; source: ReelSource; projectId: string | null }) {
  const key = `${projectId}:${source.key}`;
  const [draft, setDraft] = useState<Draft>(() => drafts.get(key) ?? { base: graph, strips: source.strips });
  const [page, setPage] = useState(0), [error, setError] = useState(''), [busy, setBusy] = useState(false);
  const [active, setActive] = useState({ reel: 0, index: 0 });
  const pending = useRef<AbortController | null>(null), csvTask = useRef(0), file = useRef<HTMLInputElement>(null), focusFrame = useRef(0);
  const panel = useRef<HTMLElement>(null);
  useEffect(() => () => { pending.current?.abort(); csvTask.current++; cancelAnimationFrame(focusFrame.current); }, []);
  const symbols = graph.symbols as { id: string; name: string }[];
  const invalid = validateReels(graph, draft.strips);
  const changed = canonicalJson(draft.strips) !== canonicalJson(source.strips);
  const length = Math.max(1, ...draft.strips.map(strip => strip.length)), pages = Math.ceil(length / pageSize);
  const activePage = Math.min(page, pages - 1), first = activePage * pageSize;
  const activeStrip = draft.strips[active.reel], activeIndex = Math.min(active.index, Math.max(0, activeStrip.length - 1));
  const focusCell = (reel: number, index: number) => {
    const trigger = document.activeElement;
    setActive({ reel, index }); setPage(Math.floor(index / pageSize));
    cancelAnimationFrame(focusFrame.current);
    focusFrame.current = requestAnimationFrame(() => {
      if (useAppStore.getState().projectId === projectId && (document.activeElement === trigger || document.activeElement === document.body)) panel.current?.querySelector<HTMLSelectElement>(`[aria-label="Reel ${reel + 1} symbol at ${index}"]`)?.focus();
    });
  };
  const stage = (strips: string[][], preview = false) => {
    csvTask.current++;
    const next = { ...draft, strips, preview }; setDraft(next); drafts.set(key, next); setError('');
  };
  const edit = (reel: number, transform: (strip: string[]) => string[]) => stage(draft.strips.map((strip, index) => index === reel ? transform(strip) : strip));
  const move = (reel: number, index: number, direction: number) => edit(reel, strip => {
    const next = [...strip]; [next[index], next[index + direction]] = [next[index + direction], next[index]]; return next;
  });
  const commit = async () => {
    csvTask.current++;
    const controller = new AbortController(); pending.current = controller; setBusy(true); setError('');
    try {
      const next = changeReels(draft.base, source.key, draft.strips);
      const committed = await applyValidatedModel(draft.base, next, controller.signal);
      if (controller.signal.aborted) return;
      const updated = { base: committed, strips: draft.strips, undo: { previous: draft.base, committed: canonicalJson(committed) } };
      setDraft(updated); drafts.set(key, updated);
    } catch (reason) { if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : String(reason)); }
    finally { if (!controller.signal.aborted) setBusy(false); }
  };
  return <section ref={panel} className="reel-editor" aria-label="Physical reel strip editor">
    <p className="source-path"><strong>Source</strong> initialState: {source.stripKeys.join(', ')}. {source.stops.length} mapped stop draws; {source.rows - 1} repeated wrap symbols per strip.</p>
    {source.conditions?.map(condition => <p className="hint" key={condition.scope}>Conditioned source {condition.scope}: at least {condition.atLeast} {condition.symbolName} symbols (ID {condition.symbol}) in the window. Applying also rebuilds showing patterns, their exact integer weights and every conditional stop subset; the original rule is retained as typed AST constants for export/reload.</p>)}
    {invalid && <p className="inline-validation" role="alert">{invalid}</p>}{error && <p className="inline-validation" role="alert">{error}</p>}
    <div className="editor-actions">
      <button className="btn primary" disabled={busy || !changed || !!invalid} onClick={() => void commit()}>{busy ? 'Validating…' : 'Validate & apply strips'}</button>
      <button className="btn" disabled={busy} onClick={() => { if (changed && !window.confirm('Discard unapplied strip edits and reset to the current model?')) return; csvTask.current++; const updated = { base: graph, strips: source.strips }; setDraft(updated); drafts.delete(key); setError(''); }}>Reset strips</button>
      {draft.undo && <button className="btn" disabled={busy} onClick={() => {
        if (canonicalJson(peekRootProject()) !== draft.undo!.committed) { setError('Newer edits prevent Undo. Reset to the current model.'); return; }
        csvTask.current++;
        rootProject(); loadProject(draft.undo!.previous);
        const previous = peekRootProject() as ModelGraph, restored = reelSources(previous).find(item => item.key === source.key)!;
        const updated = { base: previous, strips: restored.strips }; setDraft(updated); drafts.delete(key); setError('');
        focusCell(active.reel, Math.min(activeIndex, restored.strips[active.reel].length - 1));
      }}>Undo strip change</button>}
      <Link className="btn" to="/build">Inspect source graph</Link>
    </div>
    <p className="hint">Edit the circular period, not its repeated tail. Applying updates wrap data and all mapped stop rules together. Frequencies below describe ordinary raw strips before conditioning; they are not conditioned stop probabilities, RTP or final-board wins.</p>
    <div className="editor-actions">
      <button className="btn" disabled={busy || !!invalid} onClick={() => {
        const url = URL.createObjectURL(new Blob([reelsCsv(draft.strips)], { type: 'text/csv' })), link = document.createElement('a');
        link.href = url; link.download = 'reel-strips.csv'; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
      }}>Download strips CSV</button>
      <button className="btn" disabled={busy} onClick={() => file.current?.click()}>Preview strips CSV</button>
      <input ref={file} type="file" accept=".csv,text/csv" aria-label="Strip CSV file" hidden onChange={async event => {
        const selected = event.target.files?.[0]; event.target.value = ''; if (!selected) return;
        const token = ++csvTask.current;
        try { if (selected.size > 1048576) throw new Error('Strip CSV is limited to 1 MiB.'); const text = await selected.text(); if (token === csvTask.current) stage(importReelsCsv(text, graph, source.strips.length), true); }
        catch (reason) { if (token === csvTask.current) setError(reason instanceof Error ? reason.message : String(reason)); }
      }} />
    </div>
    {draft.preview && <p role="status">CSV preview staged. Review the columns and changes, then Validate & apply strips to confirm. The canonical model is unchanged.</p>}
    <div className="reel-pagination">
      <label>Strip index range<select className="inp" aria-label="Strip index range" value={activePage} onChange={event => {
        const page = Number(event.target.value), index = page * pageSize;
        const reel = activeStrip.length > index ? active.reel : Math.max(0, draft.strips.findIndex(strip => strip.length > index));
        setPage(page); setActive({ reel, index });
      }}>{Array.from({ length: pages }, (_, index) => <option key={index} value={index}>{index * pageSize}–{Math.min(length - 1, (index + 1) * pageSize - 1)}</option>)}</select></label>
      <span className="hint">25 indices per page; all stops remain in the model.</span>
    </div>
    <div className="reel-selection-tools" aria-label="Selected strip stop">
      <p className="hint">Reel {active.reel + 1}, index {activeIndex} · raw ID <code>{activeStrip[activeIndex] ?? '—'}</code> · {activeStrip.length ? `ordinary raw stop share 1/${activeStrip.length}; raw cumulative ${((activeIndex + 1) / activeStrip.length * 100).toFixed(2)}%` : 'Empty strip'}{source.strips[active.reel][activeIndex] !== activeStrip[activeIndex] && ` · was ${source.strips[active.reel][activeIndex] ?? 'absent'}`}</p>
      <div className="reel-cell-actions">
        <button className="btn" aria-label={`Move reel ${active.reel + 1} index ${activeIndex} up`} disabled={busy || activeIndex === 0} onClick={() => { move(active.reel, activeIndex, -1); focusCell(active.reel, activeIndex - 1); }}>Move up</button>
        <button className="btn" aria-label={`Move reel ${active.reel + 1} index ${activeIndex} down`} disabled={busy || activeIndex >= activeStrip.length - 1} onClick={() => { move(active.reel, activeIndex, 1); focusCell(active.reel, activeIndex + 1); }}>Move down</button>
        <button className="btn" aria-label={`Remove reel ${active.reel + 1} index ${activeIndex}`} disabled={busy || !activeStrip.length} onClick={() => { edit(active.reel, values => values.filter((_, i) => i !== activeIndex)); focusCell(active.reel, Math.max(0, Math.min(activeIndex, activeStrip.length - 2))); }}>Remove stop</button>
        <button className="btn" aria-label={`Copy reel ${active.reel + 1} index ${activeIndex} symbol ID`} disabled={busy || !activeStrip.length} onClick={() => {
          if (!navigator.clipboard) { setError('Clipboard unavailable. Select and copy the raw symbol ID above.'); return; }
          void navigator.clipboard.writeText(activeStrip[activeIndex]).catch(() => setError('Clipboard unavailable. Select and copy the raw symbol ID above.'));
        }}>Copy ID</button>
      </div>
    </div>
    <div className="math-table-scroll reel-table-scroll"><table className="math-data-table reel-data-table">
      <thead><tr><th>Index</th>{draft.strips.map((strip, reel) => <th key={reel}>Reel {reel + 1}<small>{strip.length} stops · 1/{strip.length || '—'} each</small><small>Source {source.stripKeys[reel]}</small></th>)}</tr></thead>
      <tbody>{Array.from({ length: Math.min(pageSize, length - first) }, (_, offset) => first + offset).map(index => <tr key={index}><th scope="row">{index}</th>{draft.strips.map((strip, reel) => <td key={reel}>{index < strip.length ? <>
        <select className="inp" data-changed={source.strips[reel][index] !== strip[index]} aria-label={`Reel ${reel + 1} symbol at ${index}`} disabled={busy} value={strip[index]} onFocus={() => setActive({ reel, index })} onChange={event => edit(reel, values => values.map((value, i) => i === index ? event.target.value : value))}>{symbols.map(symbol => <option key={symbol.id} value={symbol.id}>{symbol.name} · ID {symbol.id}</option>)}</select>
      </> : <span className="hint">End of strip</span>}</td>)}</tr>)}</tbody>
      <tfoot><tr><th>Add</th>{draft.strips.map((strip, reel) => <td key={reel}><button className="btn" aria-label={`Add symbol to reel ${reel + 1}`} disabled={busy || strip.length >= 4096} onClick={() => { edit(reel, values => [...values, String(symbols[0].id)]); focusCell(reel, strip.length); }}>+ Symbol</button></td>)}</tr></tfoot>
    </table></div>
    <details><summary>Symbol counts and raw-strip frequency</summary><div className="math-table-scroll"><table className="math-data-table"><thead><tr><th>Symbol</th>{draft.strips.map((_, reel) => <th key={reel}>Reel {reel + 1}</th>)}</tr></thead><tbody>{symbols.map(symbol => <tr key={symbol.id}><th scope="row">{symbol.name}<small>ID {symbol.id}</small></th>{draft.strips.map((strip, reel) => { const count = strip.filter(id => id === String(symbol.id)).length; return <td key={reel}>{count} / {strip.length}<small>{strip.length ? `${(count / strip.length * 100).toFixed(4)}%` : '—'}</small></td>; })}</tr>)}</tbody></table></div></details>
    {changed && <p role="status">Unapplied strip changes: {source.strips.map((strip, reel) => {
      const values = draft.strips[reel], differences = Array.from({ length: Math.max(strip.length, values.length) }, (_, i) => i).filter(i => strip[i] !== values[i]).length;
      return `Reel ${reel + 1}: ${strip.length} → ${values.length} stops, ${differences} changed indices`;
    }).join('; ')}. Values/order, wrap tails and mapped stop domains are included. Draft retained in this session when switching modes/projects.</p>}
    {draft.undo && <p role="status">Strip period, wrap data and stop draws applied to the canonical model. Export and analysis use these inputs.</p>}
  </section>;
}

export default function ReelStripEditor() {
  const graph = useModelProjection(), projectId = useAppStore(state => state.projectId);
  const [selected, setSelected] = useState('');
  const sources = graph ? reelSources(graph) : [], source = sources.find(item => item.key === selected) ?? sources[0];
  if (!graph || !source) return <div className="model-notice"><h2>No proven reel-strip mapping</h2><p>The model may use independent cell draws or customized/conditioned stop domains. A safe strip edit must preserve all linked source rules. Inspect the graph and raw state; this editor has made no source changes.</p><Link className="btn" to="/build">Open logic graph</Link></div>;
  return <><label className="draw-source-select">Reel set<select className="inp" aria-label="Reel set" value={source.key} onChange={event => setSelected(event.target.value)}>{sources.map(source => <option key={source.key} value={source.key}>{source.label} · {source.strips.length} reels</option>)}</select></label><ReelTransaction key={`${projectId}:${source.key}`} graph={graph} source={source} projectId={projectId} /></>;
}
