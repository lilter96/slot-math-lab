import {expect, test} from '@playwright/test';
import {
    addDictionarySymbol,
    changePayingDomain,
    removeDictionarySymbol,
    symbolReferences
} from '../src/lib/authoring/symbolLifecycle';
import {payBindings} from '../src/lib/authoring/payBindings';
import {graphStudiosCorpus} from './fixtures/graphStudiosCorpus';
import {clusterStudioProof} from './fixtures/clusterStudioProof';
import {computedLineBoundProof} from './fixtures/computedLineBoundProof';
import {sharedDrawingProof} from './fixtures/sourceLifecycleProof';
import type {ModelGraph} from '../src/lib/modelProjection';

test('All five bundled families/entries and separate reference retain payout/source contracts on a stable dictionary extension', () => {
    const cases = graphStudiosCorpus().graphs.filter(({caseId}) => /^(ancient-dragon\/ordinary|woodland-whisper\/|x7-club\/|nine-lives-tgslots\/|le-militare\/|nine-lives\/reference)/.test(caseId));
    expect(cases).toHaveLength(26);
    for (const {graph, caseId} of cases) {
        const before = structuredClone(graph), symbols = graph.symbols as { id: string }[], id = String(symbols.length),
            next = addDictionarySymbol(graph, {id, name: 'New private symbol', kind: 'Standard'});
        expect(graph, caseId).toEqual(before);
        expect((next.symbols as unknown[]).length, caseId).toBe(symbols.length + 1);
        expect(payBindings(next).map(binding => binding.key), caseId).toEqual(payBindings(graph).map(binding => binding.key));
        expect(next.paytables, caseId).toEqual(graph.paytables);
        expect(next.opaque, caseId).toEqual(graph.opaque);
    }
});
test('Cluster eligibility synchronizes runtime lookup, minimum and complete helper closure including weighted counting', () => {
    for (const weighted of [false, true]) {
        const graph = clusterStudioProof(weighted) as ModelGraph, binding = payBindings(graph)[0],
            original = structuredClone(graph);
        const added = changePayingDomain(graph, binding.key, [...binding.rows, {
            symbolId: '2',
            counts: [1],
            values: ['4']
        }], 'keep');
        expect(graph).toEqual(original);
        expect(payBindings(added)[0].rows.map(row => row.symbolId)).toEqual(['1', '2']);
        expect((added.initialState as Record<string, unknown>).clusterMinSize).toBe(1);
        const removed = changePayingDomain(added, binding.key, [{symbolId: '2', counts: [1], values: ['4']}], 'keep');
        expect(payBindings(removed)[0].rows).toEqual([{symbolId: '2', counts: [1], values: ['4']}]);
        expect(removed.opaque).toEqual(graph.opaque);
        expect(() => changePayingDomain(graph, binding.key, [{
            symbolId: '1',
            counts: [5],
            values: ['0']
        }], 'keep')).toThrow('positive');
    }
});
test('Line/scatter sizes and explicit all-wild eligibility preserve exact units and reconcile computed caps', () => {
    const graph = computedLineBoundProof() as ModelGraph,
        line = payBindings(graph).find(binding => binding.kind === 'line')!,
        scatter = payBindings(graph).find(binding => binding.kind === 'scatter')!;
    expect(changePayingDomain(graph, line.key, line.rows, 'recompute')).toEqual(graph);
    const explicit = changePayingDomain(graph, line.key, [...line.rows, {
        symbolId: '0',
        counts: [2],
        values: ['6']
    }], 'recompute');
    expect(payBindings(explicit).find(binding => binding.key === line.key)!.derivedWild).toBe(false);
    expect(explicit.nodes!.find(node => node.id === 'sink')!.winCap).toBe(35);
    const sizes = changePayingDomain(graph, scatter.key, [{symbolId: '2', counts: [1], values: ['3']}], 'recompute');
    expect((sizes.initialState as Record<string, unknown>).scatterPays).toEqual([0, 3, 0]);
    expect(payBindings(sizes).find(binding => binding.key === scatter.key)!.rows[0].values).toEqual(['3']);
});
test('Dictionary removal reports actual and unresolved references, keeps stable numeric IDs and never changes unrelated literals', () => {
    const graph = sharedDrawingProof('cells') as ModelGraph, original = structuredClone(graph);
    expect(symbolReferences(graph, 'A').some(reference => /outcome/.test(reference.label))).toBe(true);
    expect(() => removeDictionarySymbol(graph, 'A')).toThrow('still referenced');
    expect(graph).toEqual(original);
    const added = addDictionarySymbol(graph, {id: 'C', name: 'Unused C', kind: 'Standard'});
    expect(removeDictionarySymbol(added, 'C')).toEqual(graph);
    const line = computedLineBoundProof() as ModelGraph;
    expect(() => addDictionarySymbol(line, {id: '99', name: 'Wrong index', kind: 'Standard'})).toThrow('Append ID');
    const numeric = addDictionarySymbol(line, {id: '3', name: 'Unused last', kind: 'Standard'});
    expect(() => removeDictionarySymbol(numeric, '3')).toThrow('Expression literal');
    const final = addDictionarySymbol(numeric, {id: '4', name: 'Unused final', kind: 'Standard'});
    expect(removeDictionarySymbol(final, '4')).toEqual(numeric);
});
test('An additional custom payout reader prevents domain edits and retains its complete source', () => {
    const graph = computedLineBoundProof() as ModelGraph;
    graph.expressions = {
        ...graph.expressions,
        authoredCustomLookup: {exprType: 'fieldAccess', target: 'state', path: ['linePays']}
    };
    const binding = payBindings(graph).find(binding => binding.kind === 'line')!, original = structuredClone(graph);
    expect(() => changePayingDomain(graph, binding.key, [...binding.rows, {
        symbolId: '0',
        counts: [2],
        values: ['6']
    }], 'recompute')).toThrow('additional linePays reader');
    expect(() => addDictionarySymbol(graph, {
        id: '3',
        name: 'Unused new',
        kind: 'Standard'
    })).toThrow('additional linePays reader');
    expect(graph).toEqual(original);
});
