import {expect, test} from '@playwright/test';
import {createAncientDragonGraph} from '../src/games/ancient-dragon/graph';
import {createWoodlandWhisperGraph} from '../src/games/woodland-whisper/graph';
import {createLeMilitareGraph} from '../src/games/le-militare/graph';
import {createNineLivesGraph} from '../src/games/nine-lives/graph';
import {changeReels, reelSources, validateReels} from '../src/lib/reelCommands';
import type {ModelGraph} from '../src/lib/modelProjection';

for (const [name, create, groups] of [
    ['Ancient Dragon', createAncientDragonGraph, 1], ['Woodland Whisper', createWoodlandWhisperGraph, 1], ['Le Militare', createLeMilitareGraph, 2],
] as const) test(`${name} strip edits preserve source and update every wrapped strip and shared stop domain`, () => {
    const graph = create() as ModelGraph;
    graph.reviewerMetadata = {retained: true};
    const sources = reelSources(graph);
    expect(sources).toHaveLength(groups);
    const source = sources[0];
    const stop = source.stops[0];
    graph.mechanics![stop.scope].nodes.find(node => node.id === stop.nodeId)!.reviewerMetadata = {keep: 7};
    const before = structuredClone(graph);
    const strips = source.strips.map((strip, reel) => reel ? [...strip] : [...strip, strip[0]]);
    const next = changeReels(graph, source.key, strips);
    expect(graph).toEqual(before);
    const stored = (next.initialState as Record<string, string[]>)[source.stripKeys[0]];
    expect(stored).toHaveLength(strips[0].length + source.rows - 1);
    expect(stored.slice(strips[0].length)).toEqual(Array.from({length: source.rows - 1}, (_, row) => strips[0][row % strips[0].length]));
    for (const ref of source.stops) {
        const draw = next.mechanics![ref.scope].nodes.find(node => node.id === ref.nodeId)!;
        expect(draw.drawWeights).toEqual(strips[ref.reel].map((_, index) => ({
            outcomeId: String(index),
            weight: 1,
            value: 0
        })));
    }
    expect(next.reviewerMetadata).toEqual(graph.reviewerMetadata);
    expect(next.mechanics![stop.scope].nodes.find(node => node.id === stop.nodeId)!.reviewerMetadata).toEqual({keep: 7});
    expect(changeReels(next, source.key, source.strips)).toEqual(before);
    for (const other of sources.slice(1)) for (const key of other.stripKeys) expect((next.initialState as Record<string, unknown>)[key]).toEqual((graph.initialState as Record<string, unknown>)[key]);
});

test('Physical reel mapping preserves weighted stops and rejects broken wrapping and geometry-only models; bought sources have a complete condition', () => {
    const graph = createAncientDragonGraph() as ModelGraph, source = reelSources(graph)[0];
    const customized = structuredClone(graph), ref = source.stops[0];
    (customized.mechanics![ref.scope].nodes.find(node => node.id === ref.nodeId)!.drawWeights as {
        weight: number
    }[])[0].weight = 2;
    expect(reelSources(customized)).toHaveLength(1);
    expect(changeReels(customized, source.key, source.strips)).toEqual(customized);
    const broken = structuredClone(graph);
    (broken.initialState as Record<string, string[]>)[source.stripKeys[0]].push('unknown');
    expect(reelSources(broken)).toEqual([]);
    expect(reelSources(createNineLivesGraph() as ModelGraph)).toEqual([]);
    expect(reelSources(createWoodlandWhisperGraph(undefined, {buy: true}) as ModelGraph)[0].conditions).toEqual([{
        scope: 'spin-reels-triggering',
        symbol: '11',
        symbolName: 'COIN',
        atLeast: 3
    }]);
});

test('Strip transactions reject empty, unknown, oversized and changed reel count before any mutation', () => {
    const graph = createAncientDragonGraph() as ModelGraph, source = reelSources(graph)[0];
    expect(validateReels(graph, [[]])).toContain('at least one');
    expect(validateReels(graph, [['unknown']])).toContain('unknown symbol');
    expect(validateReels(graph, [Array(4097).fill('0')])).toContain('4,096');
    expect(() => changeReels(graph, source.key, source.strips.slice(1))).toThrow('reel count');
    expect(() => changeReels(graph, 'missing', source.strips)).toThrow('proven');
});
