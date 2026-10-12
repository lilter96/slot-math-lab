import {Mechanics} from '../../games/shared/graph/flow';
import {paylines} from '../../games/shared/graph/mechanics/paylines';
import type {ModelGraph, ModelNode} from '../modelProjection';
import {reelObject} from '../reelCommands';
import {changeBoundLines, type LineBinding, lineBindings} from './lineBindings';
import {matchReelShape} from './reelShape';
import {indexGraphSources} from './sourceIndex';
import {payBindings} from './payBindings';
import {computedLineBound} from './payoutBounds';
import {exactInteger, normalizedCredits} from './exactAmounts';

export interface LineStakePolicy {
    mode: 'per-line' | 'fixed-total';
    confirmFixed?: boolean;
    cap: 'keep' | 'recompute'
}

export interface LineStakePreview {
    oldBet: number;
    newBet: number;
    oldPrice: number;
    newPrice: number;
    oldCount: number;
    newCount: number
}

export function lineStakePreview(graph: ModelGraph, binding: LineBinding, count: number, policy: LineStakePolicy): LineStakePreview {
    const state = reelObject(graph.initialState), oldBet = Number(state?.totalBet), oldPrice = Number(state?.roundCost);
    if (!Number.isSafeInteger(count) || count < 1 || count > 256) throw new Error('Keep 1–256 paylines.');
    if (!Number.isSafeInteger(oldBet) || !Number.isSafeInteger(oldPrice) || oldBet <= 0 || oldPrice <= 0) throw new Error('This count edit requires authored positive totalBet and roundCost sources. Geometry edits keep the current count.');
    if (policy.mode === 'fixed-total' && !policy.confirmFixed) throw new Error('Confirm changing the stake contract to a fixed total; this changes the effective price per line.');
    const betNumerator = BigInt(oldBet) * BigInt(count), betDenominator = BigInt(binding.lines.length);
    if (policy.mode === 'per-line' && betNumerator % betDenominator !== 0n) throw new Error('The preserved per-line price does not produce whole total credits for this count.');
    const newBet = policy.mode === 'per-line' ? Number(betNumerator / betDenominator) : oldBet,
        costNumerator = BigInt(oldPrice) * BigInt(newBet);
    if (!Number.isSafeInteger(newBet) || costNumerator % BigInt(oldBet) !== 0n || costNumerator / BigInt(oldBet) > BigInt(Number.MAX_SAFE_INTEGER)) throw new Error('The entry-price ratio cannot be retained exactly within supported whole credits.');
    return {
        oldBet,
        newBet,
        oldPrice,
        newPrice: Number(costNumerator / BigInt(oldBet)),
        oldCount: binding.lines.length,
        newCount: count
    };
}

