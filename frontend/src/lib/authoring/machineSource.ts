import type {Expr} from '../../games/shared/graph/expr';
import type {ModelDefinition, ModelGraph, ModelNode} from '../modelProjection';
import {reelObject} from '../reelCommands';
import {canonicalJson} from '../hash';
import {matchReelShape} from './reelShape';
import {indexGraphSources} from './sourceIndex';
import {
    type MachineAction,
    machineDone,
    type MachineIR,
    type MachineState,
    type MachineTransition,
    type MachineValue,
    type MachineVariable,
    validateMachine
} from './machineIR';
import {
    bindMachineExpression,
    lowerMachine,
    machineNamespace,
    machineNodeExpression,
    type MachineSourceBinding,
    machineSourceRevision
} from './machineLowering';

const decode = (value: string) => {
    if (!/^(?:[\da-f]{2})+$/.test(value)) throw new Error('Unmapped source identity.');
    return new TextDecoder('utf-8', {fatal: true}).decode(Uint8Array.from(value.match(/../g)!, byte => parseInt(byte, 16)));
};
const primitive = (value: unknown): value is MachineValue => typeof value === 'string' || typeof value === 'boolean' || typeof value === 'number' && Number.isFinite(value) && Math.abs(value) <= Number.MAX_SAFE_INTEGER;

function constant(expression: unknown): MachineValue {
    const ast = reelObject(expression);
    if (ast?.exprType !== 'constant') throw new Error('Expected an authored literal.');
    if (ast.kind === 'String' && typeof ast.value === 'string') return ast.value;
    if (ast.kind === 'Integer' && /^-?\d+$/.test(String(ast.value)) && Number.isSafeInteger(Number(ast.value))) return Number(ast.value);
    if (ast.kind === 'Boolean' && ['true', 'false'].includes(String(ast.value))) return ast.value === 'true';
    throw new Error('Unsupported literal.');
}

function source(graph: ModelGraph, name: string) {
    const value = graph.mechanics?.[name];
    if (!value) throw new Error('Missing generated definition.');
    return value;
}

const edges = (definition: ModelDefinition) => Array.isArray(definition.edges) ? definition.edges as {
    sourceNodeId: string;
    sourcePort: string;
    targetNodeId: string;
    targetPort: string
}[] : [];

function nextNode(definition: ModelDefinition, node: ModelNode, port = 'state'): ModelNode | null {
    const targets = edges(definition).filter(edge => edge.sourceNodeId === node.id && edge.sourcePort === port);
    if (targets.length > 1 || targets.some(edge => edge.targetPort !== 'state')) throw new Error('This operation has unrecognized fan-out/ports.');
    return targets.length ? definition.nodes.find(node => node.id === targets[0].targetNodeId) ?? null : null;
}

function entryNode(definition: ModelDefinition): ModelNode {
    const entries = definition.nodes.filter(node => !edges(definition).some(edge => edge.targetNodeId === node.id));
    if (entries.length !== 1) throw new Error('This source has multiple entries.');
    return entries[0];
}

function controlSelection(definition: ModelDefinition, node: ModelNode, pc: string): string {
    const ast = reelObject(machineNodeExpression(definition, node, 'conditionId')), left = reelObject(ast?.left);
    if (node.nodeType !== 'branch' || ast?.exprType !== 'compare' || ast.op !== 'Eq' || left?.exprType !== 'fieldAccess' || left.target !== 'state' || canonicalJson(left.path) !== canonicalJson([pc])) throw new Error('The state selection is not the exclusive machine dispatch contract.');
    const value = constant(ast.right);
    if (typeof value !== 'string' || value === machineDone) throw new Error('Invalid state identity.');
    return value;
}

