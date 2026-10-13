/**
 * Independent regression proposals for PR #19 at 3d8ce369.
 * Copy to frontend/unit/pr19-authoring-audit.spec.ts and run the normal
 * Playwright unit config. These tests import REAL repository modules.
 * This file was not executed in the review container: the full repository
 * and dependencies were unavailable there. Separate isolated probes were run.
 * No test updates expected values to accommodate the incorrect transformation.
 */
import {expect, test} from '@playwright/test';
import {bindMachineExpression, lowerMachine} from '../src/lib/authoring/machineLowering';
import {indexGraphSources} from '../src/lib/authoring/sourceIndex';
import {sequenceMachine, weightedMachine} from './fixtures/machineProofs';
import type {ModelDefinition, ModelGraph} from '../src/lib/modelProjection';
import type {Expr} from '../src/games/shared/graph/expr';
import {payBindings} from '../src/lib/authoring/payBindings';
import {clusterStudioProof} from './fixtures/clusterStudioProof';
import {computedLineBoundProof} from './fixtures/computedLineBoundProof';
import {changePayingDomain} from '../src/lib/authoring/symbolLifecycle';
import {
    isMachineInput,
    machineInitialInput,
    resolveMachineInput,
    validateMachine
} from '../src/lib/authoring/machineIR';

const privateItem = '__machine_probe_private_item';
const privateCounter = '__machine_probe_private_counter';
const names = new Map([['item', privateItem], ['counter', privateCounter]]);
const field = (key: string): Expr => ({exprType: 'fieldAccess', target: 'state', path: [key]});

// Backend AggregateExpr defaults ItemName to "item" when the JSON omits it.
// The scalar private variable of that name must NOT capture the aggregate local.
test('PR19: aggregate default item binding survives private-variable lowering', () => {
    const expression: Expr = {
        exprType: 'aggregate', func: 'Sum', stateKey: 'values', itemType: 'Number',
        valueExpr: field('item'),
    };
    const result = bindMachineExpression(expression, names);
    expect((result.valueExpr as Expr).path).toEqual(['item']);
    expect(result.stateKey).toBe('values');
    expect((expression.valueExpr as Expr).path).toEqual(['item']);
});

test('PR19 control: explicitly named aggregate local is not captured', () => {
    const expression: Expr = {
        exprType: 'aggregate', func: 'Sum', stateKey: 'values', itemType: 'Number',
        itemName: 'item', valueExpr: field('item'),
    };
    expect((bindMachineExpression(expression, names).valueExpr as Expr).path).toEqual(['item']);
});

for (const target of [undefined, null, 'state', 'board'] as const) {
    test(`PR19: runtime-supported field namespace ${String(target)} binds private variables`, () => {
        const expression: Expr = {
            exprType: 'fieldAccess', path: ['counter'],
            ...(target === undefined ? {} : {target}),
        };
        const reads = new Set<string>();
        const result = bindMachineExpression(expression, names, reads);
        expect(result.path).toEqual([privateCounter]);
        expect(reads.has(privateCounter)).toBe(true);
    });
}

test('PR19: stateKey in an aggregate is a dependency even without a fieldAccess node', () => {
    const graph = {
        nodes: [], edges: [], initialState: {clusterPays: [2, 3]},
        expressions: {total: {exprType: 'aggregate', func: 'Sum', stateKey: 'clusterPays', itemType: 'Number'}},
    } as ModelGraph;
    expect(indexGraphSources(graph).stateReads.map(read => read.statePath)).toContainEqual(['clusterPays']);
});

test('PR19: map reads its source array, not the lexical item as global game state', () => {
    const graph = {
        nodes: [], edges: [], initialState: {values: [2, 3]},
        expressions: {
            copy: {
                exprType: 'map', stateKey: 'values', itemName: 'x', itemType: 'Number',
                body: field('x'),
            }
        },
    } as ModelGraph;
    const reads = indexGraphSources(graph).stateReads.map(read => read.statePath);
    expect(reads).toContainEqual(['values']);
    expect(reads).not.toContainEqual(['x']);
});

