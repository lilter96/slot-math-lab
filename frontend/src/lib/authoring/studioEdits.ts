import { canonicalJson } from '../hash';
import { browserStorageKey } from '../browserIdentity';
import { parseModelJson } from '../modelJson';
import type { ModelGraph } from '../modelProjection';
import { changeBoundReels, reelBindings, type PhysicalStop } from './reelBindings';
import { changeBoundWeights, weightBindings } from './weightBindings';
import { changeBoundLines, lineBindings } from './lineBindings';
import { changeBoundPayouts, payBindings } from './payBindings';
import { computedLineBound } from './payoutBounds';

export type StudioPatch = { kind: 'reels'; key: string; stops: PhysicalStop[][]; addedWeight?: string; removedStops?: 'discard' }
  | { kind: 'weights'; key: string; values: [string, string][] }
  | { kind: 'payout'; key: string; symbolId: string; count: number; value: string; capPolicy?: 'keep' | 'recompute' }
  | { kind: 'lines'; key: string; paths: number[][] }
  | { kind: 'symbol'; key: string; name: string };
export interface StudioDraft { version: 1; projectId: string; baseHash: string; patches: StudioPatch[] }
export const patchKey = (patch: StudioPatch) => patch.kind === 'payout' ? JSON.stringify([patch.kind, patch.key, patch.symbolId, patch.count]) : JSON.stringify([patch.kind, patch.key]);
export function replaceStudioPatch(patches: StudioPatch[], patch: StudioPatch): StudioPatch[] {
  const key = patchKey(patch), position = patches.findIndex(value => patchKey(value) === key);
  return position < 0 ? [...patches, patch] : patches.map((value, index) => index === position ? patch : value);
}
export function applyStudioPatches(graph: ModelGraph, patches: StudioPatch[]): ModelGraph {
  let candidate = structuredClone(graph);
  const payoutTables = new Set<string>();
  const payoutEdits = patches.filter((patch): patch is Extract<StudioPatch, { kind: 'payout' }> => patch.kind === 'payout');
  if (payoutEdits.length && new Set(payoutEdits.map(patch => patch.capPolicy ?? (computedLineBound(graph) ? 'recompute' : 'keep'))).size > 1) throw new Error('Choose the same payout-cap policy for all tables sharing this result cap.');
  for (const patch of patches) {
    if (patch.kind === 'weights') {
      const source = weightBindings(candidate).find(source => source.key === patch.key); if (!source) throw new Error('The weighted source is unavailable.');
      candidate = changeBoundWeights(candidate, source, new Map(patch.values));
    } else if (patch.kind === 'reels') {
      const source = reelBindings(candidate).find(source => source.key === patch.key); if (!source) throw new Error('The physical source is unavailable.');
      candidate = changeBoundReels(candidate, source, patch.stops, { addedWeight: patch.addedWeight, removedStops: patch.removedStops });
    } else if (patch.kind === 'payout') {
      if (payoutTables.has(patch.key)) continue;
      payoutTables.add(patch.key);
      const source = payBindings(candidate).find(source => source.key === patch.key); if (!source) throw new Error('The payout source is unavailable.');
      const edits = patches.filter((item): item is Extract<StudioPatch, { kind: 'payout' }> => item.kind === 'payout' && item.key === patch.key);
      if (edits.some(edit => edit.capPolicy !== edits[0].capPolicy)) throw new Error('Choose one payout-cap policy for this table batch.');
      candidate = changeBoundPayouts(candidate, source, edits, edits[0].capPolicy);
    } else if (patch.kind === 'lines') {
      const source = lineBindings(candidate).find(source => source.key === patch.key); if (!source) throw new Error('The payline source is unavailable.');
      candidate = changeBoundLines(candidate, source, patch.paths);
    } else {
      const symbols = Array.isArray(candidate.symbols) ? candidate.symbols as { id: string; name: string }[] : [];
      if (!patch.name.trim() || symbols.filter(symbol => symbol.id === patch.key).length !== 1) throw new Error('A symbol needs a display name and one unambiguous ID.');
      candidate.symbols = symbols.map(symbol => symbol.id === patch.key ? { ...symbol, name: patch.name } : symbol);
    }
  }
  return candidate;
}
const storageKey = (projectId: string) => browserStorageKey(`slotmath-studio-draft-v1:${projectId}`);
/** Recoverable form inputs, separate from the executable graph and view-metadata envelope. Never sent to an API. */
export function saveStudioDraft(draft: StudioDraft): void { localStorage.setItem(storageKey(draft.projectId), JSON.stringify(draft)); }
export function clearStudioDraft(projectId: string): void { localStorage.removeItem(storageKey(projectId)); }
export function storedStudioDraft(projectId: string): string | null { return localStorage.getItem(storageKey(projectId)); }
export function readStudioDraft(projectId: string): StudioDraft | null {
  const stored = storedStudioDraft(projectId); if (!stored) return null;
  const value = parseModelJson(stored);
  if (value.version !== 1 || value.projectId !== projectId || typeof value.baseHash !== 'string' || !/^[a-f0-9]{64}$/.test(value.baseHash) || !Array.isArray(value.patches)
    || value.patches.some(patch => !validPatch(patch)) || new Set(value.patches.map(patch => patchKey(patch))).size !== value.patches.length) throw new Error('The pending Studio draft could not be opened. Its original browser copy is retained.');
  return value as unknown as StudioDraft;
}
function validPatch(value: unknown): value is StudioPatch {
  if (!value || typeof value !== 'object') return false;
  const patch = value as Record<string, unknown>; if (typeof patch.key !== 'string') return false;
  if (patch.kind === 'weights') return Array.isArray(patch.values) && patch.values.every(row => Array.isArray(row) && row.length === 2 && row.every(value => typeof value === 'string')) && new Set(patch.values.map(row => row[0])).size === patch.values.length;
  if (patch.kind === 'payout') return typeof patch.symbolId === 'string' && typeof patch.count === 'number' && Number.isSafeInteger(patch.count) && typeof patch.value === 'string' && (patch.capPolicy === undefined || ['keep', 'recompute'].includes(String(patch.capPolicy)));
  if (patch.kind === 'symbol') return typeof patch.name === 'string';
  if (patch.kind === 'lines') return Array.isArray(patch.paths) && patch.paths.every(path => Array.isArray(path) && path.every(row => typeof row === 'number' && Number.isSafeInteger(row)));
  if (patch.kind === 'reels') return Array.isArray(patch.stops) && patch.stops.every(strip => Array.isArray(strip) && strip.every(stop => stop && typeof stop === 'object' && (stop.identity === null || typeof stop.identity === 'string') && typeof stop.symbol === 'string'))
    && (patch.addedWeight === undefined || typeof patch.addedWeight === 'string') && (patch.removedStops === undefined || patch.removedStops === 'discard');
  return false;
}
export function studioCandidate(graph: ModelGraph, patches: StudioPatch[]): { graph: ModelGraph | null; error: string | null; changed: boolean } {
  try { const candidate = applyStudioPatches(graph, patches); return { graph: candidate, error: null, changed: canonicalJson(candidate) !== canonicalJson(graph) }; }
  catch (error) { return { graph: null, error: error instanceof Error ? error.message : 'The draft is incomplete.', changed: true }; }
}
