import {Mechanics} from '../../games/shared/graph/flow';
import {payline, paylines} from '../../games/shared/graph/mechanics/paylines';
import {canonicalJson} from '../hash';
import type {ModelGraph, ModelNode} from '../modelProjection';
import {reelObject} from '../reelCommands';
import {matchReelShape} from './reelShape';
import {indexGraphSources, type NodeSource, resolveNodeSource, sourceKey, type SourcePath} from './sourceIndex';

interface LineSet {
    id: string;
    paylines: { positions: number[]; [key: string]: unknown }[];

    [key: string]: unknown
}

export interface LineBinding {
    version: 1;
    key: string;
    revision: string;
    setId: string;
    rows: number;
    columns: number;
    encoding: 'column-major';
    lines: readonly (readonly number[])[];
    scope: string;
    scopes: string[];
    calls: NodeSource[];
    uses: NodeSource[];
    reads: SourcePath[];
    writes: SourcePath[];
}

/** A payline path is proven against both the metadata and the existing generic execution fragment. */
export function lineBindings(graph: ModelGraph): LineBinding[] {
    const board = reelObject(graph.boardConfig), rows = Number(board?.rows), columns = Number(board?.columns);
    if (!Number.isSafeInteger(rows) || !Number.isSafeInteger(columns) || rows < 1 || columns < 1 || rows * columns > 64 || !Array.isArray(graph.paylineSets)) return [];
    const index = indexGraphSources(graph), sets = graph.paylineSets as LineSet[], bindings: LineBinding[] = [];
    for (const [setPosition, set] of sets.entries()) {
        if (typeof set.id !== 'string' || sets.filter(item => item.id === set.id).length !== 1 || !Array.isArray(set.paylines) || !set.paylines.length) continue;
        const paths = set.paylines.map(line => line.positions);
        if (paths.some(path => !Array.isArray(path) || path.length !== columns || path.some(row => !Number.isSafeInteger(row) || row < 0 || row >= rows))) continue;
        const template = paylines(new Mechanics(), paths, rows) as unknown as ModelGraph;
        const evaluator = payline(columns) as unknown as ModelGraph;
        for (const [scope, definition] of Object.entries(graph.mechanics ?? {})) {
            const shape = matchReelShape(definition, template), lineScope = shape?.libraries.get('payline');
            if (!shape || !lineScope || !graph.mechanics?.[lineScope] || !matchReelShape(graph.mechanics[lineScope], evaluator)) continue;
            const calls = paths.map((_, position) => resolveNodeSource(graph, sourceKey(scope, shape.nodes.get(`line-${position + 1}`)!.id)));
            const uses = index.libraryUses.filter(use => use.definition === scope || use.definition === lineScope).map(use => use.source);
            const reads: SourcePath[] = [['boardConfig'], ['paylineSets', setPosition], ['mechanics', scope], ['mechanics', lineScope], ...uses.map(use => use.path)];
            bindings.push({
                version: 1,
                key: JSON.stringify([set.id, scope]),
                revision: canonicalJson({
                    board,
                    set,
                    definition,
                    evaluator: graph.mechanics[lineScope],
                    uses: uses.map(use => [use.key, use.node])
                }),
                setId: set.id,
                rows,
                columns,
                encoding: 'column-major',
                lines: paths,
                scope,
                scopes: [scope],
                calls,
                uses,
                reads,
                writes: [['paylineSets', setPosition, 'paylines'], ...calls.map(call => [...call.path, 'parameters'])]
            });
        }
    }
    return sets.flatMap(set => {
        const related = bindings.filter(binding => binding.setId === set.id);
        if (!related.length) return [];
        const first = related[0];
        return [{
            ...first,
            key: JSON.stringify([set.id]),
            revision: canonicalJson(related.map(binding => binding.revision)),
            scopes: related.flatMap(binding => binding.scopes),
            calls: related.flatMap(binding => binding.calls),
            uses: [...new Map(related.flatMap(binding => binding.uses).map(use => [use.key, use])).values()],
            reads: related.flatMap(binding => binding.reads),
            writes: related.flatMap(binding => binding.writes)
        }];
    });
}

export function changeBoundLines(graph: ModelGraph, binding: LineBinding, paths: readonly (readonly number[])[]): ModelGraph {
    const current = lineBindings(graph).find(item => item.key === binding.key);
    if (!current || current.revision !== binding.revision) throw new Error('The payline definition, board or shared uses changed. Reconcile this draft.');
    if (paths.length !== current.lines.length) throw new Error('Changing line count requires a separate explicit stake policy. This edit preserves the number of lines.');
    if (paths.some(path => path.length !== current.columns || path.some(row => !Number.isSafeInteger(row) || row < 0 || row >= current.rows))) throw new Error('Every line needs one in-bounds row for each board column.');
    if (canonicalJson(paths) === canonicalJson(current.lines)) return structuredClone(graph);
    const next = structuredClone(graph), sets = next.paylineSets as LineSet[];
    const set = sets.find(set => set.id === current.setId)!;
    set.paylines = set.paylines.map((line, position) => ({...line, positions: [...paths[position]]}));
    current.calls.forEach((call, position) => {
        const node = resolveNodeSource(next, call.key).node as ModelNode;
        const parameters = {...reelObject(node.parameters)};
        paths[position % paths.length].forEach((row, column) => {
            parameters[`p${column}`] = String(column * current.rows + row);
        });
        // The resolved node is a reference into our private candidate, never into the committed graph.
        node.parameters = parameters;
    });
    return next;
}
