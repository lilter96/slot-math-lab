import { test, expect } from '@playwright/test';
import { parseTableText, tableText, spreadsheetNumber, checkSpreadsheetZip } from '../src/lib/authoring/spreadsheetIO';
import { autoColumns, exportTable, previewTable, type TableTarget, type ImportOptions } from '../src/lib/authoring/studioTabular';
import { weightBindings } from '../src/lib/authoring/weightBindings';
import { reelBindings } from '../src/lib/authoring/reelBindings';
import { payBindings } from '../src/lib/authoring/payBindings';
import { lineBindings } from '../src/lib/authoring/lineBindings';
import { applyStudioPatches } from '../src/lib/authoring/studioEdits';
import { clusterStudioProof } from './fixtures/clusterStudioProof';
import { prizeStudioProof } from './fixtures/prizeStudioProof';
import { createAncientDragonGraph } from '../src/games/ancient-dragon/graph';
import { graphStudiosCorpus } from './fixtures/graphStudiosCorpus';
import { computedLineBoundProof } from './fixtures/computedLineBoundProof';
import type { ModelGraph } from '../src/lib/modelProjection';

function preview(graph: ModelGraph, target: TableTarget, rows: string[][], override: Partial<ImportOptions> = {}) {
  const sheet = parseTableText(tableText(rows, 'csv'), 'csv').sheets[0];
  return previewTable(graph, target, sheet, { headerRow: 0, mapping: autoColumns(sheet, target), symbols: 'id', unit: 'native', addedWeight: '1', discardStops: false, ...override });
}
test('CSV/TSV retain literal IDs, delimiters, multiline fields, fractions and exact XML number tokens', () => {
  const rows = [['sourceKey', 'outcomeId', 'weight'], ['["my,scope","draw"]', '001', '3'], ['line\nname', '=literal', '1']];
  for (const format of ['csv', 'tsv'] as const) expect(parseTableText(tableText(rows, format), format).sheets[0].rows.map(row => row.map(cell => cell.text))).toEqual(rows);
  expect(spreadsheetNumber('1.000000000000000001')).toBe('1.000000000000000001'); expect(spreadsheetNumber('9.007199254740993e15')).toBe('9007199254740993'); expect(spreadsheetNumber('1E-18')).toBe('0.000000000000000001');
  expect(() => checkSpreadsheetZip(new TextEncoder().encode('symbolId,count,value\n1,2,3'))).toThrow('genuine');
});
test('Every admitted static source/table/line in the 65-graph corpus exports and reimports as a source-preserving no-op', () => {
  for (const { caseId, graph } of graphStudiosCorpus().graphs) {
    const targets: TableTarget[] = [...reelBindings(graph).map(binding => ({ kind: 'reels' as const, label: caseId, binding })), ...payBindings(graph).map(binding => ({ kind: 'paytable' as const, label: caseId, binding })), ...lineBindings(graph).map(binding => ({ kind: 'lines' as const, label: caseId, binding }))];
    const weights = weightBindings(graph).filter(binding => binding.capability === 'editable'); if (weights.length) targets.push({ kind: 'weights', label: caseId, bindings: weights });
    for (const target of targets) { const result = preview(graph, target, exportTable(graph, target)); expect(result.errors, `${caseId}/${target.kind}`).toEqual([]); expect(applyStudioPatches(graph, result.patches), `${caseId}/${target.kind}`).toEqual(graph); }
  }
});
test('Payout import uses exact units and explicit symbol matching; a new cluster size updates the complete candidate', () => {
  const graph = clusterStudioProof() as ModelGraph, binding = payBindings(graph).find(binding => binding.kind === 'cluster')!, target: TableTarget = { kind: 'paytable', label: 'Groups', binding };
  const result = preview(graph, target, [['name', 'group', 'credits'], ['A', '4', '6']], { mapping: { symbolId: 0, count: 1, value: 2 }, symbols: 'name', unit: 'credits' });
  expect(result.errors).toEqual([]); const next = applyStudioPatches(graph, result.patches); expect((next.initialState as Record<string, unknown>).clusterMinSize).toBe(4);
  expect(preview(graph, target, [['symbolId', 'count', 'value'], ['1', '4', '6.000000000000000001']]).errors).not.toEqual([]);
});
test('Independent numeric prize weights and shared prize values import together without losing payloads or collapsing draws', () => {
  const graph = prizeStudioProof() as ModelGraph, binding = payBindings(graph).find(binding => binding.kind === 'prize')!, target: TableTarget = { kind: 'paytable', label: 'Prizes', binding }, rows = exportTable(graph, target);
  rows[1][2] = '3'; rows[1][6] = '1'; const result = preview(graph, target, rows); expect(result.errors).toEqual([]);
  const next = applyStudioPatches(graph, result.patches), node = next.mechanics![binding.prize!.sources[0].scope!].nodes[0]; expect(node.drawWeights).toEqual([{ outcomeId: '3', weight: 1, value: 0 }, { outcomeId: '5', weight: 1, value: 0 }]);
});
test('Reel resizing and ID-based reorder retain weighted stop identity; missing removal policy rejects atomically', () => {
  const graph = createAncientDragonGraph() as ModelGraph, binding = reelBindings(graph)[0], target: TableTarget = { kind: 'reels', label: 'Reels', binding }, rows = exportTable(graph, target), before = structuredClone(graph);
  const first = rows[1], second = rows[2]; [first[2], second[2]] = [second[2], first[2]]; [first[3], second[3]] = [second[3], first[3]];
  const reordered = preview(graph, target, rows); expect(reordered.errors).toEqual([]); expect(graph).toEqual(before);
  const removed = rows.filter((row, index) => index === 0 || row[0] !== '1' || row[1] !== String(binding.stops[0].length - 1));
  expect(preview(graph, target, removed).errors.map(error => error.message).join(' ')).toContain('removed stop weights'); expect(preview(graph, target, removed, { discardStops: true }).errors).toEqual([]);
});
test('Formula/malformed/duplicate/unsafe rows provide locations and never yield a partly applicable edit', () => {
  const graph = prizeStudioProof() as ModelGraph, source = weightBindings(graph)[0], target: TableTarget = { kind: 'weights', label: 'Distribution', bindings: [source] };
  for (const rows of [[['outcomeId', 'weight'], ['1', '9007199254740993']], [['outcomeId', 'weight'], ['1', '1'], ['1', '2']], [['outcomeId', 'weight'], ['unknown', '1']]]) { const result = preview(graph, target, rows); expect(result.errors.length).toBeGreaterThan(0); expect(result.patches).toEqual([]); expect(result.errors[0].row).toBeGreaterThan(0); }
  const sheet = parseTableText('outcomeId,weight\n1,2', 'csv').sheets[0]; sheet.rows[1][1] = { text: '', address: 'B2', error: 'Formula cell rejected' };
  const result = previewTable(graph, target, sheet, { headerRow: 0, mapping: { outcomeId: 0, weight: 1 }, symbols: 'id', unit: 'native', addedWeight: '1', discardStops: false }); expect(result.errors[0].message).toContain('B2'); expect(result.patches).toEqual([]);
});
test('Partial imports preview the staged values and retain untouched pending outcomes in the same Draw', () => {
  const graph = prizeStudioProof() as ModelGraph, source = weightBindings(graph)[0], target: TableTarget = { kind: 'weights', label: 'Distribution', bindings: [source] }, sheet = parseTableText('outcomeId,weight\n1,2.0', 'csv').sheets[0];
  const existing = [{ kind: 'weights' as const, key: source.key, values: [['1', '8'], ['5', '4']] as [string, string][] }];
  const result = previewTable(graph, target, sheet, { headerRow: 0, mapping: { outcomeId: 0, weight: 1 }, symbols: 'id', unit: 'native', addedWeight: '1', discardStops: false }, existing);
  expect(result.errors).toEqual([]); expect(result.changes[0].before).toBe('8'); expect(result.patches).toEqual([{ kind: 'weights', key: source.key, values: [['1', '2'], ['5', '4']] }]);
});
test('Partial path imports retain pending line identities and the explicit stake policy', () => {
  const graph = computedLineBoundProof() as ModelGraph, binding = lineBindings(graph)[0], target: TableTarget = { kind: 'lines', label: 'Paths', binding };
  const existing = [{ kind: 'lines' as const, key: binding.key, paths: [[0, 0], [0, 0]], identities: ['0', null], stakePolicy: { mode: 'fixed-total' as const, confirmFixed: true, cap: 'recompute' as const } }];
  const sheet = parseTableText('line,column,row\n2,1,0', 'csv').sheets[0];
  const result = previewTable(graph, target, sheet, { headerRow: 0, mapping: autoColumns(sheet, target), symbols: 'id', unit: 'native', addedWeight: '1', discardStops: false }, existing);
  expect(result.errors).toEqual([]); expect(result.patches).toEqual(existing);
  expect(lineBindings(applyStudioPatches(graph, result.patches))[0].lines).toHaveLength(2);
});
