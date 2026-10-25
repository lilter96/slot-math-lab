import {expect, test} from '@playwright/test';
import {
    collectionRepeatCalls,
    repeatCollection,
    type CollectionRepeatSpec
} from '../src/lib/authoring/collectionRepeat';
import {sourceKey} from '../src/lib/authoring/sourceIndex';
import type {ModelGraph} from '../src/lib/modelProjection';
import {useAppStore} from '../src/store';
import {loadProject, openMechanic, peekRootProject} from '../src/lib/projectFiles';
import {commitGraphEdit, prepareGraphEdit, undoGraphEdit} from '../src/lib/authoring/transactions';
import {setBrowserIdentity} from '../src/lib/browserIdentity';

const ports = {state: {name: 'state', type: 'State'}};
const model = (): ModelGraph => ({
    schemaVersion: '1.0.0', name: 'Ordered collection control', opaque: {retained: true},
    initialState: {items: [2, 3, 5], win: 0},
    nodes: [
        {
            nodeType: 'library',
            id: 'call',
            label: 'Award one item',
            mechanicName: 'award',
            parameters: {scale: '2'},
            inputs: ports,
            outputs: ports
        },
        {nodeType: 'metricsSink', id: 'sink', winStateKey: 'win', winCap: 1000, inputs: ports}
    ],
    edges: [{
        id: 'continuation',
        sourceNodeId: 'call',
        sourcePort: 'state',
        targetNodeId: 'sink',
        targetPort: 'state',
        opaque: 42
    }],
    mechanics: {
        award: {
            name: 'award',
            description: 'Independent additive body',
            nodes: [{
                nodeType: 'modifyState',
                id: 'award',
                outputKey: 'win',
                expressionId: 'award',
                inputs: ports,
                outputs: ports
            }],
            edges: [],
            expressions: {
                award: {
                    exprType: 'binary',
                    op: 'Add',
                    left: {exprType: 'fieldAccess', target: 'state', path: ['win']},
                    right: {
                        exprType: 'binary',
                        op: 'Mul',
                        left: {exprType: 'fieldAccess', target: 'state', path: ['currentItem']},
                        right: {exprType: 'fieldAccess', target: 'state', path: ['expressions', 'scale']}
                    }
                }
            }
        }
    }
});
const spec = (): CollectionRepeatSpec => ({
    call: sourceKey(null, 'call'),
    name: 'ordered-items',
    sourceKey: 'items',
    itemKey: 'currentItem',
    indexKey: 'currentIndex',
    itemType: 'Number',
    maxItems: 3
});

test('One editable body preserves external edges, original parameters and source', () => {
    const graph = model(), original = structuredClone(graph), next = repeatCollection(graph, spec());
    expect(graph).toEqual(original);
    expect(next.edges).toEqual(original.edges);
    expect(next.mechanics!.award).toEqual(original.mechanics!.award);
    expect(next.nodes).toHaveLength(original.nodes!.length);
    expect(next.nodes![0]).toMatchObject({id: 'call', mechanicName: 'ordered-items', parameters: {}});
    const wrapper = next.mechanics!['ordered-items'];
    expect(wrapper.nodes).toHaveLength(6);
    expect(wrapper.nodes.filter(node => node.nodeType === 'library')).toEqual([{...original.nodes![0], id: 'body'}]);
    expect(wrapper.nodes.find(node => node.nodeType === 'loop')).toMatchObject({
        maxIterations: 4,
        stopConditionId: 'finished'
    });
    expect(wrapper.expressions).toMatchObject({
        snapshot: {
            exprType: 'map',
            stateKey: 'items',
            maxItems: 3,
            body: {path: ['entry']}
        }, item: {function: 'index', args: [{path: ['__repeat_ordered-items_source']}, {path: ['currentIndex']}]}
    });
    expect(next.opaque).toEqual(original.opaque);
});

test('Stored topology stays constant from empty through 4096 items and scalar or structured types', () => {
    for (const maxItems of [0, 1, 5, 25, 256, 4096]) for (const itemType of ['Number', 'String', 'Boolean', 'Array', 'Record'] as const) {
        const next = repeatCollection(model(), {...spec(), maxItems, itemType});
        expect(next.mechanics!['ordered-items'].nodes).toHaveLength(6);
        expect(next.mechanics!['ordered-items'].expressions).toMatchObject({snapshot: {maxItems, itemType}});
    }
});

for (const [name, patch] of [
    ['negative bound', {maxItems: -1}], ['fractional bound', {maxItems: 1.5}], ['unbounded', {maxItems: 4097}],
    ['NaN bound', {maxItems: NaN}], ['aliased item/index', {indexKey: 'currentItem'}], ['aliased source/item', {itemKey: 'items'}],
    ['service key', {indexKey: '__iter_each__'}], ['whitespace key', {itemKey: ' item'}], ['existing definition', {name: 'award'}],
    ['missing call', {call: sourceKey(null, 'absent')}], ['invalid item type', {itemType: 'Unknown'}]
] as [string, Partial<CollectionRepeatSpec>][]) test(`Invalid ${name} leaves the model unchanged`, () => {
    const graph = model(), before = structuredClone(graph);
    expect(() => repeatCollection(graph, {...spec(), ...patch})).toThrow();
    expect(graph).toEqual(before);
});

