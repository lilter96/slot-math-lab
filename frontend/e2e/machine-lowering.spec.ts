import {type APIRequestContext, expect, test} from './fixtures';
import {
    machineProofGraph,
    priorityMachine,
    selfMachine,
    sequenceMachine,
    weightedMachine
} from '../unit/fixtures/machineProofs';
import {lowerMachine} from '../src/lib/authoring/machineLowering';
import {copyMachineUse} from '../src/lib/authoring/machineLifecycle';
import {Flow, gameGraph, Mechanics} from '../src/games/shared/graph/flow';
import {add, param, state, sumOver} from '../src/games/shared/graph/expr';
import {coinState, holdAndSpin, holdAndSpinState} from '../src/games/shared/graph/mechanics/hold-and-spin';
import {changeRespinCount, respinContracts} from '../src/lib/authoring/respinContract';
import type {MachineIR} from '../src/lib/authoring/machineIR';
import type {ModelGraph} from '../src/lib/modelProjection';

async function run(request: APIRequestContext, graph: ModelGraph, expected: number) {
    const validation = await request.post('/api/validate', {data: {config: graph}}), checked = await validation.json();
    expect(checked, JSON.stringify(checked)).toMatchObject({isValid: true});
    const response = await request.post('/api/play/round', {data: {config: graph, seed: 42, roundIndex: 0}}),
        body = await response.json();
    expect(response.ok(), JSON.stringify(body)).toBe(true);
    expect(body.win).toBe(expected);
    return body as { win: number; state: Record<string, unknown> };
}

