import type {ModelGraph} from '../modelProjection';
import {validateWeights} from '../modelCommands';
import {canonicalJson} from '../hash';
import {resolveNodeSource, sourceKey} from './sourceIndex';
import {exactInteger} from './exactAmounts';
import {type WeightBinding, weightBindings} from './weightBindings';
import type {PhysicalStop, ReelBinding} from './reelBindings';
import type {PayBinding} from './payBindings';
import type {LineBinding} from './lineBindings';
import {replaceStudioPatch, studioCandidate, type StudioPatch} from './studioEdits';
import type {InputSheet} from './spreadsheetIO';

export type TableTarget = { kind: 'reels'; label: string; binding: ReelBinding }
    | { kind: 'weights'; label: string; bindings: WeightBinding[] }
    | { kind: 'paytable'; label: string; binding: PayBinding }
    | { kind: 'lines'; label: string; binding: LineBinding };
export type ImportField =
    'sourceKey'
    | 'outcomeId'
    | 'weight'
    | 'reel'
    | 'index'
    | 'symbolId'
    | 'stopId'
    | 'count'
    | 'value'
    | 'unit'
    | 'line'
    | 'column'
    | 'row';
export type ColumnMapping = Partial<Record<ImportField, number>>;

export interface ImportOptions {
    headerRow: number;
    mapping: ColumnMapping;
    symbols: 'id' | 'name';
    unit: 'native' | 'credits' | 'round' | 'bet';
    addedWeight: string;
    discardStops: boolean;
    capPolicy?: 'keep' | 'recompute'
}

export interface ImportPreview {
    patches: StudioPatch[];
    changes: { row: number; item: string; before: string; after: string }[];
    errors: { row: number; field: string; message: string }[]
}

export const nativeUnit = (binding: PayBinding) => binding.kind === 'prize' ? 'prize-stake multiples' : binding.unit;

export function targetFields(target: TableTarget): { field: ImportField; label: string; required: boolean }[] {
    const field = (field: ImportField, label: string, required = true) => ({field, label, required});
    if (target.kind === 'reels') return [field('reel', 'Reel · starts at 1'), field('index', 'Physical index · starts at 0'), field('symbolId', 'Symbol'), field('stopId', 'Retained stop ID', false), field('weight', 'Retained stop weight', false)];
    if (target.kind === 'weights') return [field('outcomeId', 'Outcome ID'), field('weight', 'Weight'), field('sourceKey', 'Draw source', target.bindings.length > 1)];
    if (target.kind === 'lines') return [field('line', 'Line · starts at 1'), field('column', 'Column · starts at 0'), field('row', 'Row · starts at 0')];
    return [field('symbolId', 'Paying symbol'), field('count', target.binding.kind === 'prize' ? 'Prize ordinal' : 'Paying count / size'), field('value', 'Payout / prize value'), field('unit', 'Unit label', false), ...(target.binding.prize ? [field('sourceKey', 'Prize Draw source', false), field('outcomeId', 'Original numeric outcome', false), field('weight', 'Prize weight', false)] : [])];
}

export function autoColumns(sheet: InputSheet, target: TableTarget, headerRow = 0): ColumnMapping {
    const header = sheet.rows[headerRow]?.map(cell => cell.text.trim().toLowerCase().replaceAll(/[\s_-]/g, '')) ?? [];
    const aliases: Partial<Record<ImportField, string[]>> = {
        symbolId: ['symbolid', 'symbol', 'symbolname'],
        value: ['value', 'payout', 'prize', 'amount'],
        count: ['count', 'size', 'ordinal'],
        sourceKey: ['sourcekey', 'source', 'draw']
    };
    return Object.fromEntries(targetFields(target).flatMap(({field}) => {
        const index = header.findIndex(value => [field.toLowerCase(), ...aliases[field] ?? []].includes(value));
        return index < 0 ? [] : [[field, index]];
    }));
}

function stopWeights(graph: ModelGraph, binding: ReelBinding, reel: number) {
    const stop = binding.source.stops.find(stop => stop.reel === reel && !binding.source.conditions?.some(condition => condition.scope === stop.scope));
    if (!stop) return new Map(binding.stops[reel].map(stop => [stop.identity, '1']));
    const rows = resolveNodeSource(graph, sourceKey(stop.scope, stop.nodeId)).node.drawWeights as {
        outcomeId: string;
        weight: number
    }[];
    return new Map(rows.map(row => [row.outcomeId, String(row.weight)]));
}

