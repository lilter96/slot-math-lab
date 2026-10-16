import {add, div, mul, state} from '../../games/shared/graph/expr';
import {canonicalJson} from '../hash';
import type {ModelGraph, ModelNode} from '../modelProjection';
import {reelObject} from '../reelCommands';
import {indexGraphSources} from './sourceIndex';
import {lineBindings} from './lineBindings';
import {scatterPay} from '../../games/shared/graph/mechanics/scatter';
import {matchReelShape} from './reelShape';

interface Counts {
    ordinary: bigint;
    repeated: bigint
}

export interface ComputedLineBound {
    sinkId: string;
    cap: number;
    ordinarySpins: string;
    repeatedSpins: string;
    freeMultiplier: number
}

export interface ActivePayoutCap {
    sinkId: string;
    value: number;
    unit: 'round-price multiples'
}

export function activePayoutCap(graph: ModelGraph): ActivePayoutCap | null {
    const sinks = (graph.nodes ?? []).filter(node => node.nodeType === 'metricsSink');
    if (sinks.length !== 1 || typeof sinks[0].winStateKey !== 'string' || !Number.isSafeInteger(sinks[0].winCap) || Number(sinks[0].winCap) < 0) return null;
    const writers = (graph.nodes ?? []).filter(node => node.nodeType === 'modifyState' && node.outputKey === sinks[0].winStateKey);
    const ast = writers.length === 1 && reelObject(reelObject(graph.expressions)?.[String(writers[0].expressionId)]);
    if (!ast || ast.exprType !== 'binary' || ast.op !== 'Div' || canonicalJson(ast.right) !== canonicalJson(state('roundCost'))) return null;
    return {sinkId: sinks[0].id, value: Number(sinks[0].winCap), unit: 'round-price multiples'};
}

