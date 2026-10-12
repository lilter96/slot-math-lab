import { canonicalJson } from '../hash';
import { changeWeights, validateWeights, type DrawRow } from '../modelCommands';
import type { ModelGraph } from '../modelProjection';
import { indexGraphSources, resolveNodeSource, type NodeSource } from './sourceIndex';
import { reelSources } from '../reelCommands';
export interface WeightBinding {
  version: 1;
  key: string;
  source: NodeSource;
  capability: 'editable' | 'computed' | 'dynamic' | 'custom';
  reason: string | null;
  reads: { sourceKey: string; fingerprint: string }[];
  writes: { sourceKey: string; field: 'drawWeights.weight'; outcomeIds: string[] }[];
  uses: NodeSource[];
  rows: readonly DrawRow[];
  total: string | null;
}

/** A source is never linked to another Draw merely because its numeric rows match. */
export function weightBindings(graph: ModelGraph): WeightBinding[] {
  const index = indexGraphSources(graph);
  const conditioned = new Set(reelSources(graph).filter(source => source.conditions?.length).flatMap(source => [...source.stops.map(stop => stop.scope), ...source.conditions!.map(condition => condition.scope)]));
  return index.nodes.filter(source => source.node.nodeType === 'draw').map(source => {
    const raw = source.node.drawWeights, dynamic = !!source.node.weightExpressionId;
    const rows = Array.isArray(raw) ? raw : [];
    const unique = new Set<string>();
    const valid = index.nodes.filter(node => node.key === source.key).length === 1 && rows.length > 0 && rows.every(row => {
      if (!row || typeof row.outcomeId !== 'string' || unique.has(row.outcomeId)) return false;
      unique.add(row.outcomeId);
      return Number.isSafeInteger(row.weight) && row.weight >= 0 && Number.isSafeInteger(row.value);
    });
    const error = valid ? validateWeights(rows.map(row => String(row.weight))) : 'Outcome IDs or integer values are ambiguous/unsupported. Keep the original graph and inspect this source.';
    const uses = index.libraryUses.filter(use => use.definition === source.scope).map(use => use.source);
    const reads = [source, ...uses].map(node => ({ sourceKey: node.key, fingerprint: canonicalJson(node.node) }));
    const computed = source.scope !== null && conditioned.has(source.scope);
    const capability = computed ? 'computed' : dynamic ? 'dynamic' : error ? 'custom' : 'editable';
    return { version: 1, key: source.key, source, capability, reason: computed ? 'This distribution belongs to a conditioned reel contract. Edit its ordered strips so all conditional dependencies stay synchronized.' : dynamic ? 'Weights come from an expression. Preserve it; this table cannot replace it with sampled/static values.' : error,
      reads, writes: capability === 'editable' ? [{ sourceKey: source.key, field: 'drawWeights.weight', outcomeIds: rows.map(row => row.outcomeId) }] : [], uses,
      rows, total: !error && !dynamic ? rows.reduce((sum, row) => sum + BigInt(row.weight), 0n).toString() : null };
  });
}
export function changeBoundWeights(graph: ModelGraph, binding: WeightBinding, weights: ReadonlyMap<string, string>): ModelGraph {
  const current = weightBindings(graph).find(source => source.key === binding.key);
  if (!current || current.capability !== 'editable') throw new Error(current?.reason ?? 'This source is no longer editable. Rebuild the binding.');
  if (canonicalJson(current.reads.map(read => read.sourceKey)) !== canonicalJson(binding.reads.map(read => read.sourceKey))) throw new Error('The set of shared uses changed. Rebuild the binding and review its scope.');
  for (const read of binding.reads) if (canonicalJson(resolveNodeSource(graph, read.sourceKey).node) !== read.fingerprint) throw new Error('A source or shared use changed. Reconcile the weight draft before applying.');
  if (weights.size !== current.rows.length || current.rows.some(row => !weights.has(row.outcomeId))) throw new Error('Keep all authored outcome identities in this weight-only edit.');
  return changeWeights(graph, binding.key, current.rows.map(row => weights.get(row.outcomeId)!));
}