export function exportTable(graph: ModelGraph, target: TableTarget): string[][] {
    if (target.kind === 'reels') return [['reel', 'index', 'symbolId', 'stopId', 'weight'], ...target.binding.stops.flatMap((strip, reel) => {
        const weights = stopWeights(graph, target.binding, reel);
        return strip.map((stop, index) => [String(reel + 1), String(index), stop.symbol, stop.identity ?? '', weights.get(stop.identity) ?? '1']);
    })];
    if (target.kind === 'weights') {
        const blocked = target.bindings.find(binding => binding.capability !== 'editable');
        if (blocked) throw new Error(blocked.reason ?? 'This expression-backed source requires full graph JSON export.');
        return [['sourceKey', 'outcomeId', 'weight'], ...target.bindings.flatMap(binding => binding.rows.map(row => [binding.key, row.outcomeId, String(row.weight)]))];
    }
    if (target.kind === 'lines') return [['line', 'column', 'row'], ...target.binding.lines.flatMap((path, line) => path.map((row, column) => [String(line + 1), String(column), String(row)]))];
    const binding = target.binding;
    if (binding.prize) {
        const weights = weightBindings(graph).filter(source => binding.prize!.sources.some(value => value.key === source.key)),
            row = binding.rows[0];
        return [['symbolId', 'count', 'value', 'unit', 'sourceKey', 'outcomeId', 'weight'], ...row.counts.flatMap((count, position) => weights.map(source => [row.symbolId, String(count), row.values[position], nativeUnit(binding), source.key, source.rows[position].outcomeId, String(source.rows[position].weight)]))];
    }
    return [['symbolId', 'count', 'value', 'unit'], ...binding.rows.flatMap(row => row.counts.map((count, position) => [row.symbolId, String(count), row.values[position], nativeUnit(binding)]))];
}

function payoutValue(text: string, binding: PayBinding, graph: ModelGraph, unit: ImportOptions['unit']): string {
    const bet = Number((graph.initialState as Record<string, unknown> | undefined)?.totalBet);
    const scale = unit === 'native' ? binding.scale : unit === 'round' ? binding.roundCost : unit === 'bet' ? bet : 1;
    const credits = exactInteger(text, scale);
    if (credits % binding.scale !== 0) throw new Error(`This lookup requires whole ${nativeUnit(binding)}. The imported value does not convert exactly.`);
    return String(credits / binding.scale);
}