/** Reconstruct all executable values from source, then verify them by regeneration. No Machine IR lives in view metadata. */
export function readMachineSource(graph: ModelGraph, definition: string): MachineSourceBinding | null {
    try {
        const wrapper = source(graph, definition), start = entryNode(wrapper),
            pc = `${machineNamespace(definition)}_pc`, diagnostic = `${machineNamespace(definition)}_diagnostic`,
            initial = reelObject(graph.initialState) ?? {};
        if (start.id !== 'start' || start.nodeType !== 'modifyState' || start.outputKey !== pc) return null;
        const entry = constant(machineNodeExpression(wrapper, start));
        if (typeof entry !== 'string') return null;
        const variables: MachineVariable[] = [], names = new Map<string, string>();
        let cursor = nextNode(wrapper, start);
        if (cursor?.outputKey !== diagnostic) return null;
        cursor = nextNode(wrapper, cursor);
        while (cursor?.nodeType === 'modifyState' && cursor.id.startsWith('reset-')) {
            const key = decode(cursor.id.slice(6)),
                actual = `${machineNamespace(definition)}_private_${cursor.id.slice(6)}`;
            if (cursor.outputKey !== actual) return null;
            variables.push({key, scope: 'private', initial: constant(machineNodeExpression(wrapper, cursor))});
            names.set(actual, key);
            cursor = nextNode(wrapper, cursor);
        }
        if (cursor?.nodeType !== 'loop' || cursor.id !== 'visits' || typeof cursor.maxIterations !== 'number') return null;
        const body = nextNode(wrapper, cursor, 'body');
        if (body?.nodeType !== 'library' || typeof body.mechanicName !== 'string') return null;
        const dispatch = source(graph, body.mechanicName), states: MachineState[] = [],
            scopes = [definition, body.mechanicName], seen = new Set<string>();
        const bindKey = (actual: string): string => {
            if (names.has(actual)) return names.get(actual)!;
            if (actual.startsWith(machineNamespace(definition))) throw new Error('Unknown private field binding.');
            if (!variables.some(variable => variable.key === actual)) {
                if (!primitive(initial[actual])) throw new Error('Declare this shared target through its graph operation.');
                variables.push({key: actual, scope: 'shared', initial: initial[actual]});
            }
            return actual;
        };
        const fieldsRead = new Set<string>(),
            unbind = (value: Expr): Expr => bindMachineExpression(value, names, fieldsRead);
        const action = (owner: ModelDefinition, node: ModelNode, prefix: string): MachineAction => {
            if (!node.id.startsWith(prefix)) throw new Error('An unknown operation interrupted the generated source.');
            const id = decode(node.id.slice(prefix.length)), label = String(node.label ?? id);
            if (node.nodeType === 'modifyState' && typeof node.outputKey === 'string') {
                const expression = machineNodeExpression(owner, node);
                if (!expression) throw new Error('Missing action expression.');
                return {id, label, kind: 'assign', key: bindKey(node.outputKey), expression: unbind(expression)};
            }
            if (node.nodeType === 'library' && typeof node.mechanicName === 'string') {
                const parameters = {...reelObject(node.parameters)} as Record<string, string>,
                    operation = source(graph, node.mechanicName),
                    descriptor = reelObject(operation.__machineParameterSource);
                if (descriptor) {
                    if (descriptor.version !== 1 || typeof descriptor.definition !== 'string' || !node.mechanicName.startsWith(`${definition}-op-`)) throw new Error('Unknown parameter specialization.');
                    const parameterBindings: Record<string, string> = {};
                    for (const [parameter, expressionId] of Object.entries(reelObject(descriptor.bindings) ?? {})) {
                        const binding = reelObject(reelObject(operation.expressions)?.[String(expressionId)]);
                        if (binding?.exprType !== 'fieldAccess' || binding.target !== 'state' || !Array.isArray(binding.path) || binding.path.length !== 1) throw new Error('Unmapped parameter variable.');
                        const actual = String(binding.path[0]);
                        parameterBindings[parameter] = names.get(actual) ?? actual;
                        fieldsRead.add(names.get(actual) ?? actual);
                    }
                    scopes.push(node.mechanicName);
                    return {
                        id,
                        label,
                        kind: 'library',
                        definition: descriptor.definition,
                        parameters,
                        parameterBindings
                    };
                }
                return {id, label, kind: 'library', definition: node.mechanicName, parameters};
            }
            if (node.nodeType === 'draw' && typeof node.stateWriteKey === 'string' && !node.weightExpressionId && Array.isArray(node.drawWeights)) return {
                id,
                label,
                kind: 'draw',
                key: bindKey(node.stateWriteKey),
                outcomes: node.drawWeights.map(row => ({id: row.outcomeId, weight: row.weight, payload: row.value}))
            };
            throw new Error('This operation requires its original graph editor.');
        };
        let branch: ModelNode | null = entryNode(dispatch);
        while (branch?.nodeType === 'branch') {
            if (seen.has(branch.id)) return null;
            seen.add(branch.id);
            const id = controlSelection(dispatch, branch, pc), visit = nextNode(dispatch, branch, 'true');
            if (visit?.nodeType !== 'library' || typeof visit.mechanicName !== 'string') return null;
            const owner = source(graph, visit.mechanicName);
            scopes.push(visit.mechanicName);
            const selected: MachineState = {
                id,
                label: String(owner.description ?? visit.label ?? id),
                terminal: false,
                actions: [],
                transitions: []
            };
            let operation: ModelNode | null = entryNode(owner);
            const visited = new Set<string>();
            const follow = (node: ModelNode) => {
                if (visited.has(node.id)) throw new Error('A raw cycle is not a generated transition.');
                visited.add(node.id);
            };
            while (operation?.id.startsWith('entry-action-')) {
                follow(operation);
                selected.actions.push(action(owner, operation, 'entry-action-'));
                operation = nextNode(owner, operation);
            }
            if (operation?.id === 'finish' && operation.outputKey === pc && constant(machineNodeExpression(owner, operation)) === machineDone) selected.terminal = true;
            else {
                const transitions = (node: ModelNode | null): void => {
                    if (!node) throw new Error('Missing transition/fallback.');
                    const conditional = node.nodeType === 'branch',
                        transitionId = conditional ? decode(node.id.slice('guard-'.length)) : null,
                        effects: MachineAction[] = [];
                    if (conditional) follow(node);
                    let leaf = conditional ? nextNode(owner, node, 'true') : node;
                    while (leaf?.id.startsWith('effect-')) {
                        follow(leaf);
                        const marker = leaf.id.indexOf('-action-');
                        effects.push(action(owner, leaf, leaf.id.slice(0, marker + 8)));
                        leaf = nextNode(owner, leaf);
                    }
                    if (!leaf?.id.startsWith('target-') || leaf.outputKey !== pc) throw new Error('Missing next-state assignment.');
                    const target = constant(machineNodeExpression(owner, leaf));
                    if (typeof target !== 'string') throw new Error('Next state must retain a String ID.');
                    const guard = conditional ? machineNodeExpression(owner, node, 'conditionId') : null;
                    if (conditional && !guard) throw new Error('Missing guard.');
                    const transition: MachineTransition = {
                        id: transitionId ?? decode(leaf.id.slice(7)),
                        label: String(leaf.label ?? ''),
                        target,
                        guard: guard ? unbind(guard) : null,
                        effects
                    };
                    selected.transitions.push(transition);
                    if (conditional) transitions(nextNode(owner, node, 'false'));
                };
                transitions(operation);
            }
            states.push(selected);
            branch = nextNode(dispatch, branch, 'false');
        }
        const machine: MachineIR = {
            version: 1,
            instance: definition,
            name: String(wrapper.description ?? start.label ?? definition),
            entry,
            stepBudget: cursor.maxIterations,
            variables,
            reads: [...fieldsRead].filter(key => !variables.some(variable => variable.key === key)),
            states
        };
        if (validateMachine(machine).length) return null;
        const emitted = lowerMachine(machine, graph.mechanics);
        const index = indexGraphSources(graph), owned = new Set(scopes), privateKeys = new Set(emitted.privateKeys);
        if (index.stateWrites.some(write => privateKeys.has(write.key) && !owned.has(write.source.scope ?? '')) || index.stateReads.some(read => privateKeys.has(String(read.statePath[0])) && (read.path[0] !== 'mechanics' || !owned.has(String(read.path[1]))))) return null;
        if (scopes.length !== new Set(scopes).size || Object.keys(emitted.definitions).length !== scopes.length || scopes.some(scope => !matchReelShape(source(graph, scope), emitted.definitions[scope]))) return null;
        return {definition, revision: machineSourceRevision(graph, scopes), machine, scopes};
    } catch {
        return null;
    }
}
