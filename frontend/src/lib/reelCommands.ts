import {Mechanics} from '../games/shared/graph/flow';
import {type ReelSet, reelState, spinReels} from '../games/shared/graph/mechanics/reels';
import {canonicalJson} from './hash';
import type {ModelGraph, ModelNode} from './modelProjection';
import {
    applyReelCondition,
    conditionCandidates,
    conditionStateKeys,
    findReelCondition,
    type ReelCondition
} from './reelConditioning';
import {matchReelShape} from './authoring/reelShape';
import {validateWeights} from './modelCommands';

interface StopSource {
    scope: string;
    nodeId: string;
    reel: number
}

export interface ReelSource {
    key: string;
    label: string;
    rows: number;
    stripKeys: string[];
    strips: string[][];
    stops: StopSource[];
    conditions?: ReelCondition[]
}

type Value = Record<string, unknown>;
export const reelObject = (value: unknown): Value | null => value && typeof value === 'object' && !Array.isArray(value) ? value as Value : null;
const object = reelObject;

function fields(expression: unknown): string[] {
    const value = object(expression);
    if (!value) return Array.isArray(expression) ? expression.flatMap(fields) : [];
    if (value.exprType === 'fieldAccess' && value.target === 'state' && Array.isArray(value.path) && value.path.length === 1) return [String(value.path[0])];
    return Object.values(value).flatMap(fields);
}

const nodeKeys = ['id', 'nodeType', 'inputs', 'outputs', 'stateWriteKey', 'boardStateKey', 'stateKey', 'drawWeights', 'weightExpressionId', 'outputKey', 'expressionId', 'mechanicName', 'parameters', 'conditionId', 'stopConditionId', 'maxIterations', 'winStateKey', 'winCap', 'pluginId'];

export function sameReelScope(actual: ModelGraph, expected: ModelGraph): boolean {
    if (actual.initialState && canonicalJson(actual.initialState) !== canonicalJson(expected.initialState ?? {})) return false;
    if (Array.isArray(actual.plugins) && actual.plugins.length && canonicalJson(actual.plugins) !== canonicalJson(expected.plugins ?? [])) return false;
    const project = (nodes: ModelNode[] = []) => nodes.map(node => Object.fromEntries(nodeKeys.filter(key => node[key] !== undefined).map(key => [key, node[key]])));
    return canonicalJson(project(actual.nodes)) === canonicalJson(project(expected.nodes))
        && canonicalJson(actual.edges) === canonicalJson(expected.edges)
        && canonicalJson(actual.expressions) === canonicalJson(expected.expressions);
}