/** A guarded authoring bound for the existing uncapped line-spin contract; never a payout evaluator. */
export function computedLineBound(graph: ModelGraph, verifyCurrent = true): ComputedLineBound | null {
    const initial = reelObject(graph.initialState), index = indexGraphSources(graph), lines = lineBindings(graph);
    const bet = Number(initial?.totalBet), cost = Number(initial?.roundCost), payRows = initial?.linePays,
        scatters = initial?.scatterPays;
    if (!initial || initial.roundCoins !== 0 || !Number.isSafeInteger(bet) || bet <= 0 || !Number.isSafeInteger(cost) || cost <= 0 || lines.length !== 1 || !Array.isArray(payRows) || !Array.isArray(scatters) || index.issues.length || (Array.isArray(graph.plugins) && graph.plugins.length)) return null;
    if (payRows.some(row => !Array.isArray(row) || row.length !== lines[0].columns + 1) || index.nodes.some(source => !['modifyState', 'getState', 'putState', 'draw', 'branch', 'loop', 'library', 'metricsSink'].includes(source.node.nodeType))) return null;
    const lineValues = payRows.flat();
    if (![...lineValues, ...scatters].every(value => Number.isSafeInteger(value) && value >= 0)) return null;
    const scopes = [graph, ...Object.values(graph.mechanics ?? {})];
    const sinks = (graph.nodes ?? []).filter(node => node.nodeType === 'metricsSink');
    if (sinks.length !== 1 || sinks[0].winStateKey !== 'winCredits') return null;
    const expression = (scope: ModelGraph, node: ModelNode) => reelObject(scope.expressions)?.[String(node.expressionId)];
    const rootCredits = (graph.nodes ?? []).find(node => node.nodeType === 'modifyState' && node.outputKey === 'winCredits');
    if (!rootCredits || canonicalJson(expression(graph, rootCredits)) !== canonicalJson(div(state('roundCoins'), state('roundCost')))) return null;
    if (index.stateWrites.some(write => ['linePays', 'scatterPays', 'totalBet', 'roundCost'].includes(write.key))) return null;
    const spinWrites = index.stateWrites.filter(write => write.key === 'spinCoins'),
        roundWrites = index.stateWrites.filter(write => write.key === 'roundCoins');
    if (!spinWrites.length || spinWrites.length !== roundWrites.length || [...spinWrites, ...roundWrites].some(write => write.source.node.nodeType !== 'modifyState')) return null;
    const lineScopes = new Set([...lines[0].scopes, ...lines[0].calls.map(call => String(call.node.mechanicName))]);
    const scatterScopes = new Set(Object.entries(graph.mechanics ?? {}).filter(([, definition]) => matchReelShape(definition, scatterPay() as unknown as ModelGraph)).map(([scope]) => scope));
    if (!scatterScopes.size || index.stateWrites.some(write => ['lineCoins', 'linePay', 'lineSymbol', 'lineCount'].includes(write.key) && !lineScopes.has(write.source.scope ?? ''))
        || index.stateWrites.some(write => ['scatterCoins', 'scatterCount'].includes(write.key) && !scatterScopes.has(write.source.scope ?? ''))
        || index.stateWrites.filter(write => write.key === 'winCredits').some(write => write.source.scope !== null || write.source.id !== rootCredits.id)) return null;
    for (const write of spinWrites) {
        const scope = write.source.scope === null ? graph : graph.mechanics![write.source.scope],
            edges = Array.isArray(scope.edges) ? scope.edges as Record<string, unknown>[] : [];
        const nodes = scope.nodes ?? [], outgoing = edges.filter(edge => edge.sourceNodeId === write.source.id),
            round = nodes.find(node => node.id === outgoing[0]?.targetNodeId);
        if (outgoing.length !== 1 || round?.outputKey !== 'roundCoins' || edges.filter(edge => edge.targetNodeId === round.id).length !== 1) return null;
        let before = write.source.id;
        const visited = new Set<string>();
        let scatter: ModelNode | undefined;
        while (!visited.has(before)) {
            visited.add(before);
            const incoming = edges.filter(edge => edge.targetNodeId === before);
            if (incoming.length !== 1) return null;
            const predecessor = nodes.find(node => node.id === incoming[0].sourceNodeId);
            if (!predecessor) return null;
            if (predecessor.nodeType === 'library') {
                scatter = predecessor;
                break;
            }
            if (predecessor.nodeType !== 'modifyState' || ['lineCoins', 'scatterCoins', 'spinCoins', 'roundCoins'].includes(String(predecessor.outputKey))) return null;
            before = predecessor.id;
        }
        if (!scatter || !scatterScopes.has(String(scatter.mechanicName))) return null;
        const incoming = edges.filter(edge => edge.targetNodeId === scatter.id),
            previous = nodes.find(node => node.id === incoming[0]?.sourceNodeId);
        if (incoming.length !== 1 || previous?.nodeType !== 'library' || !lines[0].scopes.includes(String(previous.mechanicName))) return null;
    }
    let multiplied = false;
    for (const scope of scopes) for (const node of scope.nodes ?? []) {
        if (node.nodeType !== 'modifyState') continue;
        if (node.outputKey === 'roundCoins' && canonicalJson(expression(scope, node)) !== canonicalJson(add(state('roundCoins'), state('spinCoins')))) return null;
        if (node.outputKey === 'spinCoins') {
            const win = add(state('lineCoins'), state('scatterCoins')), ast = canonicalJson(expression(scope, node));
            if (ast === canonicalJson(mul(win, state('spinMultiplier')))) multiplied = true;
            else if (ast !== canonicalJson(win)) return null;
        }
    }
    const ordinaryMultiplier = multiplied ? Number(initial.spinMultiplier) : 1;
    if (!Number.isSafeInteger(ordinaryMultiplier) || ordinaryMultiplier < 1) return null;
    let maximumMultiplier = ordinaryMultiplier;
    const multiplierScopes = new Set<string | null>();
    for (const write of index.stateWrites.filter(write => write.key === 'spinMultiplier')) {
        const scope = write.source.scope === null ? graph : graph.mechanics![write.source.scope],
            value = reelObject(expression(scope, write.source.node));
        if (write.source.node.nodeType !== 'modifyState' || value?.exprType !== 'constant' || value.kind !== 'Integer' || !/^\d+$/.test(String(value.value)) || !Number.isSafeInteger(Number(value.value)) || Number(value.value) < 1) return null;
        maximumMultiplier = Math.max(maximumMultiplier, Number(value.value));
        multiplierScopes.add(write.source.scope);
        // The multiplier change must lead straight into its bounded repetition, rather than changing the paid entry.
        const edges = Array.isArray(scope.edges) ? scope.edges as Record<string, unknown>[] : [],
            following = edges.filter(edge => edge.sourceNodeId === write.source.id);
        if (following.length !== 1 || !(scope.nodes ?? []).some(node => node.id === following[0].targetNodeId && node.nodeType === 'loop')) return null;
    }

    function count(scope: ModelGraph, inside: boolean, ancestry: Set<string>, selected?: Set<string>): Counts | null {
        const nodes = (scope.nodes ?? []).filter(node => !selected || selected.has(node.id)),
            edges = Array.isArray(scope.edges) ? scope.edges as Record<string, unknown>[] : [];
        const bodies = new Map<string, Set<string>>();
        for (const node of nodes) {
            const outgoing = edges.filter(edge => edge.sourceNodeId === node.id);
            if (!['branch', 'loop'].includes(node.nodeType) && outgoing.length > 1) return null;
            if (['branch', 'loop'].includes(node.nodeType) && new Set(outgoing.map(edge => edge.sourcePort)).size !== outgoing.length) return null;
            if (node.nodeType !== 'loop') continue;
            const body = outgoing.find(edge => edge.sourcePort === 'body');
            if (!body || !Number.isSafeInteger(node.maxIterations) || Number(node.maxIterations) < 1) return null;
            const found = new Set<string>(), todo = [String(body.targetNodeId)];
            while (todo.length) {
                const id = todo.pop()!;
                if (id === node.id) return null;
                if (found.has(id)) continue;
                found.add(id);
                todo.push(...edges.filter(edge => edge.sourceNodeId === id).map(edge => String(edge.targetNodeId)));
            }
            bodies.set(node.id, found);
        }
        const result: Counts = {ordinary: 0n, repeated: 0n},
            bodyNodes = new Set([...bodies.values()].flatMap(body => [...body]));
        for (const node of nodes.filter(node => !bodyNodes.has(node.id))) {
            if (node.nodeType === 'modifyState' && node.outputKey === 'spinCoins') {
                if (inside) result.repeated++; else result.ordinary++;
            }
            if (node.nodeType === 'library') {
                const key = String(node.mechanicName), definition = graph.mechanics?.[key];
                if (!definition || ancestry.has(key)) return null;
                const nested = count(definition, inside, new Set([...ancestry, key]));
                if (!nested) return null;
                result.ordinary += nested.ordinary;
                result.repeated += nested.repeated;
            }
            if (node.nodeType === 'loop') {
                const nested = count(scope, true, ancestry, bodies.get(node.id));
                if (!nested) return null;
                result.repeated += (nested.ordinary + nested.repeated) * BigInt(Number(node.maxIterations));
            }
        }
        return result;
    }

    const incoming = new Set((Array.isArray(graph.edges) ? graph.edges as Record<string, unknown>[] : []).map(edge => edge.targetNodeId));
    const entries = (graph.nodes ?? []).filter(node => !incoming.has(node.id));
    if (entries.length !== 1 || entries[0].nodeType !== 'library') return null;
    const entryScope = graph.mechanics?.[String(entries[0].mechanicName)],
        entry = entryScope && count(entryScope, false, new Set([String(entries[0].mechanicName)]));
    const totals = count(graph, false, new Set());
    if (!entry || !totals || entry.ordinary !== 1n || entry.repeated !== 0n || totals.ordinary !== 1n) return null;
    for (const scope of multiplierScopes) {
        const totals = count(scope === null ? graph : graph.mechanics![scope], false, new Set(scope ? [scope] : []));
        if (!totals || totals.ordinary !== 0n || totals.repeated === 0n) return null;
    }
    const maxLine = lineValues.reduce((maximum, value) => Math.max(maximum, value), 0),
        maxScatter = scatters.reduce((maximum, value) => Math.max(maximum, value), 0);
    const coins = BigInt(maxLine) * BigInt(lines[0].lines.length) + BigInt(maxScatter) * BigInt(bet);
    const numerator = coins * (totals.ordinary * BigInt(ordinaryMultiplier) + totals.repeated * BigInt(maximumMultiplier)),
        cap = (numerator + BigInt(cost) - 1n) / BigInt(cost);
    if (cap > BigInt(Number.MAX_SAFE_INTEGER) || (verifyCurrent && sinks[0].winCap !== Number(cap))) return null;
    return {
        sinkId: sinks[0].id,
        cap: Number(cap),
        ordinarySpins: totals.ordinary.toString(),
        repeatedSpins: totals.repeated.toString(),
        freeMultiplier: maximumMultiplier
    };
}
