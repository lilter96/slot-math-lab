import {canonicalJson} from '../hash';
import {browserStorageKey} from '../browserIdentity';
import {parseModelJson} from '../modelJson';
import type {ModelGraph} from '../modelProjection';
import {changeBoundReels, type PhysicalStop, reelBindings} from './reelBindings';
import {changeBoundWeights, weightBindings} from './weightBindings';
import {changeBoundLines, lineBindings} from './lineBindings';
import type {PayBinding} from './payBindings';
import {changeBoundPayouts, payBindings} from './payBindings';
import {computedLineBound} from './payoutBounds';
import {
    changePrizeDomain,
    changeSourceOutcomes,
    copySourceUse,
    createDrawingSource,
    type OutcomeInput,
    type SourceConnection,
    type SourceDefinitionInput
} from './sourceLifecycle';
import {changeLineDomain, type LineStakePolicy} from './lineLifecycle';
import {
    addDictionarySymbol,
    changePayingDomain,
    type DictionarySymbol,
    removeDictionarySymbol
} from './symbolLifecycle';

export type StudioPatch =
    { kind: 'reels'; key: string; stops: PhysicalStop[][]; addedWeight?: string; removedStops?: 'discard' }
    | { kind: 'weights'; key: string; values: [string, string][] }
    | {
    kind: 'payout';
    key: string;
    symbolId: string;
    count: number;
    value: string;
    remove?: boolean;
    capPolicy?: 'keep' | 'recompute'
}
    | { kind: 'lines'; key: string; paths: number[][]; identities?: (string | null)[]; stakePolicy?: LineStakePolicy }
    | { kind: 'symbol'; key: string; name: string }
    | { kind: 'add-symbol'; key: string; symbol: DictionarySymbol }
    | { kind: 'remove-symbol'; key: string }
    | { kind: 'pay-domain'; key: string; rows: PayBinding['rows']; capPolicy: 'keep' | 'recompute' }
    | SourceStudioPatch;
export type SourceStudioPatch =
    { kind: 'create-source'; key: string; input: SourceDefinitionInput; connection: SourceConnection }
    | { kind: 'copy-source'; key: string; nodeKey: string; name: string }
    | { kind: 'outcomes'; key: string; rows: OutcomeInput[] }
    | { kind: 'prize-domain'; key: string; values: { value: string; addedWeight?: string }[]; discard: boolean };

export interface StudioDraft {
    version: 1;
    projectId: string;
    baseHash: string;
    patches: StudioPatch[]
}

export const patchKey = (patch: StudioPatch) => patch.kind === 'payout' ? JSON.stringify([patch.kind, patch.key, patch.symbolId, patch.count]) : JSON.stringify([patch.kind, patch.key]);

export function replaceStudioPatch(patches: StudioPatch[], patch: StudioPatch): StudioPatch[] {
    const key = patchKey(patch), position = patches.findIndex(value => patchKey(value) === key);
    return position < 0 ? [...patches, patch] : patches.map((value, index) => index === position ? patch : value);
}