/** Adapt the existing generic authoring shape, never a game ID or kernel dispatch. */
export function reelSources(graph: ModelGraph): ReelSource[] {
    const state = object(graph.initialState), board = object(graph.boardConfig);
    const rows = Number(board?.rows), columns = Number(board?.columns);
    if (!state || !Number.isInteger(rows) || rows < 1 || !Number.isInteger(columns) || columns < 1 || rows * columns > 64) return [];
    const groups = new Map<string, ReelSource>();
    for (const [scope, definition] of Object.entries(graph.mechanics ?? {})) {
        const window = definition.nodes.find(node => node.nodeType === 'modifyState' && node.outputKey === 'board');
        const expression = window && object(definition.expressions)?.[String(window.expressionId)];
        const keys = [...new Set(fields(expression))].filter(key => Array.isArray(state[key]) && /\d+$/.test(key));
        if (keys.length !== columns) continue;
        const prefix = keys[0].replace(/\d+$/, '');
        const stripKeys = Array.from({length: columns}, (_, index) => `${prefix}${index}`);
        if (keys.some(key => !stripKeys.includes(key))) continue;
        const stored = stripKeys.map(key => state[key]);
        if (stored.some(value => !Array.isArray(value) || value.length < rows || value.some(item => typeof item !== 'string'))) continue;
        const strips = (stored as string[][]).map(strip => strip.slice(0, strip.length - rows + 1));
        const spec: ReelSet = {name: scope, stripKey: prefix, strips, rows};
        const wrapped = reelState(spec);
        if (stripKeys.some(key => canonicalJson(wrapped[key]) !== canonicalJson(state[key]))) continue;
        const stops: StopSource[] = [];
        let matched = false;
        for (const shared of [false, true]) {
            const mechanics = shared ? new Mechanics() : undefined;
            const expected = spinReels(spec, mechanics) as unknown as ModelGraph;
            const shape = matchReelShape(definition, expected);
            if (!shape) continue;
            if (mechanics) {
                const definitions = mechanics.all();
                const mapped = Object.entries(definitions).map(([name, stop], reel) => {
                    const actualName = shape.libraries.get(name), actual = actualName && graph.mechanics?.[actualName];
                    const match = actual && matchReelShape(actual, stop as unknown as ModelGraph);
                    return actualName && match ? {
                        scope: actualName,
                        nodeId: match.nodes.get(stop.nodes[0].id)!.id,
                        reel
                    } : null;
                });
                if (mapped.some(stop => !stop)) continue;
                stops.push(...mapped as StopSource[]);
            } else expected.nodes!.filter(node => node.nodeType === 'draw').forEach((node, reel) => stops.push({
                scope,
                nodeId: shape.nodes.get(node.id)!.id,
                reel
            }));
            matched = true;
            break;
        }
        const conditional = conditionCandidates(graph, scope).length > 0;
        if (!matched && !conditional) continue;
        const key = JSON.stringify(stripKeys), existing = groups.get(key);
        if (existing) existing.stops.push(...stops);
        else groups.set(key, {key, label: prefix, rows, stripKeys, strips, stops});
    }
    const conditionalScopes = Object.entries(graph.mechanics ?? {}).filter(([scope]) => conditionCandidates(graph, scope).length > 0);
    for (const source of groups.values()) {
        for (const [scope, definition] of conditionalScopes) {
            const condition = findReelCondition(graph, source, scope);
            if (!condition) continue;
            source.conditions = [...source.conditions ?? [], condition];
            definition.nodes.filter(node => node.nodeType === 'draw' && /^stop\d+$/.test(String(node.stateWriteKey))).forEach(node => source.stops.push({
                scope,
                nodeId: node.id,
                reel: Number(String(node.stateWriteKey).slice(4))
            }));
        }
    }
    for (const [key, source] of groups) for (const condition of source.conditions ?? []) {
        const keys = conditionStateKeys(condition),
            owned = new Set(source.conditions!.map(condition => condition.scope));
        const reference = (value: unknown): boolean => {
            const expression = object(value);
            if (!expression) return Array.isArray(value) && value.some(reference);
            if (expression.exprType === 'fieldAccess' && expression.target === 'state' && Array.isArray(expression.path) && [keys.patterns, keys.pattern].includes(String(expression.path[0]))) return true;
            return Object.values(expression).some(reference);
        };
        if (reference(graph.expressions) || Object.entries(graph.mechanics ?? {}).some(([scope, definition]) => !owned.has(scope) && reference(definition.expressions))) groups.delete(key);
    }
    const allStops = new Set([...groups.values()].flatMap(source => source.stops.map(stop => JSON.stringify([stop.scope, stop.nodeId]))));
    // A conditioned or customized stop source is coupled to the same state; don't rewrite only one domain.
    const unowned = [graph.nodes ?? [], ...Object.entries(graph.mechanics ?? {}).map(([scope, definition]) => definition.nodes.filter(node => !allStops.has(JSON.stringify([scope, node.id]))))]
        .flat().filter(node => node.nodeType === 'draw').map(node => String(node.stateWriteKey));
    return [...groups.values()].filter(source => source.stops.length > 0 && !source.strips.some((_, reel) => unowned.includes(`stop${reel}`)));
}

