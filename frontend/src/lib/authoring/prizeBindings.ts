import {coinPrizes} from '../../games/shared/graph/mechanics/hold-and-spin';
import {and, choose, eq, mapOver, mul, num, state, str, toNumber, when} from '../../games/shared/graph/expr';
import {canonicalJson} from '../hash';
import type {ModelGraph} from '../modelProjection';
import {reelObject} from '../reelCommands';
import {exactInteger, normalizedCredits} from './exactAmounts';
import {indexGraphSources, type NodeSource} from './sourceIndex';
import {weightBindings} from './weightBindings';
import type {PayBinding, PayoutCellEdit} from './payBindings';

interface PrizeTable {
    id: string;
    entries: { symbolId: string; counts: number[]; payouts: string[] }[]
}

export interface PrizeContract {
    sources: NodeSource[];
    outcomeIds: string[]
}

const same = (a: unknown, b: unknown) => canonicalJson(a) === canonicalJson(b);

/** Recognize the actual numeric prize consumers, independently of graph/game/definition names. */
export function prizeBindings(graph: ModelGraph): PayBinding[] {
    const initial = reelObject(graph.initialState), geometry = reelObject(graph.boardConfig),
        scale = Number(initial?.coinStake), cost = Number(initial?.roundCost),
        cells = Number(geometry?.rows) * Number(geometry?.columns);
    if (!initial || !Number.isSafeInteger(scale) || scale <= 0 || !Number.isSafeInteger(cost) || cost <= 0 || !Number.isSafeInteger(cells) || cells < 1 || cells > 64 || (Array.isArray(graph.plugins) && graph.plugins.length)) return [];
    const index = indexGraphSources(graph);
    if (index.stateWrites.some(write => ['coinStake', 'roundCost'].includes(write.key))) return [];
    const keys = Array.from({length: cells}, (_, cell) => `prize${cell + 1}`),
        landed = Array.from({length: cells}, (_, cell) => `landed${cell + 1}`);
    const sources = index.nodes.filter(source => source.node.nodeType === 'draw' && keys.includes(String(source.node.stateWriteKey)));
    if (!sources.length || index.stateWrites.some(write => keys.includes(write.key) && !sources.some(source => source.key === write.source.key))) return [];
    const weights = weightBindings(graph), scopes = [...new Set(sources.map(source => source.scope))],
        acceptedExpressions = new Set<string>();
    const symbols = Array.isArray(graph.symbols) ? graph.symbols as { id: string; kind: string }[] : [],
        tables = Array.isArray(graph.paytables) ? graph.paytables as PrizeTable[] : [], result: PayBinding[] = [];
    for (const table of tables) {
        if (typeof table.id !== 'string' || tables.filter(value => value.id === table.id).length !== 1 || !Array.isArray(table.entries) || table.entries.length !== 1) continue;
        const row = table.entries[0],
            symbol = symbols.find(symbol => symbol.id === row.symbolId && symbol.kind === 'Money');
        if (!symbol || !Array.isArray(row.counts) || !Array.isArray(row.payouts) || row.counts.length !== row.payouts.length || !row.counts.length || !same(row.counts, row.counts.map((_, i) => i + 1))) continue;
        let prizes: number[];
        try {
            prizes = row.payouts.map(value => {
                const credits = exactInteger(value, cost);
                if (credits % scale !== 0 || credits <= 0) throw new Error('A coin needs a positive whole stake prize.');
                return credits / scale;
            });
        } catch {
            continue;
        }
        if (new Set(prizes).size !== prizes.length || sources.some(source => {
            const binding = weights.find(value => value.key === source.key);
            return !binding || binding.capability !== 'editable' || !same(binding.rows.map(row => row.outcomeId), prizes.map(String)) || binding.rows.some(row => row.value !== 0);
        })) continue;
        const keysAsNumber = mul(toNumber(choose(state('place'), keys.map(state))), state('coinStake'));
        const template = coinPrizes({
            cells,
            symbol: Number(symbol.id),
            prizes: prizes.map(value => [value, 1] as const)
        });
        const paidConsumer = template.expressions.values;
        const heldConsumer = mapOver('coins', when(and(eq(choose(state('place'), landed.map(state)), str(1)), eq(state('item'), num(0))), keysAsNumber, state('item')), {
            index: 'place',
            itemType: 'Number'
        });
        acceptedExpressions.clear();
        let valid = true;
        for (const scope of scopes) {
            const definition = scope === null ? graph : graph.mechanics?.[scope],
                own = sources.filter(source => source.scope === scope);
            if (!definition || own.length !== cells || new Set(own.map(source => source.node.stateWriteKey)).size !== cells) {
                valid = false;
                break;
            }
            const ast = reelObject(definition.expressions),
                consumers = (definition.nodes ?? []).filter(node => node.nodeType === 'modifyState' && node.outputKey === 'coins' && (same(ast?.[String(node.expressionId)], paidConsumer) || same(ast?.[String(node.expressionId)], heldConsumer)));
            if (consumers.length !== 1) {
                valid = false;
                break;
            }
            acceptedExpressions.add(JSON.stringify([scope, String(consumers[0].expressionId)]));
        }
        if (!valid || index.stateReads.some(read => keys.includes(String(read.statePath[0])) && !acceptedExpressions.has(JSON.stringify(read.path[0] === 'mechanics' ? [read.path[1], read.path[3]] : [null, read.path[1]])))) continue;
        const uses = index.libraryUses.filter(use => scopes.includes(use.definition)).map(use => use.source),
            position = tables.indexOf(table);
        result.push({
            version: 1,
            kind: 'prize',
            key: JSON.stringify(['prize', table.id]),
            tableId: table.id,
            revision: canonicalJson({
                table,
                scale,
                cost,
                geometry,
                sources: sources.map(source => [source.key, source.node]),
                scopes: scopes.map(scope => [scope, scope === null ? graph.expressions : graph.mechanics?.[scope]])
            }),
            unit: 'total-bet multiples',
            roundCost: cost,
            scale,
            columns: prizes.length,
            wild: null,
            derivedWild: false,
            rows: [{symbolId: row.symbolId, counts: [...row.counts], values: prizes.map(String)}],
            scopes: scopes.filter((scope): scope is string => scope !== null),
            uses,
            reads: [['initialState', 'coinStake'], ['initialState', 'roundCost'], ['paytables', position], ...sources.map(source => source.path)],
            writes: [['paytables', position, 'entries'], ...sources.map(source => [...source.path, 'drawWeights'])],
            maximum: Math.max(...prizes),
            bound: null,
            cap: null,
            prize: {sources, outcomeIds: prizes.map(String)}
        });
    }
    return result;
}