test('PR19: Draw BoardStateKey is recorded as an actual state write', () => {
    const graph = {
        nodes: [{id: 'reel-draw', nodeType: 'draw', boardStateKey: 'board'}], edges: [],
    } as ModelGraph;
    expect(indexGraphSources(graph).stateWrites.map(write => write.key)).toContain('board');
});

test('PR19: direct node-to-parameter references remain resolvable after variable binding', () => {
    const machine = sequenceMachine();
    machine.states = [machine.states[2]];
    machine.entry = 'end';
    machine.variables.push({key: 'counter', scope: 'private', initial: 7});
    machine.states[0].actions = [{
        id: 'pay', label: 'Pay counter', kind: 'library', definition: 'parameter-op',
        parameters: {}, parameterBindings: {amount: 'counter'},
    }];
    const ports = {state: {name: 'state', type: 'State'}};
    const operation = {
        name: 'parameter-op', description: 'The parameter is referenced directly by a ModifyState node.',
        nodes: [{
            id: 'pay', nodeType: 'modifyState', expressionId: 'amount', outputKey: 'coins',
            inputs: ports, outputs: ports,
        }],
        edges: [], expressions: {},
    } as ModelDefinition;
    const lowered = lowerMachine(machine, {'parameter-op': operation});
    const specialized = Object.values(lowered.definitions).find(value => Object.hasOwn(value, '__machineParameterSource'));
    expect(specialized).toBeDefined();
    const reference = String(specialized!.nodes[0].expressionId);
    const call = Object.values(lowered.definitions).flatMap(value => value.nodes)
        .find(node => node.nodeType === 'library' && node.mechanicName === specialized!.name);
    const resolved = Object.hasOwn(specialized!.expressions ?? {}, reference)
        || Object.hasOwn(call?.parameters ?? {}, reference);
    expect(resolved, `Dangling parameter reference: ${reference}`).toBe(true);
});

test('PR19: fold initializer and source use the outer scope; body item/index/accumulator shadow private fields', () => {
    const bindings = new Map([['item', privateItem], ['index', 'private-index'], ['acc', 'private-acc'], ['values', 'private-values']]);
    const expression: Expr = {
        exprType: 'fold',
        stateKey: 'values',
        itemName: 'item',
        indexName: 'index',
        accName: 'acc',
        itemType: 'Number',
        init: field('acc'),
        body: {
            exprType: 'binary',
            op: 'Add',
            left: field('acc'),
            right: {exprType: 'binary', op: 'Add', left: field('item'), right: field('index')}
        }
    };
    const reads = new Set<string>(), result = bindMachineExpression(expression, bindings, reads);
    expect(result.stateKey).toBe('private-values');
    expect((result.init as Expr).path).toEqual(['private-acc']);
    expect(result.body).toEqual(expression.body);
    expect([...reads].sort()).toEqual(['private-acc', 'private-values']);
});

test('PR19: nested lexical captures use the outer collection item as a source without inventing a global read', () => {
    const expression: Expr = {
        exprType: 'aggregate', func: 'Sum', stateKey: 'values', itemType: 'Array',
        valueExpr: {
            exprType: 'aggregate',
            func: 'Sum',
            stateKey: 'item',
            itemName: 'cell',
            itemType: 'Number',
            valueExpr: field('cell')
        }
    };
    const reads = new Set<string>(), result = bindMachineExpression(expression, names, reads);
    expect((result.valueExpr as Expr).stateKey).toBe('item');
    expect([...reads]).toEqual(['values']);
    const index = indexGraphSources({nodes: [], expressions: {nested: expression}});
    expect(index.stateReads.map(read => read.statePath)).toEqual([['values']]);
});

test('PR19: measurement fields remain separate and aggregate does not invent index/accumulator locals', () => {
    const expression: Expr = {
        exprType: 'aggregate', func: 'Sum', stateKey: 'values', itemType: 'Number', indexName: 'counter',
        valueExpr: {
            exprType: 'binary',
            op: 'Add',
            left: field('counter'),
            right: {exprType: 'fieldAccess', target: 'measurement', path: ['counter']}
        }
    };
    const result = bindMachineExpression(expression, names);
    expect(((result.valueExpr as Expr).left as Expr).path).toEqual([privateCounter]);
    expect(((result.valueExpr as Expr).right as Expr).path).toEqual(['counter']);
});

