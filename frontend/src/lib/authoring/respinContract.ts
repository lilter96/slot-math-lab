import {Mechanics} from '../../games/shared/graph/flow';
import {holdAndSpin} from '../../games/shared/graph/mechanics/hold-and-spin';
import {columnBoosts} from '../../games/shared/graph/mechanics/column-boost';
import type {ModelGraph, ModelNode} from '../modelProjection';
import {reelObject} from '../reelCommands';
import {canonicalJson} from '../hash';
import {matchReelShape} from './reelShape';
import {indexGraphSources} from './sourceIndex';

interface RespinContract {
    scope: string;
    body: string;
    start: string;
    reset: string;
    loop: string;
    count: number;
    cells: number;
    heldAtStart: number;
    revision: string
}

const expression = (definition: {
    [key: string]: unknown
}, node: ModelNode) => reelObject(reelObject(definition.expressions)?.[String(node.expressionId)]);
const opaqueStateOutput = (node: ModelNode) => !['modifyState', 'getState', 'putState', 'data', 'branch', 'loop', 'library', 'draw'].includes(node.nodeType) && Object.values(reelObject(node.outputs) ?? {}).some(port => reelObject(port)?.type === 'State');

/** The iteration proof needs held cells to stay held, including every after-coins operation. */
function preservesHeldCells(graph: ModelGraph, name: string, cells: number): boolean {
    const closure = new Set<string>(), active = new Set<string>();
    const visit = (name: string): boolean => {
        if (active.has(name)) return false;
        if (closure.has(name)) return true;
        const definition = graph.mechanics?.[name];
        if (!definition) return false;
        active.add(name);
        for (const node of definition.nodes) if (node.nodeType === 'library' && (!node.mechanicName || !visit(node.mechanicName))) return false;
        active.delete(name);
        closure.add(name);
        return true;
    };
    if (!visit(name)) return false;
    if ([...closure].some(scope => graph.mechanics![scope].nodes.some(opaqueStateOutput))) return false;
    const index = indexGraphSources(graph),
        writes = index.stateWrites.filter(write => closure.has(write.source.scope ?? ''));
    if (writes.some(write => ['coinCount', 'respins'].includes(write.key))) return false;
    if (!writes.some(write => write.key === 'coins')) return true;
    const initial = reelObject(graph.initialState) ?? {},
        columns = Array.isArray(initial.boostedColumns) ? initial.boostedColumns.length : 0;
    const names = initial.boostNames, adds = initial.boostAdds, times = initial.boostTimes, ends = initial.boostEnds;
    if (!columns || cells % columns !== 0 || !Array.isArray(names) || !names.length || !Array.isArray(adds) || !Array.isArray(times) || !Array.isArray(ends) || [adds, times, ends].some(values => values.length !== names.length) || typeof initial.coinStake !== 'number' || initial.coinStake <= 0) return false;
    if (adds.some(value => !Number.isSafeInteger(value) || value < 0) || times.some(value => !Number.isSafeInteger(value) || value < 1) || ends.some(value => ![0, 1].includes(value))) return false;
    if (index.stateWrites.some(write => ['boostNames', 'boostAdds', 'boostTimes', 'boostEnds', 'coinStake'].includes(write.key))) return false;
    const definitions = [...closure].map(scope => graph.mechanics![scope]),
        loops = definitions.flatMap(definition => definition.nodes.filter(node => node.nodeType === 'loop')),
        draws = definitions.flatMap(definition => definition.nodes.filter(node => node.nodeType === 'draw' && node.stateWriteKey === 'boostKind'));
    if (loops.length !== 1 || draws.length !== 1 || !Number.isSafeInteger(loops[0].maxIterations) || Number(loops[0].maxIterations) < 2 || !Array.isArray(draws[0].drawWeights)) return false;
    const rows = draws[0].drawWeights as { outcomeId: string; weight: number }[];
    if (rows.length !== names.length || rows.some((row, index) => row.outcomeId !== String(index))) return false;
    const helpers = new Mechanics(), expected = columnBoosts(helpers, {
        columns,
        rows: cells / columns,
        maxPulls: Number(loops[0].maxIterations) - 1,
        kinds: names.map((kind, index) => ({
            kind: String(kind),
            add_bets: adds[index],
            times: times[index],
            ends: ends[index] === 1,
            weight: Math.max(1, rows[index].weight)
        }))
    }), templates = helpers.all(), seen = new Set<string>();
    const verify = (actual: string, template: ModelGraph): boolean => {
        if (seen.has(actual)) return true;
        seen.add(actual);
        const shape = matchReelShape(graph.mechanics![actual], template);
        if (!shape) return false;
        return [...shape.libraries].every(([expected, actual]) => !!templates[expected] && verify(actual, templates[expected] as unknown as ModelGraph));
    };
    return verify(name, expected as unknown as ModelGraph);
}

