import {
    type ReelSet,
    type Showing,
    showingState,
    showingStops,
    spinReelsShowing
} from '../games/shared/graph/mechanics/reels';
import {canonicalJson} from './hash';
import type {ModelGraph, ModelNode} from './modelProjection';
import type {ReelSource} from './reelCommands';
import {matchReelShape} from './authoring/reelShape';

export interface ReelCondition {
    scope: string;
    symbol: string;
    symbolName: string;
    atLeast: number;
    patternsKey?: string;
    patternKey?: string
}

const symbolKey = '__reelEditorSymbol', thresholdKey = '__reelEditorAtLeast';
type RecordValue = Record<string, unknown>;
const record = (value: unknown): RecordValue => value && typeof value === 'object' && !Array.isArray(value) ? value as RecordValue : {};
export const conditionStateKeys = (condition: ReelCondition) => ({
    patterns: condition.patternsKey ?? 'showPatterns',
    pattern: condition.patternKey ?? 'showPattern'
});

/** Discover the authored pattern table/scratch field, then verify the complete helper below. Names are not runtime dispatch. */
export function conditionCandidates(graph: ModelGraph, scope: string): { patternsKey: string; patternKey: string }[] {
    const definition = graph.mechanics?.[scope], first = definition?.nodes[0], initial = record(graph.initialState);
    if (first?.nodeType !== 'draw' || typeof first.stateWriteKey !== 'string') return [];
    const patternKey = first.stateWriteKey;
    const reads = new Set<string>();
    const visit = (value: unknown): void => {
        if (Array.isArray(value)) {
            value.forEach(visit);
            return;
        }
        const ast = record(value);
        if (ast.exprType === 'fieldAccess' && ast.target === 'state' && Array.isArray(ast.path)) reads.add(String(ast.path[0]));
        Object.values(ast).forEach(child => {
            if (child && typeof child === 'object') visit(child);
        });
    };
    visit(definition?.expressions);
    if (!reads.has(first.stateWriteKey)) return [];
    return [...reads].filter(key => Array.isArray(initial[key]) && (initial[key] as unknown[]).length > 0 && (initial[key] as unknown[]).every(row => Array.isArray(row) && row.every(value => Number.isSafeInteger(value) && value >= 0))).map(patternsKey => ({
        patternsKey,
        patternKey
    }));
}

function remapCondition(definition: ModelGraph, condition: ReelCondition): ModelGraph {
    const keys = conditionStateKeys(condition),
        names: Record<string, string> = {showPatterns: keys.patterns, showPattern: keys.pattern};
    const visit = (value: unknown): void => {
        if (Array.isArray(value)) {
            value.forEach(visit);
            return;
        }
        const ast = record(value);
        if (ast.exprType === 'fieldAccess' && ast.target === 'state' && Array.isArray(ast.path) && names[String(ast.path[0])]) ast.path[0] = names[String(ast.path[0])];
        if (typeof ast.stateKey === 'string' && names[ast.stateKey]) ast.stateKey = names[ast.stateKey];
        Object.values(ast).forEach(child => {
            if (child && typeof child === 'object') visit(child);
        });
    };
    visit(definition.expressions);
    definition.nodes = definition.nodes!.map(node => node.stateWriteKey === 'showPattern' ? {
        ...node,
        stateWriteKey: keys.pattern
    } : node);
    return definition;
}

function constraints(condition: ReelCondition) {
    return {
        [symbolKey]: {exprType: 'constant', kind: 'String', value: condition.symbol},
        [thresholdKey]: {exprType: 'constant', kind: 'Integer', value: String(condition.atLeast)}
    };
}

