import type { ExpressionAst } from '../../../lib/expressionParser';

// Expression authoring helpers. Each one returns plain expression JSON; nothing is evaluated here.
export type Expr = ExpressionAst;
export type ItemType = 'String' | 'Number' | 'Boolean';

export const num = (value: number): Expr => ({ exprType: 'constant', kind: 'Integer', value: String(value) });
export const str = (value: string | number): Expr => ({ exprType: 'constant', kind: 'String', value: String(value) });
export const bool = (value: boolean): Expr => ({ exprType: 'constant', kind: 'Boolean', value: String(value) });
/** A value held in the round state. */
export const state = (key: string): Expr => ({ exprType: 'fieldAccess', target: 'state', path: [key] });
/** A parameter the enclosing subgraph was placed with. */
export const param = (key: string): Expr => ({ exprType: 'fieldAccess', target: 'state', path: ['expressions', key] });
/** The zero-based iteration counter of a loop node. */
export const iteration = (loopId: string): Expr => state(`__iter_${loopId}__`);

const binary = (op: string, left: Expr, right: Expr): Expr => ({ exprType: 'binary', op, left, right });
const compare = (op: string, left: Expr, right: Expr): Expr => ({ exprType: 'compare', op, left, right });
export const add = (...values: Expr[]): Expr => values.reduce((a, c) => binary('Add', a, c));
export const sub = (a: Expr, c: Expr) => binary('Sub', a, c);
export const mul = (...values: Expr[]): Expr => values.reduce((a, c) => binary('Mul', a, c));
export const div = (a: Expr, c: Expr) => binary('Div', a, c);
export const eq = (a: Expr, c: Expr) => compare('Eq', a, c);
export const neq = (a: Expr, c: Expr) => compare('Neq', a, c);
export const lt = (a: Expr, c: Expr) => compare('Lt', a, c);
export const lte = (a: Expr, c: Expr) => compare('Lte', a, c);
export const gt = (a: Expr, c: Expr) => compare('Gt', a, c);
export const gte = (a: Expr, c: Expr) => compare('Gte', a, c);
export const and = (...values: Expr[]): Expr => values.reduce((a, c) => binary('And', a, c));
export const or = (...values: Expr[]): Expr => values.reduce((a, c) => binary('Or', a, c));
export const not = (expr: Expr): Expr => ({ exprType: 'not', expr });
export const when = (condition: Expr, thenExpr: Expr, elseExpr: Expr): Expr => ({ exprType: 'if', condition, thenExpr, elseExpr });

const call = (fn: string, ...args: Expr[]): Expr => ({ exprType: 'call', function: fn, args });
export const min = (a: Expr, c: Expr) => call('min', a, c);
export const max = (a: Expr, c: Expr) => call('max', a, c);
export const floor = (a: Expr) => call('floor', a);
export const toNumber = (a: Expr) => call('toNumber', a);
export const toText = (a: Expr) => call('toString', a);
export const itemAt = (array: Expr, index: Expr) => call('index', array, index);
/** `state[key][index]` */
export const at = (key: string, index: Expr) => itemAt(state(key), index);
export const length = (key: string) => call('length', state(key));
export const ceil = (a: Expr) => call('ceil', a);
export const contains = (array: Expr, value: Expr) => call('contains', array, value);
export const has = (key: string, value: Expr) => contains(state(key), value);
export const appended = (array: Expr, value: Expr) => call('append', array, value);
export const append = (key: string, value: Expr) => appended(state(key), value);
/** The empty array every graph keeps in its state under `emptyArray`. */
export const emptyArray = () => state('emptyArray');
export const arrayOf = (values: Expr[]): Expr => values.reduce((a, c) => appended(a, c), emptyArray());

/** A balanced conditional that picks `values[index - first]`; used where the picked value is itself an expression. */
export function choose(index: Expr, values: Expr[], first = 0): Expr {
  if (values.length === 1) return values[0];
  const middle = Math.floor(values.length / 2);
  return when(lt(index, num(first + middle)), choose(index, values.slice(0, middle), first), choose(index, values.slice(middle), first + middle));
}

interface Iteration { item?: string; index?: string; itemType?: ItemType }
/** A new array with `body` applied to every element of `state[key]`; the element is `state.item`. */
export const mapOver = (key: string, body: Expr, { item = 'item', index, itemType = 'String' }: Iteration = {}): Expr =>
  ({ exprType: 'map', stateKey: key, itemName: item, ...(index ? { indexName: index } : {}), itemType, body });
/** The elements of `state[key]` for which `predicate` holds. */
export const filterOver = (key: string, predicate: Expr, { item = 'item', index, itemType = 'String' }: Iteration = {}): Expr =>
  ({ exprType: 'filter', stateKey: key, itemName: item, ...(index ? { indexName: index } : {}), itemType, predicate });
/** Left fold over `state[key]`; the running value is `state.acc`. */
export const foldOver = (key: string, init: Expr, body: Expr, { item = 'item', index, itemType = 'String' }: Iteration = {}): Expr =>
  ({ exprType: 'fold', stateKey: key, accName: 'acc', itemName: item, ...(index ? { indexName: index } : {}), itemType, init, body });
/** How many elements of `state[key]` satisfy `predicate`. */
export const countOver = (key: string, predicate: Expr, { item = 'item', itemType = 'String' }: Iteration = {}): Expr =>
  ({ exprType: 'aggregate', func: 'Count', stateKey: key, itemName: item, itemType, predicate });
/** Sum of `value` over the elements of `state[key]`. */
export const sumOver = (key: string, value: Expr, { item = 'item', itemType = 'String' }: Iteration = {}): Expr =>
  ({ exprType: 'aggregate', func: 'Sum', stateKey: key, itemName: item, itemType, valueExpr: value });