function provesEntryMinimum(graph: ModelGraph, scope: string, body: string, minimum: number, countExpression: unknown, afterTargets: string[]): boolean {
    if (minimum === 0) return true;
    const index = indexGraphSources(graph), initial = reelObject(graph.initialState) ?? {},
        ignored = new Set([scope, body]);
    if (index.nodes.some(source => opaqueStateOutput(source.node as ModelNode))) return false;
    const collect = (scope: string) => {
        if (ignored.has(scope)) return;
        ignored.add(scope);
        for (const node of graph.mechanics![scope].nodes) if (node.nodeType === 'library' && node.mechanicName) collect(node.mechanicName);
    };
    afterTargets.forEach(collect);
    const owner = (scope: string | null) => scope === null ? graph : graph.mechanics![scope];
    const readonly = (key: string) => !index.stateWrites.some(write => write.key === key);
    const literal = (value: unknown): number | null => {
        const ast = reelObject(value);
        if (ast?.exprType === 'constant' && ast.kind === 'Integer' && Number.isSafeInteger(Number(ast.value))) return Number(ast.value);
        if (ast?.exprType === 'fieldAccess' && ast.target === 'state' && Array.isArray(ast.path) && ast.path.length === 1 && readonly(String(ast.path[0])) && Number.isSafeInteger(initial[String(ast.path[0])])) return Number(initial[String(ast.path[0])]);
        return null;
    };
    const guard = (value: unknown): boolean => {
        const ast = reelObject(value);
        if (ast?.exprType === 'binary' && ast.op === 'And') return guard(ast.left) || guard(ast.right);
        if (ast?.exprType === 'binary' && ast.op === 'Or') return guard(ast.left) && guard(ast.right);
        const left = reelObject(ast?.left), threshold = literal(ast?.right);
        return ast?.exprType === 'compare' && left?.exprType === 'fieldAccess' && left.target === 'state' && canonicalJson(left.path) === canonicalJson(['coinCount']) && threshold !== null && (ast.op === 'Gte' || ast.op === 'Eq' ? threshold >= minimum : ast.op === 'Gt' && threshold + 1 >= minimum);
    };
    const entries = index.libraryUses.filter(use => use.definition === scope);
    if (!entries.length) return false;
    const staticAssignments = new Set<string>(), gated = new Set<string>();
    for (const use of entries) {
        const definition = owner(use.source.scope),
            edges = Array.isArray(definition.edges) ? definition.edges as Record<string, unknown>[] : [],
            incoming = edges.filter(edge => edge.targetNodeId === use.source.id && edge.targetPort === 'state');
        if (incoming.length !== 1) return false;
        const edge = incoming[0], previous = definition.nodes!.find(node => node.id === edge.sourceNodeId);
        if (!previous) return false;
        if (previous.nodeType === 'branch' && edge.sourcePort === 'true' && guard(reelObject(definition.expressions)?.[String(previous.conditionId)])) {
            gated.add(use.source.key);
            continue;
        }
        const ast = expression(definition, previous),
            key = ast?.exprType === 'fieldAccess' && ast.target === 'state' && Array.isArray(ast.path) && ast.path.length === 1 ? String(ast.path[0]) : null,
            values = key && readonly(key) ? initial[key] : null;
        if (previous.nodeType !== 'modifyState' || previous.outputKey !== 'coins' || !Array.isArray(values) || values.some(value => typeof value !== 'number') || values.filter(value => value > 0).length < minimum) return false;
        staticAssignments.add(index.nodes.find(source => source.scope === use.source.scope && source.id === previous.id)!.key);
    }
    if (!gated.size) return true;
    if (!Array.isArray(initial.coins) || initial.coinCount !== initial.coins.filter(value => typeof value === 'number' && value > 0).length) return false;
    for (const write of index.stateWrites.filter(write => write.key === 'coinCount')) if (canonicalJson(expression(owner(write.source.scope), write.source.node as ModelNode)) !== canonicalJson(countExpression)) return false;
    for (const write of index.stateWrites.filter(write => write.key === 'coins' && !ignored.has(write.source.scope ?? '') && !staticAssignments.has(write.source.key))) {
        const definition = owner(write.source.scope),
            edges = Array.isArray(definition.edges) ? definition.edges as Record<string, unknown>[] : [],
            seen = new Set<string>();
        let node: ModelNode | undefined = write.source.node as ModelNode, fresh = false;
        while (node && !seen.has(node.id)) {
            seen.add(node.id);
            const outgoing = edges.filter(edge => edge.sourceNodeId === node!.id && edge.sourcePort === 'state');
            if (outgoing.length !== 1 || outgoing[0].targetPort !== 'state') break;
            node = definition.nodes!.find(node => node.id === outgoing[0].targetNodeId);
            if (!node) break;
            if (node.nodeType === 'modifyState' && node.outputKey === 'coinCount') {
                fresh = canonicalJson(expression(definition, node)) === canonicalJson(countExpression);
                break;
            }
            if (!['modifyState', 'getState', 'draw'].includes(node.nodeType) || node.outputKey === 'coins' || node.stateWriteKey === 'coins' || node.stateWriteKey === 'coinCount') break;
        }
        if (!fresh) return false;
    }
    return true;
}