/** Bounded source authoring only; this is not an evaluator or an RNG implementation. */
export function conditionedReels(source: ReelSource, condition: ReelCondition, strips = source.strips) {
    const spec: ReelSet = {
        name: condition.scope,
        stripKey: source.stripKeys[0].replace(/\d+$/, ''),
        strips,
        rows: source.rows
    };
    const showing: Showing = {
        name: condition.scope,
        symbol: condition.symbol,
        symbolName: condition.symbolName,
        atLeast: condition.atLeast
    };
    let combinations = 1;
    for (const strip of strips) {
        const counts = new Set(strip.map((_, stop) => Array.from({length: source.rows}, (_, row) => strip[(stop + row) % strip.length]).filter(symbol => symbol === condition.symbol).length));
        combinations *= counts.size;
        if (combinations > 4096) throw new Error('The conditioned strip edit exceeds 4,096 showing-pattern combinations. Keep a smaller mapped source or inspect its graph.');
    }
    let distribution: ReturnType<typeof showingStops>;
    try {
        distribution = showingStops(spec, showing);
    } catch {
        throw new Error(`No reel stops satisfy at least ${condition.atLeast} ${condition.symbolName} symbols (ID ${condition.symbol}). Restore the required symbols or reset the unapplied strips.`);
    }
    const total = distribution.patterns.reduce((sum, pattern) => sum + pattern.weight, 0);
    if (!Number.isSafeInteger(total) || distribution.patterns.some(pattern => !Number.isSafeInteger(pattern.weight) || pattern.weight < 1)) throw new Error('Conditioned draw weights exceed the editor\'s safe exact integer range. No source changes were applied.');
    const keys = conditionStateKeys(condition), initial = showingState(spec, showing);
    return {
        definition: remapCondition(spinReelsShowing(spec, showing) as unknown as ModelGraph, condition),
        state: {[keys.patterns]: initial.showPatterns, [keys.pattern]: initial.showPattern},
        distribution
    };
}

export function findReelCondition(graph: ModelGraph, source: ReelSource, scope: string): ReelCondition | null {
    const actual = graph.mechanics?.[scope];
    if (!actual) return null;
    const found: ReelCondition[] = [];
    for (const keys of conditionCandidates(graph, scope)) {
        const state = record(graph.initialState), patterns = state[keys.patternsKey];
        if (!Array.isArray(patterns) || !patterns.length || patterns.some(row => !Array.isArray(row) || row.length !== source.strips.length || row.some(count => !Number.isInteger(count) || count < 0 || count > source.rows))) continue;
        const expressions = record(actual.expressions), symbol = record(expressions[symbolKey]),
            threshold = record(expressions[thresholdKey]);
        const declared = expressions[symbolKey] !== undefined || expressions[thresholdKey] !== undefined;
        const symbols = Array.isArray(graph.symbols) ? graph.symbols as { id: string; name: string }[] : [];
        const upper = Math.min(...patterns.map(row => (row as number[]).reduce((sum, count) => sum + count, 0)));
        for (const entry of symbols) for (let atLeast = 0; atLeast <= (declared ? source.rows * source.strips.length : upper); atLeast++) {
            const condition: ReelCondition = {
                scope,
                symbol: String(entry.id),
                symbolName: entry.name,
                atLeast, ...(keys.patternsKey !== 'showPatterns' ? {patternsKey: keys.patternsKey} : {}), ...(keys.patternKey !== 'showPattern' ? {patternKey: keys.patternKey} : {})
            };
            if (declared && (canonicalJson(symbol) !== canonicalJson(constraints(condition)[symbolKey]) || canonicalJson(threshold) !== canonicalJson(constraints(condition)[thresholdKey]))) continue;
            try {
                const expected = conditionedReels(source, condition);
                if (declared) expected.definition.expressions = {...record(expected.definition.expressions), ...constraints(condition)};
                const shape = matchReelShape(actual, expected.definition);
                const exactDraws = shape && expected.definition.nodes!.filter(node => node.nodeType === 'draw').every(node => {
                    const actual = shape.nodes.get(node.id)!.drawWeights as RecordValue[];
                    return (node.drawWeights as RecordValue[]).every((row, index) => row.weight === actual[index].weight);
                });
                if (exactDraws && canonicalJson(patterns) === canonicalJson(expected.state[keys.patternsKey])) found.push(condition);
            } catch { /* No matching/bounded distribution: keep this source read-only. */
            }
        }
    }
    return found.length === 1 ? found[0] : null;
}