export function applyStudioPatches(graph: ModelGraph, patches: StudioPatch[]): ModelGraph {
    let candidate = structuredClone(graph);
    const payoutTables = new Set<string>();
    const payoutEdits = patches.filter((patch): patch is Extract<StudioPatch, {
        kind: 'payout'
    }> => patch.kind === 'payout');
    const cappedTables = new Set(payBindings(graph).filter(binding => binding.kind === 'line' || binding.kind === 'scatter').map(binding => binding.key));
    const effectivePolicy = (patch: Extract<StudioPatch, {
        kind: 'payout'
    }>) => patch.capPolicy ?? (computedLineBound(graph) ? 'recompute' : 'keep');
    if (new Set(payoutEdits.filter(patch => cappedTables.has(patch.key)).map(effectivePolicy)).size > 1) throw new Error('Choose the same payout-cap policy for all tables sharing this result cap.');
    // Prize values are numeric Draw identities. Apply weight drafts against their original identities first.
    for (const patch of [...patches.filter(patch => patch.kind !== 'payout'), ...payoutEdits]) {
        if (patch.kind === 'add-symbol') candidate = addDictionarySymbol(candidate, patch.symbol);
        else if (patch.kind === 'remove-symbol') candidate = removeDictionarySymbol(candidate, patch.key);
        else if (patch.kind === 'pay-domain') candidate = changePayingDomain(candidate, patch.key, patch.rows, patch.capPolicy);
        else if (patch.kind === 'create-source') candidate = createDrawingSource(candidate, patch.input, patch.connection);
        else if (patch.kind === 'copy-source') candidate = copySourceUse(candidate, patch.nodeKey, patch.name);
        else if (patch.kind === 'outcomes') candidate = changeSourceOutcomes(candidate, patch.key, patch.rows);
        else if (patch.kind === 'prize-domain') candidate = changePrizeDomain(candidate, patch.key, patch.values, patch.discard);
        else if (patch.kind === 'weights') {
            const source = weightBindings(candidate).find(source => source.key === patch.key);
            if (!source) throw new Error('The weighted source is unavailable.');
            candidate = changeBoundWeights(candidate, source, new Map(patch.values));
        } else if (patch.kind === 'reels') {
            const source = reelBindings(candidate).find(source => source.key === patch.key);
            if (!source) throw new Error('The physical source is unavailable.');
            candidate = changeBoundReels(candidate, source, patch.stops, {
                addedWeight: patch.addedWeight,
                removedStops: patch.removedStops
            });
        } else if (patch.kind === 'payout') {
            if (payoutTables.has(patch.key)) continue;
            payoutTables.add(patch.key);
            const source = payBindings(candidate).find(source => source.key === patch.key);
            if (!source) throw new Error('The payout source is unavailable.');
            const edits = patches.filter((item): item is Extract<StudioPatch, {
                kind: 'payout'
            }> => item.kind === 'payout' && item.key === patch.key);
            if (cappedTables.has(patch.key) && edits.some(edit => effectivePolicy(edit) !== effectivePolicy(edits[0]))) throw new Error('Choose one payout-cap policy for this table batch.');
            candidate = changeBoundPayouts(candidate, source, edits, edits[0].capPolicy);
        } else if (patch.kind === 'lines') {
            const source = lineBindings(candidate).find(source => source.key === patch.key);
            if (!source) throw new Error('The payline source is unavailable.');
            if (patch.identities) {
                if (!patch.stakePolicy) throw new Error('Choose an explicit stake and cap policy for a changed line domain.');
                candidate = changeLineDomain(candidate, source, patch.paths, patch.identities, patch.stakePolicy);
            } else candidate = changeBoundLines(candidate, source, patch.paths);
        } else {
            const symbols = Array.isArray(candidate.symbols) ? candidate.symbols as { id: string; name: string }[] : [];
            if (!patch.name.trim() || symbols.filter(symbol => symbol.id === patch.key).length !== 1) throw new Error('A symbol needs a display name and one unambiguous ID.');
            candidate.symbols = symbols.map(symbol => symbol.id === patch.key ? {...symbol, name: patch.name} : symbol);
        }
    }
    return candidate;
}

const storageKey = (projectId: string) => browserStorageKey(`slotmath-studio-draft-v1:${projectId}`);

/** Recoverable form inputs, separate from the executable graph and view-metadata envelope. Never sent to an API. */
export function saveStudioDraft(draft: StudioDraft): void {
    localStorage.setItem(storageKey(draft.projectId), JSON.stringify(draft));
}

export function clearStudioDraft(projectId: string): void {
    localStorage.removeItem(storageKey(projectId));
}

export function storedStudioDraft(projectId: string): string | null {
    return localStorage.getItem(storageKey(projectId));
}

export function readStudioDraft(projectId: string): StudioDraft | null {
    const stored = storedStudioDraft(projectId);
    if (!stored) return null;
    const value = parseModelJson(stored);
    if (value.version !== 1 || value.projectId !== projectId || typeof value.baseHash !== 'string' || !/^[a-f0-9]{64}$/.test(value.baseHash) || !Array.isArray(value.patches)
        || value.patches.some(patch => !validPatch(patch)) || new Set(value.patches.map(patch => patchKey(patch))).size !== value.patches.length) throw new Error('The pending Studio draft could not be opened. Its original browser copy is retained.');
    return value as unknown as StudioDraft;
}