/** Retained line identities preserve metadata, node IDs and edge attributes; only the proven sequential helper changes. */
export function changeLineDomain(graph: ModelGraph, binding: LineBinding, paths: number[][], identities: (string | null)[], policy: LineStakePolicy): ModelGraph {
    const current = lineBindings(graph).find(value => value.key === binding.key);
    if (!current || current.revision !== binding.revision) throw new Error('The line set, geometry or shared consumers changed. Reconcile the draft.');
    if (paths.length !== identities.length || new Set(identities.filter(id => id !== null)).size !== identities.filter(id => id !== null).length || identities.some(id => id !== null && (!/^(0|[1-9]\d*)$/.test(id) || Number(id) >= current.lines.length))) throw new Error('Each retained line needs its unique original identity. New lines have no retained ID.');
    if (paths.some(path => path.length !== current.columns || path.some(row => !Number.isSafeInteger(row) || row < 0 || row >= current.rows))) throw new Error('Every path needs one in-bounds row per column.');
    if (paths.length === current.lines.length && identities.every((id, index) => id === String(index))) return changeBoundLines(graph, current, paths);
    const pricing = lineStakePreview(graph, current, paths.length, policy), index = indexGraphSources(graph);
    if (index.stateWrites.some(write => ['totalBet', 'roundCost', 'coinStake'].includes(write.key))) throw new Error('Runtime-written stake/price sources need a separate dependency contract.');
    const bound = computedLineBound(graph);
    if (policy.cap === 'recompute' && !bound) throw new Error('No proven computed upper-bound contract exists. Choose the explicit current game-cap policy.');
    const payouts = payBindings(graph), allTables = Array.isArray(graph.paytables) ? graph.paytables as {
        id: string;
        entries: { symbolId: string; counts: number[]; payouts: string[] }[]
    }[] : [];
    if (pricing.newPrice !== pricing.oldPrice && allTables.some(table => !payouts.some(binding => binding.tableId === table.id))) throw new Error('An additional precision table has an unresolved stake dependency. Keep the current total or resolve that table first.');
    const state = reelObject(graph.initialState)!;
    if (pricing.newBet !== pricing.oldBet && state.coinStake !== undefined && (state.coinStake !== pricing.oldBet || !payouts.some(binding => binding.kind === 'prize'))) throw new Error('The coin stake is not mapped to a verified prize consumer. Preserve it or resolve its contract.');
    const next = structuredClone(graph),
        sets = next.paylineSets as { id: string; paylines: Record<string, unknown>[] }[],
        set = sets.find(set => set.id === current.setId)!;
    set.paylines = paths.map((path, index) => ({
        ...(identities[index] === null ? {} : set.paylines[Number(identities[index])]),
        positions: [...path]
    }));
    const oldTemplate = paylines(new Mechanics(), current.lines, current.rows) as unknown as ModelGraph;
    for (const scope of current.scopes) {
        const definition = next.mechanics![scope], shape = matchReelShape(graph.mechanics![scope], oldTemplate)!;
        const fixed = oldTemplate.nodes!.filter(node => node.nodeType !== 'library').map(node => structuredClone(shape.nodes.get(node.id)!));
        const oldLines = current.lines.map((_, index) => shape.nodes.get(`line-${index + 1}`)!),
            names = new Set(definition.nodes.map(node => node.id));
        const unique = (base: string) => {
            let name = base, suffix = 2;
            while (names.has(name)) name = `${base}-${suffix++}`;
            names.add(name);
            return name;
        };
        const evaluator = oldLines[0].mechanicName!;
        const lines = paths.map((path, index): ModelNode => {
            const original = identities[index] === null ? null : oldLines[Number(identities[index])],
                parameters = {...reelObject(original?.parameters)};
            path.forEach((row, column) => {
                parameters[`p${column}`] = String(column * current.rows + row);
            });
            return original ? {...original, parameters} : {
                nodeType: 'library',
                id: unique(`${current.setId}-new-line`),
                label: `Payline ${index + 1}`,
                mechanicName: evaluator,
                inputs: {state: {name: 'state', type: 'State'}},
                outputs: {state: {name: 'state', type: 'State'}},
                parameters
            };
        });
        definition.nodes = [...fixed, ...lines];
        const edges = definition.edges as Record<string, unknown>[], used = new Set<string>(),
            taken = new Set(edges.map(edge => String(edge.id)));
        definition.edges = definition.nodes.slice(0, -1).map((node, index) => {
            const target = definition.nodes[index + 1].id,
                found = edges.find(edge => edge.sourceNodeId === node.id && edge.sourcePort === 'state' && edge.targetNodeId === target) ?? edges.find(edge => edge.sourceNodeId === node.id && edge.sourcePort === 'state' && !used.has(String(edge.id)));
            if (found) {
                used.add(String(found.id));
                return {...found, targetNodeId: target, targetPort: 'state'};
            }
            let id = `${node.id}-to-${target}`, suffix = 2;
            while (taken.has(id)) id = `${node.id}-to-${target}-${suffix++}`;
            taken.add(id);
            return {id, sourceNodeId: node.id, sourcePort: 'state', targetNodeId: target, targetPort: 'state'};
        });
    }
    const initial = next.initialState as Record<string, unknown>;
    if (pricing.newBet !== pricing.oldBet) initial.totalBet = pricing.newBet;
    if (pricing.newPrice !== pricing.oldPrice) initial.roundCost = pricing.newPrice;
    if (initial.coinStake !== undefined && pricing.newBet !== pricing.oldBet) initial.coinStake = pricing.newBet;
    for (const binding of payouts) {
        const scale = binding.kind === 'scatter' ? pricing.newBet : binding.kind === 'prize' ? Number(initial.coinStake) : binding.scale;
        const table = (next.paytables as typeof allTables).find(table => table.id === binding.tableId)!;
        for (const row of binding.rows) {
            const entry = table.entries.find(entry => entry.symbolId === row.symbolId)!;
            entry.payouts = row.values.map((value, index) => {
                const credits = exactInteger(value) * scale;
                if (!Number.isSafeInteger(credits)) throw new Error('Repriced payouts exceed the exact supported credit range.');
                const oldCredits = exactInteger(entry.payouts[index], pricing.oldPrice);
                return credits === oldCredits && pricing.newPrice === pricing.oldPrice ? entry.payouts[index] : normalizedCredits(credits, pricing.newPrice);
            });
        }
    }
    if (bound && policy.cap === 'recompute') {
        const updated = computedLineBound(next, false);
        if (!updated) throw new Error('The repriced computed payout bound could not be reconciled.');
        next.nodes = next.nodes!.map(node => node.id === bound.sinkId ? {...node, winCap: updated.cap} : node);
    }
    if (!lineBindings(next).some(binding => binding.key === current.key)) throw new Error('The new sequential line helper failed its metadata/consumer contract.');
    return next;
}
