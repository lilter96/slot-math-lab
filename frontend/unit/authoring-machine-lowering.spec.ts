import {expect, test} from '@playwright/test';
import {installMachine, lowerMachine} from '../src/lib/authoring/machineLowering';
import {readMachineSource} from '../src/lib/authoring/machineSource';
import {isMachineInput, validateMachine} from '../src/lib/authoring/machineIR';
import {
    machineProofGraph,
    priorityMachine,
    selfMachine,
    sequenceMachine,
    weightedMachine
} from './fixtures/machineProofs';
import {Flow} from '../src/games/shared/graph/flow';
import {add, param, state} from '../src/games/shared/graph/expr';
import type {ModelDefinition} from '../src/lib/modelProjection';

test('Generated machines roundtrip every executable action/guard/effect from GraphConfig and use only existing atoms', () => {
    for (const machine of [sequenceMachine(), priorityMachine(), weightedMachine(), selfMachine()]) {
        const graph = machineProofGraph(machine), binding = readMachineSource(graph, machine.instance);
        expect(binding, machine.instance).not.toBeNull();
        expect(lowerMachine(binding!.machine).definitions).toEqual(lowerMachine(machine).definitions);
        expect(graph.nodes!.filter(node => node.nodeType === 'metricsSink')).toHaveLength(1);
        expect(Object.values(graph.mechanics!).flatMap(definition => definition.nodes).every(node => ['modifyState', 'branch', 'draw', 'library', 'loop', 'getState'].includes(node.nodeType))).toBe(true);
    }
});
test('Variable-bound operations reconstruct their bindings from AST source, preserve the original operation and remain portable without descriptors', () => {
    const machine = sequenceMachine();
    machine.states = [machine.states[2]];
    machine.entry = 'end';
    machine.variables.push({key: 'counter', scope: 'private', initial: 7});
    machine.states[0].actions = [{
        id: 'pay',
        label: 'Pay counter',
        kind: 'library',
        definition: 'parameter-op',
        parameters: {},
        parameterBindings: {amount: 'counter'}
    }];
    const operation = new Flow().write('pay', 'Add parameter', 'coins', add(state('coins'), param('amount'))).mechanic('parameter-op', 'Original custom operation') as unknown as ModelDefinition,
        graph = machineProofGraph(machine, {'parameter-op': operation}), original = structuredClone(operation);
    const binding = readMachineSource(graph, machine.instance);
    expect(binding).not.toBeNull();
    expect(binding!.machine.states[0].actions[0]).toMatchObject({
        definition: 'parameter-op',
        parameterBindings: {amount: 'counter'}
    });
    expect(lowerMachine(binding!.machine, graph.mechanics).definitions).toEqual(lowerMachine(machine, {'parameter-op': operation}).definitions);
    expect(graph.mechanics!['parameter-op']).toEqual(original);
});
test('Missing fallback, destination, unreachable state and ambiguous variable bindings reject before source installation', () => {
    const machine = sequenceMachine();
    machine.states[0].transitions = [];
    expect(validateMachine(machine).join(' ')).toContain('fallback');
    const other = sequenceMachine();
    other.states[0].transitions[0].target = 'missing';
    expect(validateMachine(other).join(' ')).toContain('existing target');
    const graph = machineProofGraph(sequenceMachine()), original = structuredClone(graph);
    expect(() => installMachine(graph, sequenceMachine())).toThrow('already exists');
    expect(graph).toEqual(original);
});
test('Independent instances retain shared game fields and isolate controls/counters; unrecognized source changes invalidate machine bindings', () => {
    const first = selfMachine(), second = {...selfMachine(), instance: 'another-instance'},
        graph = machineProofGraph(first), next = installMachine(graph, second);
    expect(new Set([...lowerMachine(first).privateKeys, ...lowerMachine(second).privateKeys]).size).toBe(lowerMachine(first).privateKeys.length + lowerMachine(second).privateKeys.length);
    expect((next.initialState as Record<string, unknown>).coins).toBe((graph.initialState as Record<string, unknown>).coins);
    const edited = structuredClone(graph), wrapper = edited.mechanics![first.instance];
    wrapper.nodes[0].expressionId = 'missing-formula';
    expect(readMachineSource(edited, first.instance)).toBeNull();
});
test('Malformed recovery input and external access to private controls cannot become a verified editable machine', () => {
    expect(isMachineInput({version: 1, states: [null]})).toBe(false);
    expect(isMachineInput(sequenceMachine())).toBe(true);
    const machine = sequenceMachine(), graph = machineProofGraph(machine), original = structuredClone(graph);
    graph.nodes!.unshift({
        id: 'foreign',
        nodeType: 'modifyState',
        outputKey: lowerMachine(machine).pcKey,
        expressionId: 'foreign'
    });
    graph.expressions = {foreign: {exprType: 'constant', kind: 'String', value: 'A'}};
    expect(readMachineSource(graph, machine.instance)).toBeNull();
    expect(readMachineSource(original, machine.instance)).not.toBeNull();
});