function validPatch(value: unknown): value is StudioPatch {
    if (!value || typeof value !== 'object') return false;
    const patch = value as Record<string, unknown>;
    if (typeof patch.key !== 'string') return false;
    const outcomes = (rows: unknown) => Array.isArray(rows) && rows.every(row => row && typeof row === 'object' && typeof row.id === 'string' && typeof row.weight === 'string' && typeof row.payload === 'string');
    if (patch.kind === 'outcomes') return outcomes(patch.rows);
    if (patch.kind === 'remove-symbol') return true;
    if (patch.kind === 'add-symbol') {
        const symbol = patch.symbol as Record<string, unknown> | undefined;
        return !!symbol && ['id', 'name', 'kind'].every(key => typeof symbol[key] === 'string') && symbol.id === patch.key;
    }
    if (patch.kind === 'pay-domain') return ['keep', 'recompute'].includes(String(patch.capPolicy)) && Array.isArray(patch.rows) && patch.rows.every(row => row && typeof row === 'object' && typeof row.symbolId === 'string' && Array.isArray(row.counts) && row.counts.every((count: unknown) => typeof count === 'number' && Number.isSafeInteger(count)) && Array.isArray(row.values) && row.values.every((value: unknown) => typeof value === 'string'));
    if (patch.kind === 'copy-source') return typeof patch.nodeKey === 'string' && typeof patch.name === 'string';
    if (patch.kind === 'prize-domain') return typeof patch.discard === 'boolean' && Array.isArray(patch.values) && patch.values.every(row => row && typeof row === 'object' && typeof row.value === 'string' && (row.addedWeight === undefined || typeof row.addedWeight === 'string'));
    if (patch.kind === 'create-source') {
        const input = patch.input as Record<string, unknown> | undefined,
            connection = patch.connection as Record<string, unknown> | undefined;
        const connect = connection && (['definition', 'empty-round'].includes(String(connection.kind)) || (connection.kind === 'replace' ? typeof connection.nodeKey === 'string' : connection.kind === 'edge' && (connection.scope === null || typeof connection.scope === 'string') && typeof connection.edgeId === 'string'));
        const symbols = input?.symbols === undefined || Array.isArray(input.symbols) && input.symbols.every(row => row && typeof row === 'object' && ['id', 'name', 'kind'].every(key => typeof row[key] === 'string'));
        return !!connect && !!input && typeof input.name === 'string' && symbols && (input.kind === 'outcomes' ? typeof input.stateKey === 'string' && outcomes(input.outcomes) : input.kind === 'cells' ? Number.isSafeInteger(input.rows) && Number.isSafeInteger(input.columns) && outcomes(input.outcomes) : input.kind === 'reels' && Number.isSafeInteger(input.rows) && Array.isArray(input.strips) && input.strips.every(strip => Array.isArray(strip) && strip.every(id => typeof id === 'string')));
    }
    if (patch.kind === 'weights') return Array.isArray(patch.values) && patch.values.every(row => Array.isArray(row) && row.length === 2 && row.every(value => typeof value === 'string')) && new Set(patch.values.map(row => row[0])).size === patch.values.length;
    if (patch.kind === 'payout') return typeof patch.symbolId === 'string' && typeof patch.count === 'number' && Number.isSafeInteger(patch.count) && typeof patch.value === 'string' && (patch.remove === undefined || typeof patch.remove === 'boolean') && (patch.capPolicy === undefined || ['keep', 'recompute'].includes(String(patch.capPolicy)));
    if (patch.kind === 'symbol') return typeof patch.name === 'string';
    if (patch.kind === 'lines') {
        const policy = patch.stakePolicy as Record<string, unknown> | undefined;
        return Array.isArray(patch.paths) && patch.paths.every(path => Array.isArray(path) && path.every(row => typeof row === 'number' && Number.isSafeInteger(row))) && (patch.identities === undefined || Array.isArray(patch.identities) && patch.identities.every(id => id === null || typeof id === 'string')) && (policy === undefined || ['per-line', 'fixed-total'].includes(String(policy.mode)) && ['keep', 'recompute'].includes(String(policy.cap)) && (policy.confirmFixed === undefined || typeof policy.confirmFixed === 'boolean'));
    }
    if (patch.kind === 'reels') return Array.isArray(patch.stops) && patch.stops.every(strip => Array.isArray(strip) && strip.every(stop => stop && typeof stop === 'object' && (stop.identity === null || typeof stop.identity === 'string') && typeof stop.symbol === 'string'))
        && (patch.addedWeight === undefined || typeof patch.addedWeight === 'string') && (patch.removedStops === undefined || patch.removedStops === 'discard');
    return false;
}

export function studioCandidate(graph: ModelGraph, patches: StudioPatch[]): {
    graph: ModelGraph | null;
    error: string | null;
    changed: boolean
} {
    try {
        const candidate = applyStudioPatches(graph, patches);
        return {graph: candidate, error: null, changed: canonicalJson(candidate) !== canonicalJson(graph)};
    } catch (error) {
        return {graph: null, error: error instanceof Error ? error.message : 'The draft is incomplete.', changed: true};
    }
}