/** Verify the complete generic Hold & Spin helper. Scope labels/game IDs do not select behavior. */
export function respinContracts(graph: ModelGraph): RespinContract[] {
    const result: RespinContract[] = [], index = indexGraphSources(graph);
    for (const [scope, definition] of Object.entries(graph.mechanics ?? {})) {
        const start = definition.nodes.find(node => node.nodeType === 'modifyState' && node.outputKey === 'respins' && expression(definition, node)?.exprType === 'constant'),
            loop = definition.nodes.find(node => node.nodeType === 'loop');
        if (!start || !loop) continue;
        const count = Number(expression(definition, start)?.value), maximum = Number(loop.maxIterations);
        const edges = Array.isArray(definition.edges) ? definition.edges as Record<string, unknown>[] : [],
            target = edges.find(edge => edge.sourceNodeId === loop.id && edge.sourcePort === 'body'),
            call = definition.nodes.find(node => node.id === target?.targetNodeId);
        const body = typeof call?.mechanicName === 'string' ? graph.mechanics?.[call.mechanicName] : null;
        if (!body || !Number.isSafeInteger(count) || count < 1 || !Number.isSafeInteger(maximum) || (maximum - 1) % count !== 0) continue;
        const landed = body.nodes.filter(node => node.nodeType === 'draw' && /^landed\d+$/.test(String(node.stateWriteKey))),
            cells = landed.length, heldAtStart = cells - (maximum - 1) / count;
        const prizes = body.nodes.filter(node => node.nodeType === 'draw' && /^prize\d+$/.test(String(node.stateWriteKey))),
            reset = body.nodes.find(node => node.nodeType === 'modifyState' && node.outputKey === 'respins');
        if (cells < 1 || prizes.length !== cells || heldAtStart < 0 || heldAtStart > cells || !reset) continue;
        const outcomes = landed[0].drawWeights as { outcomeId: string; weight: number }[];
        if (!Array.isArray(outcomes) || !outcomes.some(row => row.outcomeId === '1') || !outcomes.some(row => row.outcomeId === '0')) continue;
        const prizeRows = prizes[0].drawWeights as { outcomeId: string; weight: number }[];
        if (!Array.isArray(prizeRows)) continue;
        const after = body.nodes.find(node => node.nodeType === 'library'), afterCoins = after?.mechanicName ? {
            mechanic: after.mechanicName,
            label: String(after.label ?? '')
        } : undefined;
        const mechanics = new Mechanics(), template = holdAndSpin(mechanics, {
            cells,
            respins: count,
            heldAtStart,
            hitWeight: outcomes.find(row => row.outcomeId === '1')!.weight,
            missWeight: outcomes.find(row => row.outcomeId === '0')!.weight,
            prizes: prizeRows.map(row => [row.outcomeId, Math.max(1, row.weight)]),
            afterCoins
        });
        const rootShape = matchReelShape(definition, template as unknown as ModelGraph);
        if (!rootShape) continue;
        const expectedBody = Object.values(mechanics.all()).find(definition => definition.nodes.some(node => node.nodeType === 'draw' && node.stateWriteKey === 'landed1'))!;
        // Independent prize/landing weights and authored zero rows stay separate; the helper topology and all consumer ASTs remain exact.
        for (const node of expectedBody.nodes.filter(node => node.nodeType === 'draw')) {
            const actual = body.nodes.find(actual => actual.nodeType === 'draw' && actual.stateWriteKey === node.stateWriteKey);
            if (actual) node.drawWeights = structuredClone(actual.drawWeights);
        }
        const bodyShape = matchReelShape(body, expectedBody as unknown as ModelGraph);
        if (!bodyShape) continue;
        const afterTargets = [...definition.nodes.filter(node => node.nodeType === 'library' && node.id !== call!.id), ...body.nodes.filter(node => node.nodeType === 'library')].map(node => node.mechanicName);
        if (afterTargets.some(name => !name || !preservesHeldCells(graph, name, cells))) continue;
        const counted = definition.nodes.find(node => node.nodeType === 'modifyState' && node.outputKey === 'coinCount');
        if (!counted || !provesEntryMinimum(graph, scope, call!.mechanicName!, heldAtStart, expression(definition, counted), afterTargets as string[])) continue;
        const allowed = new Set([scope, call!.mechanicName]);
        if (index.stateWrites.some(write => write.key === 'respins' && !allowed.has(write.source.scope ?? ''))) continue;
        result.push({
            scope,
            body: call!.mechanicName!,
            start: start.id,
            reset: reset.id,
            loop: loop.id,
            count,
            cells,
            heldAtStart,
            revision: canonicalJson({
                root: definition,
                body,
                initialState: graph.initialState,
                uses: index.libraryUses.filter(use => allowed.has(use.definition)).map(use => use.source.node)
            })
        });
    }
    return result;
}