export function changeBoundPrizes(graph: ModelGraph, binding: PayBinding, edits: PayoutCellEdit[]): ModelGraph {
    const current = prizeBindings(graph).find(value => value.key === binding.key);
    if (!current || current.revision !== binding.revision) throw new Error('The prize source, units or consumer changed. Reconcile this draft.');
    const values = [...current.rows[0].values], seen = new Set<number>();
    for (const edit of edits) {
        const position = current.rows[0].counts.indexOf(edit.count);
        if (position < 0 || edit.symbolId !== current.rows[0].symbolId || edit.remove || seen.has(edit.count)) throw new Error('Keep one edit per existing prize outcome.');
        seen.add(edit.count);
        const value = exactInteger(edit.value);
        if (value <= 0 || !Number.isSafeInteger(value * current.scale)) throw new Error('A coin prize must be a positive whole stake amount in the exact supported range.');
        values[position] = String(value);
    }
    if (new Set(values).size !== values.length) throw new Error('Two numeric prize outcomes would acquire the same identity. Choose distinct values or explicitly merge their distribution.');
    const next = structuredClone(graph);
    if (same(values, current.rows[0].values)) return next;
    const table = (next.paytables as PrizeTable[]).find(table => table.id === current.tableId)!;
    values.forEach((value, position) => {
        if (value !== current.rows[0].values[position]) table.entries[0].payouts[position] = normalizedCredits(Number(value) * current.scale, current.roundCost);
    });
    for (const source of current.prize!.sources) {
        const nodes = source.scope === null ? next.nodes! : next.mechanics![source.scope].nodes,
            node = nodes.find(node => node.id === source.id)!;
        node.drawWeights = (node.drawWeights as Record<string, unknown>[]).map((row, position) => values[position] === row.outcomeId ? row : {
            ...row,
            outcomeId: values[position]
        });
    }
    if (!prizeBindings(next).some(binding => binding.key === current.key)) throw new Error('The prize lookup and precision declaration could not be reconciled. The original graph is unchanged.');
    return next;
}
