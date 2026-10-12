import {canonicalJson} from '../hash';
import {conditionStateKeys} from '../reelConditioning';
import type {ModelGraph} from '../modelProjection';
import {changeReels, type ReelSource, reelSources} from '../reelCommands';
import {indexGraphSources, type NodeSource, sourceKey, type SourcePath} from './sourceIndex';

export interface PhysicalStop {
    identity: string | null;
    symbol: string
}

export interface ReelBinding {
    version: 1;
    key: string;
    revision: string;
    source: ReelSource;
    encoding: 'column-major';
    weightUnit: 'integer stop weight';
    reads: { path: SourcePath; fingerprint: string }[];
    writes: SourcePath[];
    uses: NodeSource[];
    stops: PhysicalStop[][];
}

const atPath = (graph: ModelGraph, path: SourcePath): unknown => path.reduce<unknown>((value, key) => value && typeof value === 'object' ? (value as Record<string, unknown>)[key] : undefined, graph);

/** Strips, random domains and all shared refill/conditional consumers form one authoring binding. */
export function reelBindings(graph: ModelGraph): ReelBinding[] {
    const index = indexGraphSources(graph);
    return reelSources(graph).map(source => {
        const scopes = new Set(source.stops.map(stop => stop.scope));
        const uses: NodeSource[] = [];
        let added = true;
        while (added) {
            added = false;
            for (const use of index.libraryUses) if (scopes.has(use.definition) && !uses.some(node => node.key === use.source.key)) {
                uses.push(use.source);
                if (use.source.scope !== null && !scopes.has(use.source.scope)) {
                    scopes.add(use.source.scope);
                    added = true;
                }
            }
        }
        const paths: SourcePath[] = [['boardConfig'], ['symbols'], ...source.stripKeys.map(key => ['initialState', key]), ...[...scopes].map(scope => ['mechanics', scope])];
        for (const condition of source.conditions ?? []) {
            const keys = conditionStateKeys(condition);
            paths.push(['initialState', keys.patterns], ['initialState', keys.pattern]);
        }
        for (const use of uses.filter(node => node.scope === null)) paths.push(use.path);
        const reads = paths.map(path => ({path, fingerprint: canonicalJson({value: atPath(graph, path)})}));
        const writes: SourcePath[] = source.stripKeys.map(key => ['initialState', key]);
        for (const stop of source.stops) {
            const node = index.nodes.find(node => node.key === sourceKey(stop.scope, stop.nodeId));
            if (node && !source.conditions?.some(condition => condition.scope === stop.scope)) writes.push([...node.path, 'drawWeights']);
        }
        for (const condition of source.conditions ?? []) writes.push(['mechanics', condition.scope], ['initialState', conditionStateKeys(condition).patterns]);
        return {
            version: 1,
            key: source.key,
            revision: canonicalJson(reads),
            source,
            encoding: 'column-major',
            weightUnit: 'integer stop weight',
            reads,
            writes,
            uses,
            stops: source.strips.map(strip => strip.map((symbol, position) => ({identity: String(position), symbol})))
        };
    });
}

export function changeBoundReels(graph: ModelGraph, binding: ReelBinding, stops: PhysicalStop[][], policy: {
    addedWeight?: string;
    removedStops?: 'discard'
} = {}): ModelGraph {
    const current = reelBindings(graph).find(source => source.key === binding.key);
    if (!current || current.revision !== binding.revision) throw new Error('The reel source or one of its shared dependencies changed. Reconcile this draft.');
    if (stops.length !== binding.stops.length) throw new Error('Keep the authored reel count unchanged.');
    const added = stops.some(strip => strip.some(stop => stop.identity === null));
    const removed = binding.stops.some((strip, reel) => strip.some(stop => !stops[reel].some(item => item.identity === stop.identity)));
    if (added && policy.addedWeight === undefined) throw new Error('Choose the exact weight for added physical stops.');
    if (removed && policy.removedStops !== 'discard') throw new Error('Confirm discarding the removed stop weights.');
    if (sourceConditioned(binding) && (policy.addedWeight !== undefined && policy.addedWeight !== '1')) throw new Error('This conditioned source currently requires uniform ordinary stop weights.');
    return changeReels(graph, binding.key, stops.map(strip => strip.map(stop => stop.symbol)), {
        ...policy,
        identities: stops.map(strip => strip.map(stop => stop.identity))
    });
}

const sourceConditioned = (binding: ReelBinding) => !!binding.source.conditions?.length;