for (const type of ['map', 'filter'] as const) test(`PR19: ${type} binds source before explicit item/index body locals`, () => {
    const body = {exprType: 'binary', op: 'Add', left: field('item'), right: field('counter')};
    const expression = {
        exprType: type,
        stateKey: 'item',
        itemName: 'item',
        indexName: 'counter',
        itemType: 'Number',
        [type === 'map' ? 'body' : 'predicate']: body
    } as Expr;
    const reads = new Set<string>(), result = bindMachineExpression(expression, names, reads);
    expect(result.stateKey).toBe(privateItem);
    expect(result[type === 'map' ? 'body' : 'predicate']).toEqual(body);
    expect([...reads]).toEqual([privateItem]);
});

test('PR19: dependency inventory covers state atoms, actual draw contracts, inline port defaults and loop exit reasons', () => {
    const graph: ModelGraph = {
        nodes: [
            {id: 'get', nodeType: 'getState', stateKey: 'input'}, {id: 'get-default', nodeType: 'getState'},
            {id: 'put', nodeType: 'putState', stateKey: 'output'}, {
                id: 'table',
                nodeType: 'data',
                stateKey: 'table',
                values: ['1']
            },
            {
                id: 'inline',
                nodeType: 'draw',
                stateWriteKey: 'outcome',
                boardStateKey: 'unused-board',
                drawWeights: [{outcomeId: 'a', weight: 1, value: 0}]
            },
            {id: 'reels', nodeType: 'draw', boardStateKey: 'custom-board'},
            {
                id: 'loop',
                nodeType: 'loop',
                exitReason: {exprType: 'fieldAccess', path: ['reason']},
                inputs: {
                    in: {
                        name: 'in',
                        type: 'Number',
                        defaultValue: {exprType: 'fieldAccess', target: 'board', path: ['default']}
                    }
                }
            }
        ], edges: []
    };
    const before = structuredClone(graph), index = indexGraphSources(graph);
    expect(index.stateReads.map(read => read.statePath)).toEqual([['input'], ['__default__'], ['reason'], ['default']]);
    expect(index.stateWrites.map(write => write.key)).toEqual(['output', 'table', 'outcome', 'custom-board', 'rows', 'cols', '__iter_loop__', '__wins_loop__']);
    expect(index.unresolvedEffects).toEqual([]);
    expect(graph).toEqual(before);
});

test('PR19: named expression closure records real array dependencies and exposes unresolved cycles', () => {
    const graph: ModelGraph = {
        nodes: [{id: 'write', nodeType: 'modifyState', outputKey: 'coins', expressionId: 'outer'}],
        edges: [],
        expressions: {
            outer: {exprType: 'fieldAccess', path: ['expressions', 'inner']},
            inner: {exprType: 'aggregate', func: 'Sum', stateKey: 'values', itemType: 'Number'}
        }
    };
    const index = indexGraphSources(graph);
    expect(index.expressionReferences.find(reference => reference.id === 'outer')).toMatchObject({
        resolved: true,
        statePaths: [['values']]
    });
    expect(index.unresolvedEffects).toEqual([]);
    (graph.expressions as Record<string, unknown>).inner = {
        exprType: 'fieldAccess',
        target: 'state',
        path: ['expressions', 'outer']
    };
    expect(indexGraphSources(graph).unresolvedEffects.length).toBeGreaterThan(0);
    (graph.expressions as Record<string, unknown>).inner = {exprType: 'futureExpression', stateKey: 'values'};
    expect(indexGraphSources(graph).unresolvedEffects.length).toBeGreaterThan(0);
});

