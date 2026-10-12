import { Flow, type Mechanic } from '../../games/shared/graph/flow';
import { bool, eq, num, state, str, type Expr } from '../../games/shared/graph/expr';
import type { ModelDefinition, ModelGraph, ModelNode } from '../modelProjection';
import { canonicalJson } from '../hash';
import { reelObject } from '../reelCommands';
import { machineDone, validateMachine, type MachineAction, type MachineIR, type MachineValue } from './machineIR';
const segment = (value: string) => [...new TextEncoder().encode(value)].map(byte => byte.toString(16).padStart(2, '0')).join('');
export const machineNamespace = (instance: string) => `__machine_${segment(instance)}`;
const literal = (value: MachineValue) => typeof value === 'number' ? num(value) : typeof value === 'boolean' ? bool(value) : str(value);
export interface LoweredMachine { definition: string; definitions: Record<string, ModelDefinition>; initialState: Record<string, MachineValue>; privateKeys: string[]; pcKey: string; diagnosticKey: string }
/** Bind declared private variables to this instance. This transforms expressions; it never evaluates them. */
export function bindMachineExpression(expression: Expr, names: Map<string, string>, reads?: Set<string>): Expr {
  const next = structuredClone(expression);
  const visit = (value: unknown, locals: Set<string>): void => {
    if (Array.isArray(value)) { value.forEach(child => visit(child, locals)); return; } const ast = reelObject(value); if (!ast) return;
    if (ast.exprType === 'fieldAccess' && ast.target === 'state' && Array.isArray(ast.path) && !locals.has(String(ast.path[0])) && !(ast.path[0] === 'expressions' && ast.path.length > 1)) { const key = String(ast.path[0]); if (names.has(key)) ast.path[0] = names.get(key); reads?.add(String(ast.path[0])); }
    if (typeof ast.stateKey === 'string' && !locals.has(ast.stateKey)) { if (names.has(ast.stateKey)) ast.stateKey = names.get(ast.stateKey); reads?.add(String(ast.stateKey)); }
    const nested = new Set(locals);
    if (['map', 'filter', 'fold', 'aggregate'].includes(String(ast.exprType))) for (const field of ['itemName', 'indexName', 'accName']) if (typeof ast[field] === 'string') nested.add(String(ast[field]));
    for (const [key, child] of Object.entries(ast)) if (child && typeof child === 'object') visit(child, ['body', 'predicate', 'valueExpr'].includes(key) ? nested : locals);
  }; visit(next, new Set()); return next;
}
/** Text for the existing scalar parameter parser and human-readable guards, never an evaluator. */
export function machineExpressionText(value: Expr): string {
  const ast = value as Record<string, unknown>, text = (value: unknown) => machineExpressionText(value as Expr);
  if (ast.exprType === 'constant') { if (ast.kind === 'String') return JSON.stringify(ast.value); if (['Integer', 'Boolean'].includes(String(ast.kind))) return String(ast.value); if (ast.kind === 'Rational') return `(${String(ast.value)})`; }
  if (ast.exprType === 'fieldAccess' && ast.target === 'state' && Array.isArray(ast.path)) return 'state' + ast.path.map(key => `[${JSON.stringify(key)}]`).join('');
  const operators: Record<string, string> = { Add: '+', Sub: '-', Mul: '*', Div: '/', And: '&&', Or: '||', Eq: '==', Neq: '!=', Lt: '<', Lte: '<=', Gt: '>', Gte: '>=' };
  if (['binary', 'compare'].includes(String(ast.exprType)) && Object.hasOwn(operators, String(ast.op))) return `(${text(ast.left)} ${operators[String(ast.op)]} ${text(ast.right)})`;
  if (ast.exprType === 'not') return `!(${text(ast.expr)})`;
  if (ast.exprType === 'if') return `(${text(ast.condition)} ? ${text(ast.thenExpr)} : ${text(ast.elseExpr)})`;
  if (ast.exprType === 'call' && typeof ast.function === 'string' && Array.isArray(ast.args)) return `${ast.function}(${ast.args.map(text).join(', ')})`;
  throw new Error('This expression uses an operation outside the scalar text editor. Keep its source AST.');
}

