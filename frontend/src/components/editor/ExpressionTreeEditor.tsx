import type { ExpressionAst as Expr } from '../../lib/expressionParser';
const types = ['constant', 'fieldAccess', 'binary', 'compare', 'if', 'not', 'call', 'aggregate', 'map', 'fold', 'filter'];
const zero = (): Expr => ({ exprType: 'constant', kind: 'Integer', value: '0' });
function fresh(type: string): Expr {
  switch (type) {
    case 'fieldAccess': return { exprType: type, target: 'state', path: ['totalCoins'] };
    case 'binary': return { exprType: type, op: 'Add', left: zero(), right: zero() };
    case 'compare': return { exprType: type, op: 'Eq', left: zero(), right: zero() };
    case 'if': return { exprType: type, condition: { exprType: 'constant', kind: 'Boolean', value: 'true' }, thenExpr: zero(), elseExpr: zero() };
    case 'not': return { exprType: type, expr: { exprType: 'constant', kind: 'Boolean', value: 'true' } };
    case 'call': return { exprType: type, function: 'max', args: [zero(), zero()] };
    case 'aggregate': return { exprType: type, func: 'Sum', stateKey: 'board', itemName: 'item', itemType: 'String' };
    case 'map': return { exprType: type, stateKey: 'board', itemName: 'item', indexName: 'i', itemType: 'String', body: { exprType: 'fieldAccess', target: 'state', path: ['item'] } };
    case 'fold': return { exprType: type, stateKey: 'board', itemName: 'item', accName: 'acc', indexName: 'i', itemType: 'String', init: zero(), body: zero() };
    case 'filter': return { exprType: type, stateKey: 'board', itemName: 'item', indexName: 'i', itemType: 'String', predicate: { exprType: 'constant', kind: 'Boolean', value: 'true' } };
    default: return zero();
  }
}
export default function ExpressionTreeEditor({ value, onChange, label = 'Expression', depth = 0, allowSettlement = false }: {
  value: Expr; onChange: (value: Expr) => void; label?: string; depth?: number; allowSettlement?: boolean;
}) {
  const update = (key: string, next: unknown) => onChange({ ...value, [key]: next });
  const input = (key: string, title: string) => <label className="ast-field" key={key}>{title}<input className="inp" aria-label={`${label} ${title}`} value={String(value[key] ?? '')} onChange={e => update(key, e.target.value)} /></label>;
  const child = (key: string, title: string) => <ExpressionTreeEditor key={key} value={value[key] as Expr} onChange={next => update(key, next)} label={`${label} ${title}`} depth={depth + 1} allowSettlement={allowSettlement} />;
  return <details className="ast-tree" open={depth < 2}>
    <summary>{label}: <b>{value.exprType}</b>{value.value !== undefined ? ` ${value.value}` : value.op ? ` ${value.op}` : value.function ? ` ${value.function}` : ''}</summary>
    <label className="ast-field">Node type<select className="inp" aria-label={`${label} node type`} value={value.exprType} onChange={e => onChange(fresh(e.target.value))}>{types.map(t => <option key={t}>{t}</option>)}</select></label>
    {value.exprType === 'constant' && <><label className="ast-field">Kind<select className="inp" aria-label={`${label} kind`} value={String(value.kind)} onChange={e => update('kind', e.target.value)}>{['Integer', 'Rational', 'Boolean', 'String'].map(t => <option key={t}>{t}</option>)}</select></label>{input('value', 'Value')}</>}
    {value.exprType === 'fieldAccess' && <><label className="ast-field">Namespace<select className="inp" aria-label={`${label} namespace`} value={String(value.target ?? 'state')} onChange={e => update('target', e.target.value)}><option value="state">Game state</option>{allowSettlement && <option value="measurement">Completed-round settlement</option>}</select></label><label className="ast-field">State path<input className="inp" aria-label={`${label} state path`} value={(value.path as string[]).join('.')} onChange={e => update('path', e.target.value.split('.'))} /></label></>}
    {['binary', 'compare'].includes(value.exprType) && <><label className="ast-field">Operator<select className="inp" aria-label={`${label} operator`} value={String(value.op)} onChange={e => update('op', e.target.value)}>{(value.exprType === 'binary' ? ['Add', 'Sub', 'Mul', 'Div', 'And', 'Or'] : ['Eq', 'Neq', 'Lt', 'Gt', 'Lte', 'Gte']).map(t => <option key={t}>{t}</option>)}</select></label>{child('left', 'Left')}{child('right', 'Right')}</>}
    {value.exprType === 'if' && <>{child('condition', 'Condition')}{child('thenExpr', 'Then')}{child('elseExpr', 'Else')}</>}
    {value.exprType === 'not' && child('expr', 'Operand')}
    {value.exprType === 'call' && <>{input('function', 'Function')}{(value.args as Expr[]).map((arg, i) => <ExpressionTreeEditor key={i} value={arg} label={`${label} Argument ${i + 1}`} depth={depth + 1} onChange={next => update('args', (value.args as Expr[]).map((v, j) => i === j ? next : v))} />)}<button type="button" className="btn sm" onClick={() => update('args', [...value.args as Expr[], zero()])}>Add argument</button></>}
    {['aggregate', 'map', 'fold', 'filter'].includes(value.exprType) && <>{input('stateKey', 'Array state key')}{input('itemName', 'Item binding')}{input('indexName', 'Index binding')}
      <label className="ast-field">Item type<select className="inp" value={String(value.itemType ?? 'String')} onChange={e => update('itemType', e.target.value)}>{['String', 'Number', 'Boolean', 'Array'].map(t => <option key={t}>{t}</option>)}</select></label>
      {value.exprType === 'fold' && <>{input('accName', 'Accumulator')}{child('init', 'Initial')}</>}
      {value.exprType === 'aggregate' && <>{input('func', 'Aggregate function')}{value.predicate ? child('predicate', 'Predicate') : <button type="button" className="btn sm" onClick={() => update('predicate', fresh('compare'))}>Add predicate</button>}{value.valueExpr ? child('valueExpr', 'Value selector') : <button type="button" className="btn sm" onClick={() => update('valueExpr', zero())}>Add value selector</button>}</>}
      {['map', 'fold'].includes(value.exprType) && child('body', 'Body')}{value.exprType === 'filter' && child('predicate', 'Predicate')}</>}
  </details>;
}
