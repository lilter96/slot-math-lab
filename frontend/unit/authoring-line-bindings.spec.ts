import {expect, test} from '@playwright/test';
import {createAncientDragonGraph} from '../src/games/ancient-dragon/graph';
import {createWoodlandWhisperGraph} from '../src/games/woodland-whisper/graph';
import {createX7ClubGraph} from '../src/games/x7-club/graph';
import {createNineLivesGraph} from '../src/games/nine-lives-tgslots/graph';
import type {ModelGraph} from '../src/lib/modelProjection';
import {changeBoundLines, lineBindings} from '../src/lib/authoring/lineBindings';

for (const [name, create, count] of [['Ancient Dragon', createAncientDragonGraph, 25], ['Woodland Whisper', createWoodlandWhisperGraph, 30], ['X7 Club', createX7ClubGraph, 20]] as const) {
    test(`${name}: payline paths update column-major execution parameters and preserve stake, metadata and other lines`, () => {
        const graph = create() as ModelGraph, binding = lineBindings(graph)[0];
        expect(binding).toBeDefined();
        expect(binding.lines).toHaveLength(count);
        const before = structuredClone(graph), paths = binding.lines.map(path => [...path]);
        paths[0][0] = (paths[0][0] + 1) % binding.rows;
        const next = changeBoundLines(graph, binding, paths), first = binding.calls[0];
        expect(next.mechanics![first.scope!].nodes.find(node => node.id === first.id)!.parameters).toEqual(Object.fromEntries(paths[0].map((row, column) => [`p${column}`, String(column * binding.rows + row)])));
        expect(next.initialState).toEqual(before.initialState);
        expect(next.paytables).toEqual(before.paytables);
        expect(next.nodes).toEqual(before.nodes);
        expect(graph).toEqual(before);
        expect(lineBindings(next)[0].lines).toEqual(paths);
        expect(changeBoundLines(graph, binding, binding.lines)).toEqual(graph);
        expect(() => changeBoundLines(graph, binding, paths.slice(1))).toThrow('stake policy');
        paths[0][0] = binding.rows;
        expect(() => changeBoundLines(graph, binding, paths)).toThrow('in-bounds');
    });
}
test('Renamed shared payline fragments remain editable; stale geometry and inconsistent metadata stay locally unavailable', () => {
    const graph = createAncientDragonGraph() as ModelGraph;
    const original = lineBindings(graph)[0], definition = graph.mechanics![original.scope];
    const oldCall = original.calls[0].id;
    definition.nodes.find(node => node.id === oldCall)!.id = 'my-private-line';
    for (const edge of definition.edges as Record<string, unknown>[]) {
        if (edge.sourceNodeId === oldCall) edge.sourceNodeId = 'my-private-line';
        if (edge.targetNodeId === oldCall) edge.targetNodeId = 'my-private-line';
    }
    graph.mechanics!['my-lines'] = definition;
    delete graph.mechanics![original.scope];
    for (const scope of [graph, ...Object.values(graph.mechanics ?? {})]) for (const node of scope.nodes ?? []) if (node.mechanicName === original.scope) node.mechanicName = 'my-lines';
    const binding = lineBindings(graph)[0];
    expect(binding.scope).toBe('my-lines');
    expect(binding.calls[0].id).toBe('my-private-line');
    const changed = structuredClone(graph);
    (changed.boardConfig as { rows: number }).rows = 2;
    expect(() => changeBoundLines(changed, binding, binding.lines)).toThrow('changed');
    const broken = structuredClone(graph);
    (broken.paylineSets as { paylines: { positions: number[] }[] }[])[0].paylines[0].positions[0] = 2;
    expect(lineBindings(broken)).toEqual([]);
    expect(lineBindings(createNineLivesGraph() as ModelGraph)).toEqual([]);
});
test('Duplicated execution definitions linked to one declared line set change together without merging unrelated state', () => {
    const graph = createAncientDragonGraph() as ModelGraph, original = lineBindings(graph)[0];
    graph.mechanics!['second-line-stage'] = structuredClone(graph.mechanics![original.scope]);
    const binding = lineBindings(graph)[0];
    expect(binding.scopes).toHaveLength(2);
    expect(binding.calls).toHaveLength(50);
    const paths = binding.lines.map(path => [...path]);
    paths[0][0] = (paths[0][0] + 1) % binding.rows;
    const next = changeBoundLines(graph, binding, paths);
    for (const scope of binding.scopes) expect((next.mechanics![scope].nodes.find(node => node.id === original.calls[0].id)!.parameters as Record<string, string>).p0).toBe(String(paths[0][0]));
    expect(next.initialState).toEqual(graph.initialState);
    expect(next.paytables).toEqual(graph.paytables);
});
