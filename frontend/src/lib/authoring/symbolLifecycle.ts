import type {ModelGraph} from '../modelProjection';
import {reelObject} from '../reelCommands';
import {canonicalJson} from '../hash';
import {linePayTable} from '../../games/shared/graph/mechanics/paylines';
import {scatterPayTable} from '../../games/shared/graph/mechanics/scatter';
import {type PayBinding, payBindings} from './payBindings';
import {changeBoundClusterPayouts, reconcileClusterTable} from './clusterPayBindings';
import {exactInteger, normalizedCredits} from './exactAmounts';
import {computedLineBound} from './payoutBounds';
import {indexGraphSources} from './sourceIndex';
import {reelBindings} from './reelBindings';
import {cellBindings} from './cellBindings';

export interface DictionarySymbol {
    id: string;
    name: string;
    kind: string
}

export interface SymbolReference {
    label: string;
    path: string;
    potential?: boolean
}

const dictionary = (graph: ModelGraph) => Array.isArray(graph.symbols) ? graph.symbols as DictionarySymbol[] : [];
const denseDictionary = (symbols: DictionarySymbol[]) => symbols.length > 0 && symbols.every((symbol, index) => symbol.id === String(index));
const tables = (graph: ModelGraph) => graph.paytables as {
    id: string;
    entries: { symbolId: string; counts: number[]; payouts: string[] }[]
}[];

function assertLookupReaders(graph: ModelGraph, bindings: PayBinding[]) {
    const index = indexGraphSources(graph);
    if (index.unresolvedEffects.some(effect => effect.active)) throw new Error('Resolve the unknown source dependencies before changing the paying domain. Other graph regions remain available.');
    for (const kind of ['line', 'scatter'] as const) {
        const scopes = new Set(bindings.filter(binding => binding.kind === kind).flatMap(binding => binding.scopes)),
            key = kind === 'line' ? 'linePays' : 'scatterPays';
        if (scopes.size && index.stateReads.some(read => read.statePath[0] === key && (read.path[0] !== 'mechanics' || !scopes.has(String(read.path[1]))))) throw new Error(`An additional ${key} reader has an unresolved domain contract. Resolve that consumer before changing symbols or sizes.`);
    }
}

/** References and unresolved literal uses are blockers, never a request to rewrite arbitrary JSON values. */
export function symbolReferences(graph: ModelGraph, id: string): SymbolReference[] {
    const references: SymbolReference[] = [], index = indexGraphSources(graph), reels = reelBindings(graph);
    const stops = new Set(reels.flatMap(binding => binding.source.stops.map(stop => JSON.stringify([stop.scope, stop.nodeId])))),
        cells = new Set(cellBindings(graph).flatMap(binding => binding.cells.map(cell => cell.source.key)));
    for (const source of index.nodes) if (!stops.has(source.key) && source.node.nodeType === 'draw' && Array.isArray(source.node.drawWeights) && source.node.drawWeights.some(row => reelObject(row)?.outcomeId === id)) references.push({
        label: `${source.scope ?? 'Root'} / ${source.node.label ?? source.id} outcome`,
        path: source.path.join('.'),
        potential: !cells.has(source.key)
    });
    for (const table of Array.isArray(graph.paytables) ? tables(graph) : []) if (table.entries.some(row => row.symbolId === id)) references.push({
        label: `Paytable ${table.id}`,
        path: 'paytables'
    });
    const initial = reelObject(graph.initialState),
        strips = new Set(reels.flatMap(binding => binding.source.stripKeys));
    for (const [key, value] of Object.entries(initial ?? {})) {
        if (['linePays', 'scatterPays', 'clusterPays', 'clusterPaytable'].includes(key)) continue;
        const values = Array.isArray(value) ? value : [value];
        if (values.some(value => value === id || ((key === 'wildSymbol' || strips.has(key)) && String(value) === id))) references.push({
            label: `Initial state ${key}`,
            path: `initialState.${key}`,
            potential: !strips.has(key) && !['board', 'wildSymbol'].includes(key)
        });
    }
    const visit = (value: unknown, path: (string | number)[]) => {
        if (Array.isArray(value)) {
            value.forEach((child, index) => visit(child, [...path, index]));
            return;
        }
        const object = reelObject(value);
        if (!object) return;
        if (object.exprType === 'constant' && String(object.value) === id) references.push({
            label: `Expression literal ${path.join('.')}`,
            path: path.join('.'),
            potential: true
        });
        for (const [key, child] of Object.entries(object)) visit(child, [...path, key]);
    };
    index.expressions.forEach(source => visit(source.value, [...source.path]));
    index.nodes.forEach(source => {
        const parameters = reelObject(source.node.parameters);
        if (parameters && Object.values(parameters).some(value => String(value) === id)) references.push({
            label: `${source.scope ?? 'Root'} / ${source.id} parameter`,
            path: source.path.join('.'),
            potential: true
        });
    });
    if (Array.isArray(graph.plugins) && graph.plugins.length) references.push({
        label: 'Plugin symbol contract is unresolved',
        path: 'plugins',
        potential: true
    });
    return references;
}

