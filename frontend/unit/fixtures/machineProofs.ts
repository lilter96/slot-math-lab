import {Flow, gameGraph, Mechanics} from '../../src/games/shared/graph/flow';
import {add, eq, type Expr, lt, num, state, str} from '../../src/games/shared/graph/expr';
import {lowerMachine} from '../../src/lib/authoring/machineLowering';
import type {MachineAction, MachineIR} from '../../src/lib/authoring/machineIR';
import type {ModelDefinition, ModelGraph} from '../../src/lib/modelProjection';

const assign = (id: string, key: string, expression: Expr): MachineAction => ({
    kind: 'assign',
    id,
    label: id,
    key,
    expression
});

export function sequenceMachine(): MachineIR {
    return {
        version: 1,
        instance: 'sequence-machine',
        name: 'Sequence proof',
        entry: 'A',
        stepBudget: 4,
        variables: [{key: 'coins', scope: 'shared', initial: 0}],
        states: [
            {
                id: 'A',
                label: 'Action A',
                terminal: false,
                actions: [assign('A', 'coins', add(state('coins'), num(1)))],
                transitions: [{id: 'to-B', label: 'Next', target: 'B', guard: null, effects: []}]
            },
            {
                id: 'B',
                label: 'Action B',
                terminal: false,
                actions: [assign('B', 'coins', add(state('coins'), num(10)))],
                transitions: [{id: 'to-end', label: 'Next', target: 'end', guard: null, effects: []}]
            },
            {
                id: 'end',
                label: 'Terminal action',
                terminal: true,
                actions: [assign('terminal', 'coins', add(state('coins'), num(100)))],
                transitions: []
            },
        ]
    };
}

export function priorityMachine(): MachineIR {
    const result = sequenceMachine();
    result.instance = 'priority-machine';
    result.name = 'Post-entry guard/effect order';
    result.variables.push({key: 'counter', scope: 'private', initial: 0});
    result.states = [
        {
            id: 'A', label: 'Choose', terminal: false, actions: [assign('enter', 'counter', num(1))], transitions: [
                {
                    id: 'first',
                    label: 'First true guard',
                    target: 'end',
                    guard: eq(state('counter'), num(1)),
                    effects: [assign('first-effect', 'counter', add(state('counter'), num(1))), assign('second-effect', 'coins', state('counter'))]
                },
                {
                    id: 'second',
                    label: 'Second true guard must not run',
                    target: 'end',
                    guard: eq(state('counter'), num(1)),
                    effects: [assign('wrong', 'coins', num(999))]
                },
                {
                    id: 'fallback',
                    label: 'Fallback',
                    target: 'end',
                    guard: null,
                    effects: [assign('wrong-fallback', 'coins', num(888))]
                },
            ]
        },
        {
            id: 'end',
            label: 'Terminal sees effects next step',
            terminal: true,
            actions: [assign('terminal', 'coins', add(state('coins'), num(10)))],
            transitions: []
        },
    ];
    return result; // Expected 12, not 999/888, because guards see counter=1 and effects see updated counter=2.
}

export function weightedMachine(): MachineIR {
    const result = sequenceMachine();
    result.instance = 'weighted-machine';
    result.name = 'One weighted choice';
    result.variables.push({key: 'choice', scope: 'private', initial: ''});
    result.states = [
        {
            id: 'A',
            label: 'Choose once',
            terminal: false,
            actions: [{
                id: 'draw',
                label: 'One chance selection',
                kind: 'draw',
                key: 'choice',
                outcomes: [{id: 'one', weight: 1, payload: 0}, {id: 'three', weight: 3, payload: 0}, {
                    id: 'zero',
                    weight: 0,
                    payload: 0
                }]
            }],
            transitions: [
                {
                    id: 'one',
                    label: 'One',
                    target: 'end',
                    guard: eq(state('choice'), str('one')),
                    effects: [assign('one', 'coins', num(1))]
                },
                {
                    id: 'other',
                    label: 'Otherwise three',
                    target: 'end',
                    guard: null,
                    effects: [assign('three', 'coins', num(3))]
                },
            ]
        }, {id: 'end', label: 'End', terminal: true, actions: [], transitions: []},
    ];
    return result; // (1×1 + 3×3)/4 = 5/2.
}

export function selfMachine(): MachineIR {
    const result = sequenceMachine();
    result.instance = 'self-machine';
    result.name = 'Two visits then terminal';
    result.variables.push({key: 'counter', scope: 'private', initial: 0});
    result.states = [
        {
            id: 'A',
            label: 'Visit',
            terminal: false,
            actions: [assign('count', 'counter', add(state('counter'), num(1))), assign('pay', 'coins', add(state('coins'), num(1)))],
            transitions: [
                {
                    id: 'repeat',
                    label: 'Visit again below two',
                    target: 'A',
                    guard: lt(state('counter'), num(2)),
                    effects: []
                }, {id: 'finish', label: 'Finish', target: 'end', guard: null, effects: []},
            ]
        }, {
            id: 'end',
            label: 'Terminal',
            terminal: true,
            actions: [assign('terminal', 'coins', add(state('coins'), num(10)))],
            transitions: []
        },
    ];
    return result; // 1+1+10=12 in three visits; budget 4 stops naturally, budget 3 is modelLimit.
}

export function machineProofGraph(machine: MachineIR, operations: Record<string, ModelDefinition> = {}): ModelGraph {
    const emitted = lowerMachine(machine, operations), mechanics = new Mechanics();
    Object.values({...operations, ...emitted.definitions}).forEach(definition => mechanics.add(definition as unknown as Parameters<Mechanics['add']>[0]));
    return gameGraph({
        id: `proof-${machine.instance}`,
        name: machine.name,
        description: 'Independent expected outcomes; GraphConfig runs without authoring metadata.',
        root: new Flow().use('machine', machine.name, emitted.definition).sink('sink', 'One final payout', 'coins', 1000),
        mechanics,
        initialState: emitted.initialState,
        tables: {opaque: {retained: true}}
    }) as ModelGraph;
}