const loopReasons = (state: Record<string, unknown>) => Object.entries(state).filter(([key]) => key.startsWith('__exitReason_')).map(([, value]) => value);
test('Independent copy includes a nested machine behind an opaque operation and preserves the raw graph payout of 36', async ({request}) => {
    const inner = {...selfMachine(), instance: 'inner'}, outer = {...selfMachine(), instance: 'outer'},
        wrapper = new Flow().use('inner', 'Nested invocation', 'inner').mechanic('wrapper', 'Opaque graph operation') as unknown as NonNullable<ModelGraph['mechanics']>[string];
    outer.states[0].actions.unshift({
        kind: 'library',
        id: 'wrapped',
        label: 'Wrapped invocation',
        definition: 'wrapper',
        parameters: {}
    });
    const graph = machineProofGraph(outer, {...lowerMachine(inner).definitions, wrapper}),
        copied = copyMachineUse(graph, JSON.stringify([null, 'machine']), 'independent');
    await run(request, graph, 36);
    await run(request, copied, 36);
});
test('Machine graph-only sequence: A, B and terminal each execute once and one outer sink pays 111', async ({request}) => {
    const machine = sequenceMachine(), result = await run(request, machineProofGraph(machine), 111);
    expect(result.state[lowerMachine(machine).pcKey]).toBe('__Done__');
    expect(loopReasons(result.state)).toContain('condition');
    expect(loopReasons(result.state)).not.toContain('modelLimit');
});
test('An existing fractional shared payout field is retained by the ordinary machine graph and pays 111.5', async ({request}) => {
    const graph = machineProofGraph(sequenceMachine());
    (graph.initialState as Record<string, unknown>).coins = 0.5;
    graph.nodes!.find(node => node.nodeType === 'metricsSink')!.settlement = {quantum: '0.5', mode: 'nearestEven'};
    await run(request, graph, 111.5);
});
test('Machine explicit variable parameter binding specializes the original graph operation and pays seven', async ({request}) => {
    const machine: MachineIR = {
        ...sequenceMachine(),
        entry: 'end',
        stepBudget: 2,
        variables: [{key: 'coins', scope: 'shared', initial: 0}, {key: 'counter', scope: 'private', initial: 7}],
        states: [{
            id: 'end',
            label: 'Pay the argument',
            terminal: true,
            transitions: [],
            actions: [{
                kind: 'library',
                id: 'pay',
                label: 'Pay private counter',
                definition: 'pay-parameter',
                parameters: {},
                parameterBindings: {amount: 'counter'}
            }]
        }]
    };
    const operation = new Flow().write('pay', 'Add the argument', 'coins', add(state('coins'), param('amount'))).mechanic('pay-parameter', 'An ordinary parameterized operation') as unknown as NonNullable<ModelGraph['mechanics']>[string],
        graph = machineProofGraph(machine, {'pay-parameter': operation});
    await run(request, graph, 7);
    for (const definition of Object.values(graph.mechanics!)) delete definition.__machineParameterSource;
    await run(request, graph, 7);
});
test('Machine post-entry guards and sequential effects: first true wins, next state waits for the next visit and pays 12', async ({request}) => {
    await run(request, machineProofGraph(priorityMachine()), 12);
});
test('Machine weighted choice: one Draw has independent exact payout (1×1 + 3×3)/4 = 5/2, including an authored zero row', async ({request}) => {
    const graph = machineProofGraph(weightedMachine()),
        validation = await request.post('/api/validate', {data: {config: graph}});
    expect(await validation.json()).toMatchObject({isValid: true});
    const response = await request.post('/api/evaluate/light', {
        data: {
            config: graph,
            maxBranches: 10000,
            sampleSize: 1000
        }
    });
    expect(response.ok()).toBe(true);
    const result = await response.json();
    expect(result.strategy).toBe('Exact');
    expect(result.rtp).toBe(2.5);
});
test('Machine self transition: two entry visits and one terminal visit pay 12; exact limit boundary remains modelLimit', async ({request}) => {
    const machine = selfMachine(), natural = await run(request, machineProofGraph(machine), 12);
    expect(loopReasons(natural.state)).toContain('condition');
    expect(loopReasons(natural.state)).not.toContain('modelLimit');
    const limited = await run(request, machineProofGraph({...machine, stepBudget: 3}), 12);
    expect(loopReasons(limited.state)).toContain('modelLimit');
    const shorter = await run(request, machineProofGraph({...machine, stepBudget: 2}), 2);
    expect(loopReasons(shorter.state)).toContain('modelLimit');
    expect(shorter.state[lowerMachine(machine).pcKey]).toBe('end');
});
test('Machine initially terminal: its action runs once; budget 1 still classifies the boundary as modelLimit', async ({request}) => {
    const machine: MachineIR = {
        ...sequenceMachine(),
        entry: 'end',
        stepBudget: 2,
        states: [sequenceMachine().states[2]]
    };
    const natural = await run(request, machineProofGraph(machine), 100);
    expect(loopReasons(natural.state)).toContain('condition');
    const limited = await run(request, machineProofGraph({...machine, stepBudget: 1}), 100);
    expect(loopReasons(limited.state)).toContain('modelLimit');
});
test('Machine nested instances: inner completion does not corrupt outer control; private counters remain separate and shared coins pay 36', async ({request}) => {
    const inner = {...selfMachine(), instance: 'inner-machine'}, outer = {...selfMachine(), instance: 'outer-machine'};
    outer.states[0].actions.unshift({
        id: 'nested',
        label: 'One inner invocation on each outer visit',
        kind: 'library',
        definition: inner.instance,
        parameters: {}
    });
    const parts = [lowerMachine(inner), lowerMachine(outer)], mechanics = new Mechanics();
    parts.forEach(part => Object.values(part.definitions).forEach(definition => mechanics.add(definition as unknown as Parameters<Mechanics['add']>[0])));
    const graph = gameGraph({
        id: 'nested-machine-proof',
        name: 'Nested instance proof',
        description: 'Two inner calls pay 12 each, outer visits pay 2 and its terminal pays 10: 36.',
        root: new Flow().use('outer', 'Outer instance', outer.instance).sink('sink', 'One payout', 'coins', 1000),
        mechanics,
        initialState: Object.assign({}, ...parts.map(part => part.initialState))
    }) as ModelGraph;
    const result = await run(request, graph, 36);
    for (const part of parts) expect(result.state[part.pcKey]).toBe('__Done__');
    expect(loopReasons(result.state)).not.toContain('modelLimit');
});
test('Machine private binding preserves lexical map/fold item fields while namespacing the outer private variable', async ({request}) => {
    const machine: MachineIR = {
        ...sequenceMachine(),
        entry: 'end',
        stepBudget: 2,
        variables: [{key: 'coins', scope: 'shared', initial: 0}, {key: 'item', scope: 'private', initial: 100}],
        reads: ['values'],
        states: [{
            id: 'end',
            label: 'Lexical sum',
            terminal: true,
            transitions: [],
            actions: [{
                kind: 'assign',
                id: 'sum',
                label: 'Array sum plus private item',
                key: 'coins',
                expression: add(sumOver('values', state('item'), {itemType: 'Number'}), state('item'))
            }]
        }]
    };
    const graph = machineProofGraph(machine);
    graph.initialState = {...(graph.initialState as Record<string, unknown>), values: [2, 3]};
    await run(request, graph, 105);
});
test('Existing respin initialization/reset edit preserves the helper and changes independent exact coin expectation 5/4 → 55/32', async ({request}) => {
    const mechanics = new Mechanics(), operation = mechanics.add(holdAndSpin(mechanics, {
        cells: 2,
        respins: 1,
        heldAtStart: 0,
        hitWeight: 1,
        missWeight: 1,
        prizes: [[1, 1]]
    }));
    const graph = gameGraph({
        id: 'respin-bound-proof',
        name: 'Independent two-cell respins',
        description: 'Two fair empty cells, unit prizes. One miss allowance: 5/4. Two: 55/32.',
        root: new Flow().use('bonus', 'Existing structured respins', operation).sink('sink', 'Coin sum', 'bonusCoins', 10),
        mechanics,
        initialState: {...coinState(2, 1), ...holdAndSpinState(2)}
    }) as ModelGraph;
    const binding = respinContracts(graph)[0];
    expect(binding).toBeDefined();
    const next = changeRespinCount(graph, binding, 2);
    for (const [config, expected] of [[graph, 5 / 4], [next, 55 / 32]] as const) {
        const response = await request.post('/api/evaluate/light', {
            data: {
                config,
                maxBranches: 10000,
                sampleSize: 1000
            }
        });
        expect(response.ok()).toBe(true);
        const result = await response.json();
        expect(result.strategy).toBe('Exact');
        expect(result.rtp).toBeCloseTo(expected, 12);
    }
});