test('Snapshot collisions in opaque data and non-State boundaries are rejected', () => {
    const graph = model();
    graph.opaque = {hidden: '__repeat_ordered-items_source'};
    expect(() => repeatCollection(graph, spec())).toThrow('snapshot key');
    graph.opaque = {};
    graph.nodes![0].outputs = {out: {name: 'out', type: 'Number'}};
    expect(collectionRepeatCalls(graph)).toEqual([]);
    expect(() => repeatCollection(graph, spec())).toThrow('one State');
});

test('Nested effects include DrawMany, board draws, missing bodies and unknown atoms', () => {
    for (const node of [
        {nodeType: 'modifyState', outputKey: 'currentItem'}, {nodeType: 'drawMany', outputKey: 'currentIndex'},
        {nodeType: 'draw', stateWriteKey: 'currentItem'}, {nodeType: 'draw', boardStateKey: 'currentItem'},
        {nodeType: 'data', stateKey: 'currentIndex'}, {nodeType: 'putState', stateKey: 'currentItem'},
        {nodeType: 'modifyState', outputKey: '__iter_call/each__'},
        {nodeType: 'transform'}, {nodeType: 'future-unknown'}
    ]) {
        const graph = model();
        graph.mechanics!.award.nodes = [{nodeType: 'library', id: 'child', mechanicName: 'child'}];
        graph.mechanics!.child = {nodes: [{...node, id: 'write'}]};
        expect(() => repeatCollection(graph, spec())).toThrow();
    }
    const graph = model();
    graph.mechanics!.award.nodes = [{nodeType: 'library', id: 'child', mechanicName: 'missing'}];
    expect(() => repeatCollection(graph, spec())).toThrow('missing subgraph');
    graph.mechanics!.award.nodes[0].mechanicName = 'award';
    expect(() => repeatCollection(graph, spec())).toThrow('recursive');
});

test('Nested repeats require distinct bindings and keep the outer body once', () => {
    const graph = repeatCollection(model(), spec());
    const inner = {
        call: sourceKey('ordered-items', 'body'),
        name: 'inner-items',
        sourceKey: 'innerItems',
        itemKey: 'innerItem',
        indexKey: 'innerIndex',
        itemType: 'Number' as const,
        maxItems: 2
    };
    const next = repeatCollection(graph, inner);
    expect(next.mechanics!['ordered-items'].nodes.find(node => node.id === 'body')).toMatchObject({mechanicName: 'inner-items'});
    expect(next.mechanics!['inner-items'].nodes.filter(node => node.nodeType === 'library')).toHaveLength(1);
    expect(() => repeatCollection(next, {
        ...spec(),
        name: 'conflict',
        itemKey: 'innerItem'
    })).toThrow('distinct bindings');
});

test('Validation commits one repeat in an open shared definition and undo restores context', async () => {
    setBrowserIdentity({authenticated: false, required: false});
    const graph = model();
    graph.mechanics!.round = {nodes: [structuredClone(graph.nodes![0])], edges: []};
    graph.nodes![0].mechanicName = 'round';
    graph.nodes![0].parameters = {};
    useAppStore.setState({projectId: 'repeat-project', projects: {}, graphTrail: [], verificationSource: null});
    loadProject(graph as Record<string, unknown>);
    openMechanic('round');
    const before = peekRootProject();
    const edit = prepareGraphEdit('Repeat over collection', base => repeatCollection(base, {
        ...spec(),
        call: sourceKey('round', 'call')
    }));
    const applied = await commitGraphEdit(edit, undefined, async candidate => {
        expect(candidate.mechanics!.round.nodes[0].mechanicName).toBe('ordered-items');
        expect(candidate.mechanics!.award).toEqual(graph.mechanics!.award);
        return {isValid: true};
    });
    expect(useAppStore.getState().graphTrail.map(entry => entry.mechanic)).toEqual(['round']);
    expect(peekRootProject()!.mechanics!['ordered-items'].nodes).toHaveLength(6);
    undoGraphEdit(applied);
    expect(peekRootProject()).toEqual(before);
    expect(useAppStore.getState().graphTrail.map(entry => entry.mechanic)).toEqual(['round']);
});


test('Binding types are explicit without changing existing schema annotations or accepting a conflicting declaration', () => {
    const graph = model();
    graph.stateSchema = [{name: 'currentItem', type: 'number', annotation: 'Retain this'}, {
        name: 'win',
        type: 'number'
    }];
    const next = repeatCollection(graph, spec());
    expect(next.stateSchema).toEqual([...graph.stateSchema as unknown[], {name: 'currentIndex', type: 'number'}]);
    expect(() => repeatCollection(graph, {
        ...spec(),
        itemType: 'Boolean'
    })).toThrow('conflicts with an authored state type');
});
