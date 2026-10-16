import {Flow, gameGraph, Mechanics} from '../../src/games/shared/graph/flow';
import {bool, type Expr, num, param, state} from '../../src/games/shared/graph/expr';
import {machineProofGraph} from './machineProofs';
import type {MachineIR} from '../../src/lib/authoring/machineIR';
import type {ModelDefinition, ModelGraph} from '../../src/lib/modelProjection';

const terminal = (instance: string, expression: Expr): MachineIR => ({
    version: 1, instance, name: instance, entry: 'end', stepBudget: 2,
    variables: [{key: 'coins', scope: 'shared', initial: 0}, {key: 'item', scope: 'private', initial: 10},
        {key: 'counter', scope: 'private', initial: 7}, {key: 'acc', scope: 'private', initial: 10}], reads: ['values'],
    states: [{
        id: 'end',
        label: 'Terminal action',
        terminal: true,
        transitions: [],
        actions: [{id: 'pay', label: 'Pay', kind: 'assign', key: 'coins', expression}]
    }]
});
const field = (key: string): Expr => ({exprType: 'fieldAccess', target: 'state', path: [key]});
const add = (left: Expr, right: Expr): Expr => ({exprType: 'binary', op: 'Add', left, right});
const listGraph = (machine: MachineIR, values: unknown[] = [1, 2]) => {
    const graph = machineProofGraph(machine);
    graph.initialState = {...graph.initialState as Record<string, unknown>, values};
    return graph;
};