for (const [name, create, table] of [['line', computedLineBoundProof, 'linePays'], ['cluster', clusterStudioProof, 'clusterPays']] as const) {
    for (const reader of [
        {exprType: 'aggregate', func: 'Sum', stateKey: table, itemType: 'Number'},
        {exprType: 'fieldAccess', target: 'board', path: [table]},
        {exprType: 'fieldAccess', path: [table]}
    ]) test(`PR19: additional ${name} ${reader.exprType}/${String(reader.target)} reader cannot evade payout reconciliation`, () => {
        const graph = create() as ModelGraph;
        const binding = payBindings(graph).find(binding => binding.kind === name)!;
        expect(binding).toBeDefined();
        graph.expressions = {...graph.expressions as Record<string, unknown>, customRead: reader};
        if (name === 'cluster') expect(payBindings(graph).some(binding => binding.kind === name)).toBe(false);
        else expect(() => changePayingDomain(graph, binding.key, [...binding.rows, {
            symbolId: '0',
            counts: [2],
            values: ['6']
        }], 'keep')).toThrow('additional linePays reader');
    });
    test(`PR19: a dynamically written ${name} lookup cannot be admitted as immutable`, () => {
        const graph = create() as ModelGraph;
        graph.nodes!.push({id: 'table-writer', nodeType: 'draw', boardStateKey: table});
        expect(payBindings(graph).some(binding => binding.kind === name)).toBe(false);
    });
}

test('PR19: opaque effects remain explicit and block an unsafe table transformation locally', () => {
    const graph = clusterStudioProof() as ModelGraph;
    graph.nodes!.push({id: 'opaque', nodeType: 'custom-op'});
    const index = indexGraphSources(graph);
    expect(index.unresolvedEffects).toMatchObject([{scope: null, reads: true, writes: true}]);
    expect(payBindings(graph).some(binding => binding.kind === 'cluster')).toBe(false);
    expect(graph.nodes!.at(-1)).toEqual({id: 'opaque', nodeType: 'custom-op'});
});

for (const raw of ['', '1.00000000000000001', '9007199254740990.1', '9007199254740990.9']) {
    for (const input of ['weightInput', 'payloadInput'] as const) test(`PR19: ${input} retains and rejects the original token ${JSON.stringify(raw)}`, () => {
        const machine = weightedMachine(), action = machine.states[0].actions.find(action => action.kind === 'draw')!;
        if (action.kind !== 'draw') throw new Error('Missing independent Draw fixture');
        const before = action.outcomes[0][input === 'weightInput' ? 'weight' : 'payload'];
        action.outcomes[0][input] = raw;
        expect(isMachineInput(machine)).toBe(true);
        expect(validateMachine(machine).length).toBeGreaterThan(0);
        expect(() => lowerMachine(machine)).toThrow();
        expect(action.outcomes[0][input]).toBe(raw);
        expect(action.outcomes[0][input === 'weightInput' ? 'weight' : 'payload']).toBe(before);
    });
    test(`PR19: count and numeric initial values reject the original token ${JSON.stringify(raw)}`, () => {
        const machine = sequenceMachine();
        machine.stepBudgetInput = raw;
        expect(isMachineInput(machine)).toBe(true);
        expect(validateMachine(machine).length).toBeGreaterThan(0);
        expect(() => machineInitialInput(raw, 'private')).toThrow();
        expect(() => machineInitialInput(raw, 'shared')).toThrow();
    });
}

test('PR19: intentional zero and negative payload survive, raw input never becomes graph metadata, and totals share Studio limits', () => {
    const machine = weightedMachine(), action = machine.states[0].actions.find(action => action.kind === 'draw')!;
    if (action.kind !== 'draw') throw new Error('Missing independent Draw fixture');
    action.outcomes[0].weightInput = '0';
    action.outcomes[0].payloadInput = '-7';
    const resolved = resolveMachineInput(machine),
        next = resolved.states[0].actions.find(action => action.kind === 'draw')!;
    if (next.kind !== 'draw') throw new Error('Missing resolved Draw');
    expect(next.outcomes[0]).toEqual({id: action.outcomes[0].id, weight: 0, payload: -7});
    expect(JSON.stringify(lowerMachine(machine))).not.toContain('weightInput');
    expect(JSON.stringify(lowerMachine(machine))).not.toContain('payloadInput');
    action.outcomes[0].weightInput = String(Number.MAX_SAFE_INTEGER);
    expect(validateMachine(machine).join(' ')).toContain('total weight exceeds');
    expect(machineInitialInput('0.5', 'shared')).toBe(0.5);
    expect(machineInitialInput('1e3', 'private')).toBe(1000);
});