export function changeRespinCount(graph: ModelGraph, binding: RespinContract, count: number): ModelGraph {
    const current = respinContracts(graph).find(value => value.scope === binding.scope);
    if (!current || current.revision !== binding.revision) throw new Error('The respin counter/loop/consumer contract changed. Reconcile the source.');
    const budget = (binding.cells - binding.heldAtStart) * count + 1;
    if (!Number.isSafeInteger(count) || count < 1 || !Number.isSafeInteger(budget) || budget > 100000) throw new Error('Use a positive whole reset count whose proven Loop budget fits 100000.');
    const next = structuredClone(graph);
    if (count === binding.count) return next;
    const root = next.mechanics![binding.scope], body = next.mechanics![binding.body],
        start = root.nodes.find(node => node.id === binding.start)!,
        reset = body.nodes.find(node => node.id === binding.reset)!;
    const initial = expression(root, start)!;
    initial.value = String(count);
    const restored = expression(body, reset)!;
    const thenExpr = reelObject(restored.thenExpr);
    if (!thenExpr || thenExpr.exprType !== 'constant' || Number(thenExpr.value) !== binding.count) throw new Error('The reset is not the proven conditional counter assignment.');
    thenExpr.value = String(count);
    root.nodes.find(node => node.id === binding.loop)!.maxIterations = budget;
    if (start.label === `Start with ${binding.count} respins`) start.label = `Start with ${count} respins`;
    if (reset.label === `More coins than before restore the ${binding.count} respins; a respin without a new coin uses one up`) reset.label = `More coins than before restore the ${count} respins; a respin without a new coin uses one up`;
    if (!respinContracts(next).some(value => value.scope === binding.scope && value.count === count)) throw new Error('The updated counter/bound could not be reconciled.');
    return next;
}