export function validateReels(graph: ModelGraph, strips: string[][]): string | null {
    const symbols = Array.isArray(graph.symbols) ? graph.symbols as { id: string }[] : [];
    const known = new Set(symbols.map(symbol => String(symbol.id)));
    if (!known.size) return 'No authored symbol dictionary is available for this strip mapping.';
    if (strips.some(strip => !strip.length)) return 'Every reel needs at least one symbol.';
    if (strips.some(strip => strip.length > 4096)) return 'This editor supports at most 4,096 stops per reel.';
    if (strips.some(strip => strip.some(symbol => !known.has(symbol)))) return 'A strip contains an unknown symbol ID. Choose a symbol from this model.';
    return null;
}

export interface ReelStopPolicy {
    identities?: (string | null)[][];
    addedWeight?: string;
    removedStops?: 'discard'
}

export function changeReels(graph: ModelGraph, sourceKey: string, strips: string[][], policy: ReelStopPolicy = {}): ModelGraph {
    const source = reelSources(graph).find(item => item.key === sourceKey);
    if (!source) throw new Error('No proven reel/stop mapping. Inspect the source graph before editing.');
    if (strips.length !== source.strips.length) throw new Error('Keep the authored reel count unchanged.');
    const invalid = validateReels(graph, strips);
    if (invalid) throw new Error(invalid);
    if (canonicalJson(strips) === canonicalJson(source.strips) && (!policy.identities || policy.identities.every(ids => ids.every((id, position) => id === String(position))))) return structuredClone(graph);
    if (source.conditions?.length && source.stops.filter(stop => !source.conditions?.some(condition => condition.scope === stop.scope)).some(stop => (graph.mechanics![stop.scope].nodes.find(node => node.id === stop.nodeId)!.drawWeights as Value[]).some(row => row.weight !== 1))) throw new Error('The retained conditioned fragment describes uniform ordinary stops. Reconcile the weighted conditioning contract before editing.');
    if (policy.identities && (policy.identities.length !== strips.length || policy.identities.some((ids, reel) => ids.length !== strips[reel].length || new Set(ids.filter(id => id !== null)).size !== ids.filter(id => id !== null).length))) throw new Error('Every retained physical stop needs one unique identity within its reel.');
    const next = structuredClone(graph), state = next.initialState as Value;
    for (let reel = 0; reel < strips.length; reel++) {
        const strip = strips[reel];
        state[source.stripKeys[reel]] = Array.from({length: strip.length + source.rows - 1}, (_, index) => strip[index % strip.length]);
    }
    for (const stop of source.stops) {
        if (source.conditions?.some(condition => condition.scope === stop.scope)) continue;
        const node = next.mechanics![stop.scope].nodes.find(node => node.id === stop.nodeId)!;
        const previous = node.drawWeights as Value[];
        const identities = policy.identities?.[stop.reel] ?? strips[stop.reel].map((_, index) => index < source.strips[stop.reel].length ? String(index) : null);
        if (identities.some(id => id !== null && !previous.some(row => row.outcomeId === id))) throw new Error('A retained stop identity is absent from the original distribution.');
        const nonuniform = previous.some(row => row.weight !== 1);
        const removed = previous.some(row => !identities.includes(String(row.outcomeId)));
        if (nonuniform && removed && policy.removedStops !== 'discard') throw new Error('Choose an explicit discard policy for removed weighted stops.');
        if (nonuniform && identities.includes(null) && policy.addedWeight === undefined) throw new Error('Specify the exact weight for new stops; retained stop weights are preserved.');
        const addedWeight = policy.addedWeight ?? '1';
        if (!/^\d+$/.test(addedWeight) || !Number.isSafeInteger(Number(addedWeight))) throw new Error('New stop weight must be an exact non-negative integer.');
        const rows = identities.map((identity, index) => {
            const retained = previous.find(row => row.outcomeId === identity);
            return {...retained, outcomeId: String(index), weight: retained?.weight ?? Number(addedWeight), value: 0};
        });
        const error = validateWeights(rows.map(row => String(row.weight)));
        if (error) throw new Error(error);
        node.drawWeights = rows;
    }
    for (const condition of source.conditions ?? []) applyReelCondition(next, source, condition, strips);
    return next;
}