/** A bounded exclusive dispatch loop. Existing graph atoms are the only executable authority. */
export function lowerMachine(machine: MachineIR, operations: Record<string, ModelDefinition> = {}): LoweredMachine {
  const errors = validateMachine(machine); if (errors.length) throw new Error(errors.join(' '));
  const prefix = machineNamespace(machine.instance), pcKey = `${prefix}_pc`, diagnosticKey = `${prefix}_diagnostic`, names = new Map(machine.variables.map(variable => [variable.key, variable.scope === 'private' ? `${prefix}_private_${segment(variable.key)}` : variable.key]));
  const reads = new Set<string>(), actionReads = (value: MachineAction) => { if (value.kind === 'assign') bindMachineExpression(value.expression, new Map(), reads); };
  machine.states.forEach(selected => { selected.actions.forEach(actionReads); selected.transitions.forEach(transition => { if (transition.guard) bindMachineExpression(transition.guard, new Map(), reads); transition.effects.forEach(actionReads); }); });
  if ([...reads].some(key => !names.has(key) && !machine.reads?.includes(key))) throw new Error('Declare every directly read game field in the variable bindings or read-only field list.');
  if ([...names.values()].some(key => key === pcKey || key === diagnosticKey)) throw new Error('A shared game field conflicts with this instance’s reserved control namespace.');
  const definitions: Record<string, ModelDefinition> = {}, register = (definition: Mechanic) => { definitions[definition.name] = definition as unknown as ModelDefinition; return definition.name; };
  let retainedDraws = new Map<string, Extract<MachineAction, { kind: 'draw' }>>();
  let selectedState = '';
  const action = (flow: Flow, value: MachineAction, prefix = 'entry') => {
    const id = `${prefix}-action-${segment(value.id)}`;
    if (value.kind === 'assign') flow.write(id, value.label, names.get(value.key)!, bindMachineExpression(value.expression, names));
    else if (value.kind === 'library') {
      let operation = value.definition; const parameters = { ...value.parameters }, bindings = Object.entries(value.parameterBindings ?? {});
      if (bindings.length) {
        const original = operations[value.definition]; if (!original || original.__machineParameterSource !== undefined) throw new Error('This parameter binding needs an available original operation without a conflicting source descriptor.');
        operation = `${machine.instance}-op-${segment(selectedState)}-${id}`; const clone = structuredClone(original), ids: Record<string, string> = Object.create(null);
        const ast = { ...reelObject(clone.expressions) }; for (const [parameter] of bindings) { if (Object.hasOwn(ast, parameter)) throw new Error(`Parameter ${parameter} conflicts with an operation expression.`); const expressionId = `__binding_${segment(parameter)}`; if (Object.hasOwn(ast, expressionId)) throw new Error('A parameter-binding expression already exists.'); ids[parameter] = expressionId; delete parameters[parameter]; }
        const visit = (value: unknown): unknown => { if (Array.isArray(value)) return value.map(visit); const expression = reelObject(value); if (!expression) return value; if (expression.exprType === 'fieldAccess' && expression.target === 'state' && Array.isArray(expression.path) && expression.path[0] === 'expressions') { const parameterName = expression.path[1], binding = bindings.find(([parameter]) => parameter === parameterName); if (binding) return { ...expression, path: [names.get(binding[1]) ?? binding[1], ...expression.path.slice(2)] }; } return Object.fromEntries(Object.entries(expression).map(([key, child]) => [key, child && typeof child === 'object' ? visit(child) : child])); };
        clone.expressions = visit(ast); for (const [parameter, variable] of bindings) (clone.expressions as Record<string, Expr>)[ids[parameter]] = state(names.get(variable) ?? variable);
        clone.name = operation; clone.__machineParameterSource = { version: 1, definition: value.definition, bindings: ids }; definitions[operation] = clone;
      }
      flow.use(id, value.label, operation, parameters);
    }
    else { flow.draw(id, value.label, names.get(value.key)!, value.outcomes.map(outcome => [outcome.id, Math.max(1, outcome.weight)] as const)); retainedDraws.set(id, value); }
  };
  const states = new Map<string, string>();
  for (const selected of machine.states) {
    selectedState = selected.id;
    retainedDraws = new Map();
    const flow = new Flow(); selected.actions.forEach(value => action(flow, value));
    if (selected.terminal) flow.write('finish', 'Terminal visit completed', pcKey, str(machineDone));
    else {
      const choose = (path: Flow, index: number): void => {
        const transition = selected.transitions[index], effect = (branch: Flow) => { transition.effects.forEach(value => action(branch, value, `effect-${segment(transition.id)}`)); branch.write(`target-${segment(transition.id)}`, transition.label || 'Next state', pcKey, str(transition.target)); };
        if (transition.guard === null) effect(path); else path.either(`guard-${segment(transition.id)}`, transition.label, bindMachineExpression(transition.guard, names), effect, rest => choose(rest, index + 1));
      }; choose(flow, 0);
    }
    const definition = flow.mechanic(`${machine.instance}-state-${segment(selected.id)}`, selected.label);
    for (const node of definition.nodes) if (node.nodeType === 'draw') { const value = retainedDraws.get(node.id)!; node.drawWeights = value.outcomes.map(outcome => ({ outcomeId: outcome.id, weight: outcome.weight, value: outcome.payload })); }
    states.set(selected.id, register(definition));
  }
  const dispatch = new Flow(), chooseState = (path: Flow, index: number): void => {
    if (index === machine.states.length) { path.write('invalid-state', 'Unknown machine state: inspect this diagnostic; the Loop limit remains authoritative', diagnosticKey, str('Unknown current state')); return; }
    const selected = machine.states[index]; path.either(`state-${segment(selected.id)}`, selected.label, eq(state(pcKey), str(selected.id)), branch => branch.use(`visit-${segment(selected.id)}`, selected.label, states.get(selected.id)!), branch => chooseState(branch, index + 1));
  }; chooseState(dispatch, 0);
  const dispatchName = register(dispatch.mechanic(`${machine.instance}-dispatch`, 'One state visit: exclusive selection, entry actions, ordered guards, effects, then next state.'));
  const wrapper = new Flow().write('start', machine.name, pcKey, str(machine.entry)).write('diagnostic', 'Clear this invocation’s diagnostic', diagnosticKey, str(''));
  machine.variables.filter(variable => variable.scope === 'private').forEach(variable => wrapper.write(`reset-${segment(variable.key)}`, variable.key, names.get(variable.key)!, literal(variable.initial)));
  wrapper.repeat('visits', 'State visits · includes the terminal visit; limit precedence is unchanged', machine.stepBudget, dispatchName, { until: eq(state(pcKey), str(machineDone)) });
  register(wrapper.mechanic(machine.instance, machine.name));
  return { definition: machine.instance, definitions, initialState: { [pcKey]: machine.entry, [diagnosticKey]: '', ...Object.fromEntries(machine.variables.map(variable => [names.get(variable.key)!, variable.initial])) }, privateKeys: [pcKey, diagnosticKey, ...machine.variables.filter(variable => variable.scope === 'private').map(variable => names.get(variable.key)!)], pcKey, diagnosticKey };
}