/** Input programs only. Independent expected results live in the backend and API tests, never in generated goldens. */
export function pr19AuthoringProofs(): { id: string; graph: ModelGraph }[] {
    const proofs: { id: string; graph: ModelGraph }[] = [];
    for (const explicit of [false, true]) {
        const id = explicit ? 'aggregate-explicit' : 'aggregate-default';
        proofs.push({
            id, graph: listGraph(terminal(id, {
                exprType: 'aggregate', func: 'Sum', stateKey: 'values', itemType: 'Number',
                ...(explicit ? {itemName: 'item'} : {}), valueExpr: field('item')
            }))
        });
    }
    proofs.push({
        id: 'aggregate-local-alias', graph: listGraph(terminal('aggregate-local-alias', {
            exprType: 'aggregate', func: 'Sum', stateKey: 'values', itemType: 'Number',
            valueExpr: {exprType: 'fieldAccess', target: 'board', path: ['item']}
        }))
    });
    for (const target of [undefined, null, 'state', 'board']) {
        const id = `alias-${String(target)}`;
        proofs.push({
            id,
            graph: listGraph(terminal(id, {
                exprType: 'fieldAccess',
                path: ['counter'], ...(target === undefined ? {} : {target})
            }))
        });
    }
    proofs.push({
        id: 'fold-initializer', graph: listGraph(terminal('fold-initializer', {
            exprType: 'fold',
            stateKey: 'values',
            itemName: 'item',
            accName: 'acc',
            indexName: 'counter',
            itemType: 'Number',
            init: field('acc'),
            body: add(field('acc'), add(field('item'), field('counter')))
        }))
    });
    for (const filter of [false, true]) {
        const id = filter ? 'filter-locals' : 'map-locals',
            machine = terminal(id, num(0));
        const collection: Expr = filter
            ? {
                exprType: 'filter',
                stateKey: 'values',
                itemName: 'item',
                indexName: 'counter',
                itemType: 'Number',
                predicate: {exprType: 'compare', op: 'Gt', left: field('item'), right: field('counter')}
            }
            : {
                exprType: 'map',
                stateKey: 'values',
                itemName: 'item',
                indexName: 'counter',
                itemType: 'Number',
                body: add(field('item'), field('counter'))
            };
        const action = machine.states[0].actions[0];
        if (action.kind !== 'assign') throw new Error('Expected the scalar fixture assignment.');
        action.expression = {exprType: 'call', function: 'index', args: [collection, num(filter ? 0 : 1)]};
        proofs.push({id, graph: listGraph(machine, filter ? [0, 2] : [1, 2])});
    }
    const nested: Expr = {
        exprType: 'aggregate', func: 'Sum', stateKey: 'values', itemType: 'Array', valueExpr:
            {
                exprType: 'aggregate',
                func: 'Sum',
                stateKey: 'item',
                itemType: 'Number',
                itemName: 'cell',
                valueExpr: field('cell')
            }
    };
    proofs.push({id: 'aggregate-nested', graph: listGraph(terminal('aggregate-nested', nested), [[1, 2], [3]])});

    const operation: ModelDefinition = {
        name: 'direct-parameter', description: 'A normal direct node expression parameter.',
        nodes: [{
            id: 'pay',
            nodeType: 'modifyState',
            expressionId: 'amount',
            outputKey: 'coins',
            inputs: {state: {name: 'state', type: 'State'}},
            outputs: {state: {name: 'state', type: 'State'}}
        }], edges: [], expressions: {}
    };
    const original = (id: string, operation: ModelDefinition, parameters: Record<string, string>) => {
        const mechanics = new Mechanics();
        mechanics.add(operation as unknown as Parameters<Mechanics['add']>[0]);
        return gameGraph({
            id,
            name: id,
            mechanics,
            root: new Flow().use('operation', 'Original literal call', operation.name!, parameters).sink('sink', 'One payout', 'coins', 1000),
            initialState: {coins: 0}
        }) as ModelGraph;
    };
    proofs.push({id: 'parameter-original', graph: original('parameter-original', operation, {amount: '7'})});
    const bound = terminal('parameter-bound', num(0));
    bound.states[0].actions = [{
        id: 'pay',
        label: 'Pay private counter',
        kind: 'library',
        definition: operation.name!,
        parameters: {},
        parameterBindings: {amount: 'counter'}
    }];
    proofs.push({id: 'parameter-bound', graph: machineProofGraph(bound, {[operation.name!]: operation})});

    const mechanics = new Mechanics(),
        body = new Flow().write('never', 'Unvisited body', 'coins', num(0)).mechanic('parameter-body');
    mechanics.add(body);
    const composed = new Flow().either('branch', 'Parameter condition', bool(true), yes => yes.write('yes-pay', 'Direct amount', 'coins', num(7)), no => no.write('no-pay', 'Other amount', 'coins', num(0)))
        .repeat('loop', 'Parameter stop', 3, body.name, {until: bool(true)}).write('transitive', 'Transitive amount', 'coins', param('via')).mechanic('parameter-composed');
    composed.nodes.find(node => node.id === 'branch')!.conditionId = 'flag';
    composed.nodes.find(node => node.id === 'yes-pay')!.expressionId = 'amount';
    const loop = composed.nodes.find(node => node.id === 'loop')!;
    loop.stopConditionId = 'flag';
    loop.exitReason = param('reason');
    const payNode = composed.nodes.find(node => node.id === 'yes-pay')!;
    payNode.inputs = {...payNode.inputs, argument: {name: 'argument', type: 'Number', defaultValue: param('amount')}};
    composed.expressions.via = param('amount');
    const composedDefinition = composed as unknown as ModelDefinition;
    // Add the nested original definition to the outer graph without specializing or changing its literals.
    const literalGraph = original('parameter-composed-original', composedDefinition, {
        amount: '7',
        flag: 'true',
        reason: 'condition'
    });
    literalGraph.mechanics = {...literalGraph.mechanics, [body.name]: body as unknown as ModelDefinition};
    proofs.push({id: 'parameter-composed-original', graph: literalGraph});
    const boundComposed = terminal('parameter-composed-bound', num(0));
    boundComposed.variables.push({key: 'flag', scope: 'private', initial: true}, {
        key: 'reason',
        scope: 'private',
        initial: 'condition'
    });
    boundComposed.states[0].actions = [{
        id: 'operation',
        label: 'Parameter reference closure',
        kind: 'library',
        definition: composed.name,
        parameters: {},
        parameterBindings: {amount: 'counter', flag: 'flag', reason: 'reason'}
    }];
    proofs.push({
        id: 'parameter-composed-bound',
        graph: machineProofGraph(boundComposed, {
            [composed.name]: composedDefinition,
            [body.name]: body as unknown as ModelDefinition
        })
    });
    const dynamic = new Flow().draw('choice', 'One expression-weighted outcome', 'choice', [['a', 1]])
        .write('pay', 'Selected payout', 'coins', {
            exprType: 'if',
            condition: {
                exprType: 'compare',
                op: 'Eq',
                left: state('choice'),
                right: {exprType: 'constant', kind: 'String', value: 'a'}
            },
            thenExpr: param('amount'),
            elseExpr: num(0)
        })
        .mechanic('parameter-weighted');
    // Existing EvaluateAsWeights accepts a numeric scalar, producing one weight, not an array.
    dynamic.nodes[0].weightExpressionId = 'amount';
    const weightedOriginal = original('parameter-weighted-original', dynamic as unknown as ModelDefinition, {amount: '7'});
    weightedOriginal.initialState = {
        ...weightedOriginal.initialState as Record<string, unknown>,
        emptyArray: [],
        choice: ''
    };
    proofs.push({id: 'parameter-weighted-original', graph: weightedOriginal});
    const weightedBound = terminal('parameter-weighted-bound', num(0));
    weightedBound.states[0].actions = [{
        id: 'operation',
        label: 'Bound dynamic weights',
        kind: 'library',
        definition: dynamic.name,
        parameters: {},
        parameterBindings: {amount: 'counter'}
    }];
    const weightedGraph = machineProofGraph(weightedBound, {[dynamic.name]: dynamic as unknown as ModelDefinition});
    weightedGraph.initialState = {...weightedGraph.initialState as Record<string, unknown>, emptyArray: [], choice: ''};
    proofs.push({id: 'parameter-weighted-bound', graph: weightedGraph});
    return proofs;
}
