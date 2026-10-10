import { useAppStore, type GraphNode } from '../../store';
import { openMechanic } from '../../lib/projectFiles';
import type { ExpressionAst } from '../../lib/expressionParser';
import ExpressionTreeEditor from './ExpressionTreeEditor';
export default function NativeInspector({ node }: { node: GraphNode }) {
  const tables = useAppStore(s => s.tables); const setNodeData = useAppStore(s => s.setNodeData);
  const native = node.data.backendNode as Record<string, unknown>;
  const update = (key: string, value: unknown) => setNodeData(node.id, { backendNode: { ...native, [key]: value },
    ...(key === 'label' ? { label: String(value) } : {}), ...(key === 'winCap' ? { winCap: Number(value) } : {}),
    ...(key === 'drawWeights' ? { drawWeights: value as GraphNode['data']['drawWeights'] } : {}) });
  const field = (key: string, label: string, numeric = false) => <div className="field" key={key}><label htmlFor={`native-${key}`}>{label}</label><input id={`native-${key}`} className="inp" type={numeric ? 'number' : 'text'} value={String(native[key] ?? '')} onChange={e => update(key, numeric ? Number(e.target.value) : e.target.value)} /></div>;
  const refKey = ['expressionId', 'conditionId', 'stopConditionId', 'weightExpressionId'].find(key => native[key]);
  const expressions = tables.expressions as Record<string, ExpressionAst> ?? {};
  return <><div className="panel-h"><b>{node.data.label}</b><span>{String(native.nodeType)}</span></div><div className="panel-body">
    {field('label', 'Label')}
    {native.nodeType === 'modifyState' && <>{field('outputKey', 'Output state key')}{field('expressionId', 'Expression ID')}</>}
    {native.nodeType === 'metricsSink' && <>{field('winCap', 'Round win cap', true)}{field('winStateKey', 'Payout state key')}<MonetaryPolicy value={native.settlement as MonetaryPolicyValue | undefined} change={value => update('settlement', value)} /></>}
    {native.nodeType === 'loop' && <>{field('maxIterations', 'Iteration cap', true)}{field('stopConditionId', 'Stop expression ID')}<div className="section-label">Exit reason at conditional stop</div><p className="hint">The iteration limit always records modelLimit. Classify conditional exits explicitly as condition, payoutCap, authoredStop or resourceExpiry.</p><ExpressionTreeEditor value={native.exitReason as ExpressionAst ?? { exprType: 'constant', kind: 'String', value: 'condition' }} onChange={value => update('exitReason', value)} /></>}
    {native.nodeType === 'branch' && field('conditionId', 'Condition expression ID')}
    {native.nodeType === 'library' && <>{field('mechanicName', 'Subgraph name')}
      {Object.entries(native.parameters as Record<string, string> ?? {}).map(([key, value]) => <div className="field" key={key}><label htmlFor={`param-${key}`}>Parameter {key}</label><input id={`param-${key}`} className="inp" value={value} onChange={e => update('parameters', { ...native.parameters as object, [key]: e.target.value })} /></div>)}
      <button className="btn primary" onClick={() => openMechanic(String(native.mechanicName))}>Open subgraph</button></>}
    {native.nodeType === 'draw' && <>{field('stateWriteKey', 'Outcome state key')}
      <table className="native-weights"><thead><tr><th>Outcome</th><th>Weight</th></tr></thead><tbody>
        {(node.data.drawWeights ?? []).map((weight, i, weights) => <tr key={i}><td>{weight.outcomeId}</td><td><input className="inp" aria-label={`Outcome ${weight.outcomeId} weight`} type="number" min="0" value={weight.weight} onChange={e => update('drawWeights', weights.map((w, j) => j === i ? { ...w, weight: Number(e.target.value) } : w))} /></td></tr>)}
      </tbody></table></>}
    {refKey && expressions[String(native[refKey])] && <><div className="section-label">Typed expression · editable AST</div>
      <ExpressionTreeEditor value={expressions[String(native[refKey])]} onChange={value => useAppStore.setState({ tables: { ...tables, expressions: { ...expressions, [String(native[refKey])]: value } } })} /></>}
    <div className="divider" /><button className="btn" onClick={() => useAppStore.getState().removeNode(node.id)}>Delete node</button>
  </div></>;
}

interface MonetaryPolicyValue { quantum: string; mode: string }
function MonetaryPolicy({ value, change }: { value?: MonetaryPolicyValue; change(value: MonetaryPolicyValue | null): void }) {
  return <fieldset><legend>Monetary settlement</legend><label><input type="checkbox" checked={!!value} onChange={e => change(e.target.checked ? { quantum: '0.01', mode: 'nearestEven' } : null)} />Round total award before WinCap</label>{value && <><label>Quantum in payout units<input className="inp" value={value.quantum} onChange={e => change({ ...value, quantum: e.target.value })} /></label><label>Rounding rule<select className="inp" value={value.mode} onChange={e => change({ ...value, mode: e.target.value })}>{['floor', 'ceiling', 'nearestEven', 'nearestAway'].map(mode => <option key={mode}>{mode}</option>)}</select></label><p className="hint">Exact pre/post-rounding values and signed difference appear in __settlementBefore, __settlementAfter and __roundingDifference at complete-round measurement boundaries. The cap applies afterward. Quantum has at most nine decimal places.</p></>}</fieldset>;
}
