import { useState } from 'react';
import { useAppStore } from '../../store';
import type { ExpressionAst } from '../../lib/expressionParser';
import ExpressionTreeEditor from './ExpressionTreeEditor';
import GameDialog from '../../games/doghouse/GameDialog';
import { graphRequest, type GraphRound } from '../../games/doghouse/api';
import { exportProject } from '../../lib/projectFiles';
export default function ProjectDataEditor({ onClose, initialTable }: { onClose: () => void; initialTable?: string }) {
  const tables = useAppStore(s => s.tables);
  const state = tables.initialState as Record<string, unknown> ?? {};
  const expressions = tables.expressions as Record<string, ExpressionAst> ?? {};
  const [kind, setKind] = useState('data');
  const [key, setKey] = useState(initialTable ?? Object.keys(state)[0] ?? '');
  const [expressionId, setExpressionId] = useState(Object.keys(expressions)[0] ?? '');
  const [newKey, setNewKey] = useState('');
  const [executed, setExecuted] = useState<GraphRound | null>(null), [running, setRunning] = useState(false), [error, setError] = useState('');
  const update = (value: unknown) => useAppStore.setState({ tables: { ...tables, initialState: { ...state, [key]: value } } });
  const value = state[key];
  return <GameDialog title="Project data & expressions" onClose={onClose} wide>
    <div className="data-editor-tabs"><button className="btn" onClick={() => setKind('data')}>State & tables</button><button className="btn" onClick={() => setKind('expressions')}>Expressions</button><button className="btn" onClick={() => setKind('execute')}>Execute & inspect</button></div>
    {kind === 'data' ? <>
      <label>State field / data table<select className="inp" aria-label="Data table" value={key} onChange={e => setKey(e.target.value)}>{Object.keys(state).map(k => <option key={k}>{k}</option>)}</select></label>
      <p className="hint">These are the actual inputs serialized with the graph, including reel strips, paytable values and initial mechanic state.</p>
      {Array.isArray(value) ? <div className="data-grid">{value.map((entry, i) => <label key={i}>{i}<input className="inp" aria-label={`Data row ${i + 1}`} value={String(entry)} onChange={e => update(value.map((x, j) => j === i ? (typeof entry === 'number' ? Number(e.target.value) : e.target.value) : x))} /><button className="btn sm" aria-label={`Delete data row ${i + 1}`} onClick={() => update(value.filter((_, j) => i !== j))}>×</button></label>)}
        <button className="btn" onClick={() => update([...value, '0'])}>Add data row</button></div>
        : typeof value === 'boolean' ? <label><input aria-label="State field value" type="checkbox" checked={value} onChange={e => update(e.target.checked)} />Enabled</label>
        : <label>Value<input className="inp" aria-label="State field value" type={typeof value === 'number' ? 'number' : 'text'} value={String(value ?? '')} onChange={e => update(typeof value === 'number' ? Number(e.target.value) : e.target.value)} /></label>}
      <div className="data-editor-tabs"><input className="inp" aria-label="New data field" value={newKey} onChange={e => setNewKey(e.target.value)} placeholder="New state field" /><button className="btn" disabled={!newKey.trim()} onClick={() => {
        useAppStore.setState({ tables: { ...tables, initialState: { ...state, [newKey]: [] } } }); setKey(newKey); setNewKey('');
      }}>Add array field</button></div>
    </> : kind === 'expressions' ? <>
      <label>Expression<select className="inp" aria-label="Expression definition" value={expressionId} onChange={e => setExpressionId(e.target.value)}>{Object.keys(expressions).map(id => <option key={id}>{id}</option>)}</select></label>
      {expressions[expressionId] && <ExpressionTreeEditor value={expressions[expressionId]} onChange={v => useAppStore.setState({ tables: { ...tables, expressions: { ...expressions, [expressionId]: v } } })} />}
      <div className="data-editor-tabs"><input className="inp" aria-label="New expression ID" value={newKey} onChange={e => setNewKey(e.target.value)} /><button className="btn" disabled={!newKey.trim()} onClick={() => {
        useAppStore.setState({ tables: { ...tables, expressions: { ...expressions, [newKey]: { exprType: 'constant', kind: 'Integer', value: '0' } } } }); setExpressionId(newKey); setNewKey('');
      }}>Add expression</button></div>
    </> : <>
      <p>Execute the current complete graph with seed 42. The final state below comes from the shared compiler and interpreter.</p>
      <button className="btn primary" disabled={running} onClick={async () => { setRunning(true); setError(''); try { const config = exportProject(); if (!config) throw new Error('No graph'); setExecuted(await graphRequest<GraphRound>('play/round', { config, seed: 42, trace: true })); } catch(e) { setError(e instanceof Error ? e.message : 'Execution failed'); } finally { setRunning(false); } }}>{running ? 'Executing…' : 'Execute graph'}</button>
      {error && <p role="alert">{error}</p>}{executed && <><p>Graph {executed.configHash}</p><table className="dh-math-table" data-testid="executed-state"><thead><tr><th>State field</th><th>Exact value</th></tr></thead><tbody>{Object.entries(executed.state).map(([key, value]) => <tr key={key}><th>{key}</th><td><code style={{ overflowWrap: 'anywhere' }}>{typeof value === 'object' ? JSON.stringify(value) : String(value)}</code></td></tr>)}</tbody></table></>}
    </>}
  </GameDialog>;
}