export function applyReelCondition(graph: ModelGraph, source: ReelSource, condition: ReelCondition, strips: string[][]): void {
    const generated = conditionedReels(source, condition, strips), actual = graph.mechanics![condition.scope];
    const previous = conditionedReels(source, condition).definition;
    if (record(actual.expressions)[symbolKey] !== undefined) previous.expressions = {...record(previous.expressions), ...constraints(condition)};
    const shape = matchReelShape(actual, previous);
    if (!shape) throw new Error('The conditioned helper changed. Reconcile its source before editing.');
    const takenNodes = new Set(actual.nodes.map(node => node.id)),
        takenExpressions = new Set(Object.keys(record(actual.expressions))),
        takenEdges = new Set((actual.edges as RecordValue[]).map(edge => String(edge.id)));
    const unique = (base: string, names: Set<string>) => {
        let id = base, suffix = 2;
        while (names.has(id)) id = `${base}-${suffix++}`;
        names.add(id);
        return id;
    };
    const ids = new Map([...shape.nodes].map(([id, node]) => [id, node.id])), expressions = new Map<string, string>();
    for (const node of previous.nodes!) for (const field of ['expressionId', 'conditionId', 'weightExpressionId']) if (typeof node[field] === 'string') expressions.set(String(node[field]), String(shape.nodes.get(node.id)![field]));
    for (const id of Object.keys(record(previous.expressions))) if (!expressions.has(id)) expressions.set(id, id);
    for (const node of generated.definition.nodes!) if (!ids.has(node.id)) ids.set(node.id, unique(node.id, takenNodes));
    for (const id of Object.keys(record(generated.definition.expressions))) if (!expressions.has(id)) expressions.set(id, unique(id, takenExpressions));
    const nextIds = new Set(generated.definition.nodes!.map(node => node.id));
    for (const [id, node] of shape.nodes) if (!nextIds.has(id)) {
        const original = previous.nodes!.find(item => item.id === id)!;
        if (Object.keys(node).some(key => !(key in original)) || node.label !== original.label) throw new Error(`Rebuilding conditioned stops would remove authored metadata on ${node.id}. Edit its source graph instead.`);
    }
    const nodes = generated.definition.nodes!.map(node => {
        const retained = shape.nodes.get(node.id), translated: ModelNode = {...node, id: ids.get(node.id)!};
        for (const field of ['expressionId', 'conditionId', 'weightExpressionId']) if (typeof node[field] === 'string') translated[field] = expressions.get(String(node[field]));
        return {...retained, ...translated, label: retained?.label ?? node.label};
    });
    const retainedEdges = actual.edges as RecordValue[],
        edges = (generated.definition.edges as RecordValue[]).map(edge => {
            const endpoints = {
                sourceNodeId: ids.get(String(edge.sourceNodeId)),
                targetNodeId: ids.get(String(edge.targetNodeId)),
                sourcePort: edge.sourcePort,
                targetPort: edge.targetPort
            };
            return retainedEdges.find(old => Object.entries(endpoints).every(([key, value]) => old[key] === value)) ?? {
                ...edge, ...endpoints,
                id: unique(String(edge.id), takenEdges)
            };
        });
    const ast = {...record(actual.expressions)};
    for (const id of Object.keys(record(previous.expressions))) if (!(id in record(generated.definition.expressions))) delete ast[expressions.get(id)!];
    for (const [id, value] of Object.entries({...record(generated.definition.expressions), ...constraints(condition)})) ast[expressions.get(id) ?? id] = value;
    graph.mechanics![condition.scope] = {...actual, nodes, edges, expressions: ast};
    const keys = conditionStateKeys(condition);
    (graph.initialState as RecordValue)[keys.patterns] = generated.state[keys.patterns];
}