export function installMachine(graph: ModelGraph, machine: MachineIR): ModelGraph {
  const emitted = lowerMachine(machine, graph.mechanics), next = structuredClone(graph);
  if (Object.keys(emitted.definitions).some(name => Object.hasOwn(graph.mechanics ?? {}, name))) throw new Error('A generated definition already exists. Choose a fresh machine instance identity.');
  let initial = { ...reelObject(graph.initialState) };
  for (const [key, value] of Object.entries(emitted.initialState)) { if (Object.hasOwn(initial, key) && (emitted.privateKeys.includes(key) || typeof initial[key] !== typeof value)) throw new Error(`State field ${key} conflicts with this instance/binding.`); if (!Object.hasOwn(initial, key)) initial = { ...initial, [key]: value }; }
  next.initialState = initial; next.mechanics = { ...graph.mechanics, ...emitted.definitions }; return next;
}

export interface MachineSourceBinding { definition: string; revision: string; machine: MachineIR; scopes: string[] }
/** Recognition is intentionally limited to the emitted structure; an arbitrary graph keeps its original operations. */
export function machineSourceRevision(graph: ModelGraph, scopes: string[]): string { return canonicalJson({ scopes, graph }); }
export const machineNodeExpression = (definition: ModelDefinition, node: ModelNode, field = 'expressionId') => reelObject(definition.expressions)?.[String(node[field])] as Expr | undefined;