export function previewTable(graph: ModelGraph, target: TableTarget, sheet: InputSheet, options: ImportOptions, existing: StudioPatch[] = []): ImportPreview {
    const result: ImportPreview = {patches: [], changes: [], errors: []}, {mapping} = options;
    const fail = (row: number, field: string, message: string) => result.errors.push({row, field, message});
    if (!Number.isSafeInteger(options.headerRow) || options.headerRow < 0 || options.headerRow >= sheet.rows.length) {
        fail(0, 'Header', 'Choose a header row within this worksheet.');
        return result;
    }
    for (const {
        field,
        label,
        required
    } of targetFields(target)) if (required && mapping[field] === undefined) fail(0, label, 'Select its input column.');
    const selected = Object.values(mapping).filter(value => value !== undefined);
    if (new Set(selected).size !== selected.length || selected.some(column => !Number.isSafeInteger(column) || column < 0)) fail(0, 'Columns', 'Map each input column at most once.');
    if (result.errors.length) return result;
    const symbols = Array.isArray(graph.symbols) ? graph.symbols as { id: string; name: string }[] : [];
    const symbolId = (text: string) => {
        const found = symbols.filter(symbol => options.symbols === 'id' ? symbol.id === text : symbol.name === text);
        if (found.length !== 1) throw new Error(`Symbol ${JSON.stringify(text)} is unknown or ambiguous. Choose exact IDs or a unique displayed name.`);
        return found[0].id;
    };
    const integer = (text: string, label: string) => {
        if (!/^\d+$/.test(text) || !Number.isSafeInteger(Number(text))) throw new Error(`${label} must be an exact non-negative integer.`);
        return Number(text);
    };
    const weightDrafts = new Map<string, Map<string, string>>(), seenWeights = new Set<string>(),
        seenPays = new Map<string, string>();
    const reelDrafts = target.kind === 'reels' ? target.binding.stops.map(() => new Map<number, PhysicalStop>()) : [];
    const pendingLines = target.kind === 'lines' ? existing.find(patch => patch.kind === 'lines' && patch.key === target.binding.key) : undefined;
    const lines = target.kind === 'lines' ? (pendingLines?.kind === 'lines' ? pendingLines.paths : target.binding.lines).map(path => [...path]) : [],
        seenLines = new Set<string>();
    const weightSources = target.kind === 'weights' ? target.bindings : target.kind === 'paytable' && target.binding.prize ? weightBindings(graph).filter(source => target.binding.prize!.sources.some(value => value.key === source.key)) : [];

    function weightSource(value: string) {
        const found = !value && weightSources.length === 1 ? weightSources : weightSources.filter(source => source.key === value || source.source.id === value);
        if (found.length !== 1) throw new Error('Choose an unambiguous Draw source. Separate draws are never linked by equal numbers.');
        if (found[0].capability !== 'editable') throw new Error(found[0].reason ?? 'This distribution is not statically editable.');
        return found[0];
    }

    function weight(source: WeightBinding, id: string, value: string, row: number) {
        const identity = JSON.stringify([source.key, id]),
            outcome = source.rows.find(outcome => outcome.outcomeId === id);
        if (!outcome || seenWeights.has(identity)) throw new Error('Unknown or duplicated outcome in this Draw source.');
        seenWeights.add(identity);
        const pending = existing.find(patch => patch.kind === 'weights' && patch.key === source.key);
        const draft = weightDrafts.get(source.key) ?? new Map(pending?.kind === 'weights' ? pending.values : source.rows.map(row => [row.outcomeId, String(row.weight)]));
        const before = draft.get(id)!;
        let exact: string;
        try {
            exact = String(exactInteger(value));
        } catch (error) {
            throw new Error(`Weight requires an exact integer: ${error instanceof Error ? error.message : 'unsupported value'}`, {cause: error});
        }
        draft.set(id, exact);
        weightDrafts.set(source.key, draft);
        result.changes.push({row, item: `${source.source.id} / ${id} weight`, before, after: value});
    }

    let dataRows = 0;
    for (let index = options.headerRow + 1; index < sheet.rows.length; index++) {
        const row = sheet.rows[index], number = index + 1;
        if (selected.every(column => !row[column]?.text && !row[column]?.error)) continue;
        dataRows++;
        const read = (field: ImportField) => {
            const column = mapping[field];
            if (column === undefined) return '';
            const cell = row[column];
            if (cell?.error) throw new Error(`${cell.address ?? field}: ${cell.error}`);
            return cell?.text ?? '';
        };
        try {
            if (target.kind === 'weights') {
                const source = weightSource(read('sourceKey'));
                weight(source, read('outcomeId'), read('weight'), number);
            } else if (target.kind === 'reels') {
                const reel = integer(read('reel'), 'Reel') - 1, position = integer(read('index'), 'Physical index'),
                    symbol = symbolId(read('symbolId'));
                if (reel < 0 || reel >= reelDrafts.length || position > 4095 || reelDrafts[reel].has(position)) throw new Error('Unknown reel, duplicate index or physical index above 4095.');
                const original = target.binding.stops[reel],
                    id = mapping.stopId === undefined ? original[position]?.identity ?? null : read('stopId') || null;
                if (id !== null && !original.some(stop => stop.identity === id)) throw new Error('Retained stop ID does not belong to this reel. Leave the ID empty for an explicitly added stop.');
                const expectedWeight = id === null ? options.addedWeight : stopWeights(graph, target.binding, reel).get(id);
                if (mapping.weight !== undefined && exactInteger(read('weight')) !== exactInteger(expectedWeight ?? '')) throw new Error('This strip import retains stop weights. Change retained weights in their distribution; new weights must match the explicit added-stop policy.');
                reelDrafts[reel].set(position, {identity: id, symbol});
                result.changes.push({
                    row: number,
                    item: `Reel ${reel + 1} / stop ${position}`,
                    before: original[position]?.symbol ?? '(new stop)',
                    after: symbol
                });
            } else if (target.kind === 'lines') {
                const line = integer(read('line'), 'Line') - 1, column = integer(read('column'), 'Column'),
                    value = integer(read('row'), 'Row'), identity = `${line}/${column}`;
                if (line < 0 || line >= lines.length || column >= target.binding.columns || value >= target.binding.rows || seenLines.has(identity)) throw new Error('Unknown/duplicated line position or row outside the board. Line count changes require an explicit stake policy.');
                seenLines.add(identity);
                result.changes.push({
                    row: number,
                    item: `Line ${line + 1} / column ${column}`,
                    before: String(lines[line][column]),
                    after: String(value)
                });
                lines[line][column] = value;
            } else {
                const binding = target.binding, id = symbolId(read('symbolId')),
                    count = integer(read('count'), 'Paying count'),
                    amount = payoutValue(read('value'), binding, graph, options.unit),
                    authored = binding.rows.find(row => row.symbolId === id);
                if (!authored) throw new Error('Choose an authored paying symbol; adding eligibility requires its dependency-aware edit.');
                const position = authored.counts.indexOf(count);
                if (position < 0 && binding.cluster?.encoding !== 'shared-column-major') throw new Error('This paying size is outside the supported authored domain.');
                const expectedUnit = options.unit === 'native' ? nativeUnit(binding) : options.unit === 'credits' ? 'credits' : options.unit === 'round' ? 'round-price multiples' : 'total-bet multiples';
                if (mapping.unit !== undefined && read('unit') !== expectedUnit) throw new Error(`Unit column must say ${expectedUnit}; select its corresponding import unit.`);
                const identity = JSON.stringify([id, count]), previous = seenPays.get(identity);
                if (previous !== undefined && (previous !== amount || !binding.prize)) throw new Error('Duplicated payout coordinate or conflicting values for a shared prize.');
                if (previous === undefined) {
                    const pending = existing.find(patch => patch.kind === 'payout' && patch.key === binding.key && patch.symbolId === id && patch.count === count);
                    seenPays.set(identity, amount);
                    result.patches.push({
                        kind: 'payout',
                        key: binding.key,
                        symbolId: id,
                        count,
                        value: amount, ...(binding.cap || binding.bound ? {capPolicy: options.capPolicy ?? (binding.bound ? 'recompute' : 'keep')} : {})
                    });
                    result.changes.push({
                        row: number,
                        item: `${id} / ${count} payout`,
                        before: pending?.kind === 'payout' ? pending.remove ? '(staged removal)' : pending.value : authored.values[position] ?? '(new paying size)',
                        after: amount
                    });
                }
                if (binding.prize && mapping.weight !== undefined) {
                    const source = weightSource(read('sourceKey')), outcome = binding.prize.outcomeIds[position];
                    if (!outcome || (mapping.outcomeId !== undefined && read('outcomeId') !== outcome)) throw new Error('The original numeric prize outcome does not match this ordinal.');
                    weight(source, outcome, read('weight'), number);
                }
            }
        } catch (error) {
            fail(number, 'Mapped input', error instanceof Error ? error.message : 'Invalid row.');
        }
    }
    if (!dataRows) fail(0, 'Rows', 'This worksheet has no mapped data rows below its header.');
    for (const [key, values] of weightDrafts) {
        const source = weightSources.find(source => source.key === key)!,
            error = validateWeights(source.rows.map(row => values.get(row.outcomeId)!));
        if (error) fail(0, source.source.id, error); else result.patches.push({
            kind: 'weights',
            key,
            values: [...values]
        });
    }
    if (target.kind === 'reels') {
        const stops = reelDrafts.map((draft, reel) => {
            const ordered = [...draft].sort(([a], [b]) => a - b);
            if (!ordered.length || ordered.some(([position], index) => position !== index)) fail(0, `Reel ${reel + 1}`, 'Import each reel completely with consecutive indices starting at zero.');
            const retained = ordered.map(([, stop]) => stop.identity).filter(value => value !== null);
            if (new Set(retained).size !== retained.length) fail(0, `Reel ${reel + 1}`, 'A retained stop identity occurs twice.');
            return ordered.map(([, stop]) => stop);
        });
        result.patches.push({
            kind: 'reels',
            key: target.binding.key,
            stops, ...(stops.some(strip => strip.some(stop => stop.identity === null)) ? {addedWeight: options.addedWeight} : {}), ...(options.discardStops ? {removedStops: 'discard'} : {})
        });
    }
    if (target.kind === 'lines') result.patches.push({
        ...(pendingLines?.kind === 'lines' ? pendingLines : {}),
        kind: 'lines',
        key: target.binding.key,
        paths: lines
    });
    if (!result.errors.length) {
        const candidate = studioCandidate(graph, result.patches.reduce(replaceStudioPatch, existing));
        if (candidate.error) fail(0, 'Complete candidate', candidate.error);
    }
    if (result.errors.length) result.patches = [];
    return result;
}

export const tableTargetKey = (target: TableTarget) => target.kind === 'weights' ? canonicalJson(target.bindings.map(binding => binding.key)) : target.binding.key;