function reconcileDictionary(graph: ModelGraph, next: ModelGraph) {
    const bindings = payBindings(graph), initial = reelObject(next.initialState);
    assertLookupReaders(graph, bindings);
    if (initial && Array.isArray(initial.linePays) && !bindings.some(binding => binding.kind === 'line')) throw new Error('The line lookup has an unresolved dictionary contract. Retain its symbols.');
    if (initial && Array.isArray(initial.clusterPays) && !bindings.some(binding => binding.kind === 'cluster')) throw new Error('The cluster lookup has an unresolved dictionary contract. Retain its symbols.');
    for (const binding of bindings) {
        if (binding.kind === 'line') {
            const table = tables(next).find(table => table.id === binding.tableId)!;
            initial!.linePays = linePayTable({
                symbolCount: dictionary(next).length,
                wild: binding.wild!,
                reels: binding.columns,
                pays: Object.fromEntries(table.entries.map(row => [row.symbolId, Object.fromEntries(row.counts.map((count, index) => [count, exactInteger(row.payouts[index], binding.roundCost)]))]))
            });
        } else if (binding.cluster?.encoding === 'shared-column-major') reconcileClusterTable(graph, next, binding);
    }
    if (bindings.some(binding => !payBindings(next).some(next => next.key === binding.key))) throw new Error('A payout dependency cannot retain its dictionary contract. The source stays intact.');
}

export function addDictionarySymbol(graph: ModelGraph, symbol: DictionarySymbol): ModelGraph {
    const symbols = dictionary(graph);
    if (!symbol.id || !symbol.name.trim() || symbols.some(value => value.id === symbol.id) || !['Standard', 'Wild', 'Scatter', 'Special'].includes(symbol.kind)) throw new Error('Provide a unique symbol ID, display name and supported kind.');
    if (denseDictionary(symbols) && symbol.id !== String(symbols.length)) throw new Error(`This dictionary uses numeric lookup indices. Append ID ${symbols.length}; existing IDs cannot move.`);
    if (symbols.some(value => value.kind === 'Wild') && symbol.kind === 'Wild') throw new Error('Adding another wild needs its own substitution contract. Add an ordinary symbol instead.');
    const next = structuredClone(graph);
    next.symbols = [...symbols, symbol];
    reconcileDictionary(graph, next);
    return next;
}

export function removeDictionarySymbol(graph: ModelGraph, id: string): ModelGraph {
    const symbols = dictionary(graph);
    if (symbols.filter(symbol => symbol.id === id).length !== 1 || symbols.length <= 1) throw new Error('Keep at least one unambiguous dictionary symbol.');
    const references = symbolReferences(graph, id);
    if (references.length) throw new Error(`Symbol ${id} is still referenced: ${references.slice(0, 3).map(reference => reference.label).join('; ')}. Resolve those uses first.`);
    if (denseDictionary(symbols) && symbols.at(-1)!.id !== id) throw new Error('Removing this numeric index would move other symbol IDs. Only an unreferenced final index can be removed.');
    const next = structuredClone(graph);
    next.symbols = symbols.filter(symbol => symbol.id !== id);
    reconcileDictionary(graph, next);
    return next;
}

/** Complete paying rows, with exact native units. Eligibility and lookup/bound dependencies commit together. */
export function changePayingDomain(graph: ModelGraph, key: string, rows: PayBinding['rows'], capPolicy: 'keep' | 'recompute'): ModelGraph {
    const binding = payBindings(graph).find(binding => binding.key === key);
    if (!binding) throw new Error('The payout consumer contract is unavailable.');
    if (binding.kind === 'prize') throw new Error('Numeric prizes use the separate prize-domain editor.');
    if (canonicalJson(rows) === canonicalJson(binding.rows)) return structuredClone(graph);
    if (binding.kind === 'cluster') return changeBoundClusterPayouts(graph, binding, [], rows);
    assertLookupReaders(graph, payBindings(graph));
    if (!rows.length || new Set(rows.map(row => row.symbolId)).size !== rows.length || rows.some(row => !dictionary(graph).some(symbol => symbol.id === row.symbolId) || !row.counts.length || row.counts.length !== row.values.length || new Set(row.counts).size !== row.counts.length || row.counts.some(count => !Number.isSafeInteger(count) || count < 1 || count > binding.columns))) throw new Error('Keep distinct dictionary symbols with at least one in-bounds paying size.');
    if (binding.kind === 'scatter' && (rows.length !== 1 || rows[0].symbolId !== binding.rows[0].symbolId)) throw new Error('This scatter consumer retains its current symbol. Change its supported paying sizes.');
    const next = structuredClone(graph), table = tables(next).find(table => table.id === binding.tableId)!,
        state = reelObject(next.initialState)!;
    table.entries = rows.map(row => ({
        ...table.entries.find(entry => entry.symbolId === row.symbolId),
        symbolId: row.symbolId,
        counts: [...row.counts],
        payouts: row.values.map(value => {
            const credits = exactInteger(value) * binding.scale;
            if (!Number.isSafeInteger(credits)) throw new Error('The scaled payout exceeds the exact supported range.');
            return normalizedCredits(credits, binding.roundCost);
        })
    }));
    if (binding.kind === 'line') state.linePays = linePayTable({
        symbolCount: dictionary(next).length,
        wild: binding.wild!,
        reels: binding.columns,
        pays: Object.fromEntries(rows.map(row => [row.symbolId, Object.fromEntries(row.counts.map((count, index) => [count, exactInteger(row.values[index])]))]))
    });
    else state.scatterPays = scatterPayTable(Object.fromEntries(rows[0].counts.map((count, index) => [count, exactInteger(rows[0].values[index])])), binding.columns);
    if (capPolicy === 'recompute') {
        const previous = computedLineBound(graph), updated = computedLineBound(next, false);
        if (!previous || !updated) throw new Error('The computed bound cannot be reconciled. Choose the explicit game-cap policy.');
        next.nodes = next.nodes!.map(node => node.id === previous.sinkId ? {...node, winCap: updated.cap} : node);
    } else if (!binding.cap && !computedLineBound(graph)) throw new Error('This source has no mapped payout-bound or cap contract. Resolve it before changing eligibility.');
    if (!payBindings(next).some(next => next.key === binding.key)) throw new Error('Eligibility or precision no longer matches the runtime consumer. The graph stays intact.');
    return next;
}
