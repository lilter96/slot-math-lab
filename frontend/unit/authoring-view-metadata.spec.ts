import {expect, test} from '@playwright/test';
import {activateProject, captureProjectDraft, useAppStore} from '../src/store';
import {peekRootProject, projectContentScope, switchProject} from '../src/lib/projectFiles';
import {importProjectFile, parseProjectImport, projectEnvelope} from '../src/lib/authoring/projectEnvelope';
import {type AuthoringViews, parseAuthoringViews} from '../src/lib/authoring/viewMetadata';

const graph = {
    schemaVersion: '1.0.0',
    name: 'Metadata proof',
    nodes: [{id: '001', nodeType: 'draw', drawWeights: [{outcomeId: '001', weight: 1, value: 0}]}],
    edges: [],
    opaque: {keep: true}
};
const views: AuthoringViews = {
    version: 1,
    studio: {tab: 'sources', selectedSource: '[null,"001"]', pinnedSources: ['[null,"001"]']},
    machine: {positions: {stateA: {x: 50, y: 60}}, viewport: {x: 0, y: 0, zoom: 1}, expandedSources: []},
    symbolAssets: {'001': '/assets/private-symbol.png'}
};
test.beforeEach(() => useAppStore.setState({
    projects: {},
    authoringDraft: null,
    graphTrail: [],
    authoringViews: null,
    projectId: null,
    nodes: [],
    edges: []
}));

test('Versioned envelope contains exactly one executable graph and preserves non-executable metadata through import/export', () => {
    switchProject(graph, views);
    const before = projectContentScope();
    const exported = projectEnvelope();
    expect(exported.graph).toEqual(graph);
    expect(exported.views).toEqual(views);
    useAppStore.setState({authoringViews: {...views, studio: {...views.studio!, tab: 'paytables'}}});
    expect(projectContentScope()).toBe(before);
    expect(peekRootProject()).toEqual(graph);
    const parsed = parseProjectImport(JSON.stringify(exported));
    expect(parsed).toEqual({graph, views});
    importProjectFile(JSON.stringify(exported));
    expect(projectEnvelope()).toEqual(exported);
    expect(parseProjectImport(JSON.stringify(graph))).toEqual({graph, views: null});
    const custom = {...graph, format: 'an-authored-extension', views: {unrelated: true}};
    expect(parseProjectImport(JSON.stringify(custom))).toEqual({graph: custom, views: null});
});
test('Independent projects retain their own layout and pins while a raw graph import starts without view metadata', () => {
    switchProject(graph, views);
    const first = useAppStore.getState().projectId!, snapshot = captureProjectDraft();
    expect(snapshot.authoringViews).toEqual(views);
    switchProject(graph);
    const second = useAppStore.getState().projectId!;
    expect(second).not.toBe(first);
    expect(useAppStore.getState().authoringViews).toBeNull();
    activateProject(first);
    expect(useAppStore.getState().authoringViews).toEqual(views);
    expect(peekRootProject()).toEqual(graph);
    activateProject(second);
    expect(useAppStore.getState().authoringViews).toBeNull();
});
test('Unknown metadata, numerical math copies and malformed imports are refused before replacing a private project', () => {
    switchProject(graph, views);
    const before = useAppStore.getState();
    for (const value of [{...views, version: 2}, {...views, weights: [1, 2]}, {
        ...views,
        machine: {...views.machine, transitions: []}
    }, {
        ...views,
        studio: {...views.studio, payouts: [1]}
    }]) expect(() => parseAuthoringViews(value)).toThrow('metadata');
    for (const value of [{format: 'slotmath-project-v2', graph, views}, {
        format: 'slotmath-project-v1',
        graph: {nodes: [{}], edges: []},
        views
    }, {
        format: 'slotmath-project-v1',
        graph,
        views: {...views, version: 2}
    }]) expect(() => importProjectFile(JSON.stringify(value))).toThrow();
    expect(useAppStore.getState()).toBe(before);
    expect(projectEnvelope().graph).toEqual(graph);
});
