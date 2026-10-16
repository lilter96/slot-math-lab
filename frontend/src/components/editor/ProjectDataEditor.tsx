import { projectContentScope } from '../../lib/projectFiles';
import ModelNumberInput from './ModelNumberInput';
import { EvidenceInputsEditor } from './EvidenceInputsEditor';
import { useEffect, useRef, useState } from 'react';
import { useAppStore } from '../../store';
import type { ExpressionAst } from '../../lib/expressionParser';
import ExpressionTreeEditor from './ExpressionTreeEditor';
import GameDialog from './GraphDialog';
import { graphRequest, type GraphRound } from '../../lib/graphApi';
import { DEFAULT_FEATURES, useFeaturesQuery } from '../../api/hooks';
import { peekRootProject } from '../../lib/projectFiles';

const executionScope = projectContentScope;
export default function ProjectDataEditor({ onClose, initialTable }: { onClose: () => void; initialTable?: string }) {
  const play = (useFeaturesQuery().data ?? DEFAULT_FEATURES).play;
  const tables = useAppStore(s => s.tables);
  const state = tables.initialState as Record<string, unknown> ?? {};
  const expressions = tables.expressions as Record<string, ExpressionAst> ?? {};
  const [kind, setKind] = useState('data');
  const [key, setKey] = useState(initialTable ?? Object.keys(state)[0] ?? '');
  const [expressionId, setExpressionId] = useState(Object.keys(expressions)[0] ?? '');
  const [newKey, setNewKey] = useState('');
  const [executed, setExecuted] = useState<GraphRound | null>(null), [running, setRunning] = useState(false), [error, setError] = useState('');
  const execution = useRef<{ controller: AbortController; scope: string } | null>(null);
  useEffect(() => {
    let scope = executionScope();
    const unsubscribe = useAppStore.subscribe((current, previous) => {
      if (current.projectId === previous.projectId && current.graphTrail === previous.graphTrail && current.nodes === previous.nodes && current.edges === previous.edges && current.tables === previous.tables) return;
      const next = executionScope();
      if (next === scope) return;
      scope = next;
      execution.current?.controller.abort(); execution.current = null;
      setExecuted(null); setError(''); setRunning(false);
    });
    return () => { unsubscribe(); execution.current?.controller.abort(); execution.current = null; };
  }, []);
  const update = (value: unknown) => useAppStore.setState({ tables: { ...tables, initialState: { ...state, [key]: value } } });
  const inputScope = useAppStore(s => JSON.stringify([s.projectId, s.graphTrail.map(entry => entry.mechanic)]));
  const value = state[key];
  const readOnlyState = value === null || (value != null && typeof value === 'object' && !Array.isArray(value)) || (Array.isArray(value) && value.some(entry => typeof entry !== 'string' && typeof entry !== 'number'));
  return <GameDialog title="Project data & expressions" onClose={onClose} wide>
    <div className="data-editor-tabs"><button className="btn" onClick={() => setKind('data')}>State & tables</button><button className="btn" onClick={() => setKind('expressions')}>Expressions</button><button className="btn" onClick={() => setKind('evidence')}>Rule / spec / asset inputs</button>{play && <button className="btn" onClick={() => setKind('execute')}>Execute & inspect</button>}</div>
    {kind === 'data' ? <>
      <label>State field / data table<select className="inp" aria-label="Data table" value={key} onChange={e => setKey(e.target.value)}>{Object.keys(state).map(k => <option key={k}>{k}</option>)}</select></label>
      <p className="hint">These are the actual inputs serialized with the graph, including reel strips, paytable values and initial mechanic state.</p>
      {readOnlyState ? <><p className="hint">Structured state, null values and arrays with boolean or nested values are read only here to preserve their types. Use the source graph or a supported data adapter to edit them.</p><pre className="code-block">{JSON.stringify(value, null, 2)}</pre></> : Array.isArray(value) ? <div className="data-grid" style={{ alignItems: 'start' }}>{value.map((entry, i) => <label key={i}>{i}{typeof entry === 'number' ? <ModelNumberInput key={`${inputScope}:${key}:${i}`} label={`Data row ${i + 1}`} value={entry} onChange={next => update(value.map((x, j) => j === i ? next : x))} /> : <input className="inp" aria-label={`Data row ${i + 1}`} value={entry} onChange={e => update(value.map((x, j) => j === i ? e.target.value : x))} />}<button className="btn sm" aria-label={`Delete data row ${i + 1}`} onClick={() => { if (window.confirm(`Delete row ${i + 1} from ${key}? Following rows will shift index.`)) update(value.filter((_, j) => i !== j)); }}>×</button></label>)}
        <button className="btn" onClick={() => update([...value, value.length && value.every(entry => typeof entry === 'number') ? 0 : '0'])}>Add data row</button></div>
        : typeof value === 'boolean' ? <label><input aria-label="State field value" type="checkbox" checked={value} onChange={e => update(e.target.checked)} />Enabled</label>
        : <label>Value{typeof value === 'number' ? <ModelNumberInput key={`${inputScope}:${key}`} label="State field value" value={value} onChange={update} /> : <input className="inp" aria-label="State field value" value={String(value ?? '')} onChange={e => update(e.target.value)} />}</label>}
      <div className="data-editor-tabs"><input className="inp" aria-label="New data field" value={newKey} onChange={e => setNewKey(e.target.value)} placeholder="New state field" /><button className="btn" disabled={!newKey.trim() || Object.hasOwn(state, newKey)} onClick={() => {
        useAppStore.setState({ tables: { ...tables, initialState: { ...state, [newKey]: [] } } }); setKey(newKey); setNewKey('');
      }}>Add array field</button></div>
      {Object.hasOwn(state, newKey) && <p role="alert">This state field already exists. Choose a different name to keep its value.</p>}
    </> : kind === 'expressions' ? <>
      <label>Expression<select className="inp" aria-label="Expression definition" value={expressionId} onChange={e => setExpressionId(e.target.value)}>{Object.keys(expressions).map(id => <option key={id}>{id}</option>)}</select></label>
      {expressions[expressionId] && <ExpressionTreeEditor value={expressions[expressionId]} onChange={v => useAppStore.setState({ tables: { ...tables, expressions: { ...expressions, [expressionId]: v } } })} />}
      <div className="data-editor-tabs"><input className="inp" aria-label="New expression ID" value={newKey} onChange={e => setNewKey(e.target.value)} /><button className="btn" disabled={!newKey.trim() || Object.hasOwn(expressions, newKey)} onClick={() => {
        useAppStore.setState({ tables: { ...tables, expressions: { ...expressions, [newKey]: { exprType: 'constant', kind: 'Integer', value: '0' } } } }); setExpressionId(newKey); setNewKey('');
      }}>Add expression</button></div>
      {Object.hasOwn(expressions, newKey) && <p role="alert">This expression ID already exists. Choose a different ID to keep its definition.</p>}
    </> : kind === 'evidence' ? <EvidenceInputsEditor /> : <>
      <p>Execute the current complete graph with seed 42. The final state below comes from the shared compiler and interpreter.</p>
      <button className="btn primary" disabled={running || !play} onClick={async () => {
        execution.current?.controller.abort();
        const task = { controller: new AbortController(), scope: executionScope() };
        execution.current = task; setRunning(true); setError(''); setExecuted(null);
        const current = () => execution.current === task && !task.controller.signal.aborted && task.scope === executionScope();
        try {
          const config = peekRootProject(); if (!config) throw new Error('No graph');
          const result = await graphRequest<GraphRound>('play/round', { config, seed: 42, trace: true }, task.controller.signal);
          if (current()) setExecuted(result);
        } catch (e) { if (current()) setError(e instanceof Error ? e.message : 'Execution failed'); }
        finally { if (current()) { setRunning(false); execution.current = null; } }
      }}>{running ? 'Executing…' : 'Execute graph'}</button>
      {error && <p role="alert">{error}</p>}{executed && <><p>Graph {executed.configHash}</p><table className="dh-math-table" data-testid="executed-state"><thead><tr><th>State field</th><th>Exact value</th></tr></thead><tbody>{Object.entries(executed.state).map(([key, value]) => <tr key={key}><th>{key}</th><td><code style={{ overflowWrap: 'anywhere' }}>{typeof value === 'object' ? JSON.stringify(value) : String(value)}</code></td></tr>)}</tbody></table></>}
    </>}
  </GameDialog>;
}