test('PR19: parameter binding closes all node reference fields and inline default/exit ASTs while retaining unrelated literals', () => {
    const machine = sequenceMachine();
    machine.states = [machine.states[2]];
    machine.entry = 'end';
    machine.variables.push({key: 'counter', scope: 'private', initial: 7});
    machine.states[0].actions = [{
        id: 'pay',
        label: 'Pay',
        kind: 'library',
        definition: 'parameters',
        parameters: {keep: 'literal amount'},
        parameterBindings: {amount: 'counter'}
    }];
    const param: Expr = {exprType: 'fieldAccess', path: ['expressions', 'amount']};
    const original: ModelDefinition = {
        name: 'parameters',
        nodes: [
            {
                id: 'pay',
                nodeType: 'modifyState',
                expressionId: 'amount',
                outputKey: 'coins',
                inputs: {in: {name: 'in', type: 'Number', defaultValue: param}}
            },
            {id: 'branch', nodeType: 'branch', conditionId: 'amount'},
            {id: 'loop', nodeType: 'loop', stopConditionId: 'amount', exitReason: param},
            {id: 'weighted', nodeType: 'draw', weightExpressionId: 'amount'}
        ],
        edges: [],
        expressions: {via: param, transitive: {exprType: 'fieldAccess', target: 'state', path: ['expressions', 'via']}}
    };
    const before = structuredClone(original), lowered = lowerMachine(machine, {parameters: original});
    const operation = Object.values(lowered.definitions).find(value => Object.hasOwn(value, '__machineParameterSource'))!;
    for (const [i, field] of ['expressionId', 'conditionId', 'stopConditionId', 'weightExpressionId'].entries()) {
        expect(Object.hasOwn(operation.expressions as object, String(operation.nodes[i][field]))).toBe(true);
    }
    expect((operation.nodes[2].exitReason as Expr).path).toEqual([machineNamespaceForCounter(machine.instance)]);
    const defaultValue = (operation.nodes[0].inputs as Record<string, { defaultValue: Expr }>).in.defaultValue;
    expect(defaultValue.path).toEqual([machineNamespaceForCounter(machine.instance)]);
    expect((operation.expressions as Record<string, Expr>).via.path).toEqual([machineNamespaceForCounter(machine.instance)]);
    expect(original).toEqual(before);
});

function machineNamespaceForCounter(instance: string) {
    const hex = (text: string) => [...new TextEncoder().encode(text)].map(byte => byte.toString(16).padStart(2, '0')).join('');
    return `__machine_${hex(instance)}_private_${hex('counter')}`;
}

test('PR19: opaque expression/node metadata is retained without pretending its example AST executes', () => {
    const expression = {
        exprType: 'binary',
        op: 'Add',
        left: field('counter'),
        right: {exprType: 'constant', kind: 'Integer', value: '1'},
        reviewerExample: field('counter')
    } as Expr;
    const bound = bindMachineExpression(expression, names);
    expect((bound.left as Expr).path).toEqual([privateCounter]);
    expect(bound.reviewerExample).toEqual(field('counter'));
    const graph: ModelGraph = {
        nodes: [{
            id: 'get',
            nodeType: 'getState',
            stateKey: 'actual',
            reviewerExample: field('metadata')
        }], expressions: {value: expression}
    };
    expect(indexGraphSources(graph).stateReads.map(read => read.statePath)).toEqual([['actual'], ['counter']]);
});

test('PR19: unused parameterized templates retain their unresolved scope without blocking an unrelated proven payout', () => {
    const graph = computedLineBoundProof() as ModelGraph;
    graph.mechanics = {
        ...graph.mechanics,
        template: {
            nodes: [{id: 'assign', nodeType: 'modifyState', expressionId: 'amount', outputKey: 'custom'}],
            edges: [],
            expressions: {}
        }
    };
    const index = indexGraphSources(graph);
    expect(index.unresolvedEffects).toMatchObject([{scope: 'template', active: false, reads: true}]);
    expect(payBindings(graph).some(binding => binding.kind === 'line')).toBe(true);
    graph.nodes!.push({id: 'activate-template', nodeType: 'library', mechanicName: 'template', parameters: {}});
    expect(indexGraphSources(graph).unresolvedEffects.some(effect => effect.scope === 'template' && effect.active)).toBe(true);
    expect(payBindings(graph).some(binding => binding.kind === 'line')).toBe(false);
});
