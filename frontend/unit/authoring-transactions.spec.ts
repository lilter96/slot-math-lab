import { graphStudiosCorpus } from './fixtures/graphStudiosCorpus';
import { canonicalJson } from '../src/lib/hash';
import { test, expect } from '@playwright/test';
import { useAppStore } from '../src/store';
import { loadProject, openMechanic, peekRootProject, projectEditorState } from '../src/lib/projectFiles';
import { prepareGraphEdit, commitGraphEdit, undoGraphEdit, type GraphValidator } from '../src/lib/authoring/transactions';
const graph = { schemaVersion: '1.0.0', id: 'custom', name: 'Custom source', unknown: { retained: true }, nodes: [{ id: 'call', nodeType: 'library', mechanicName: 'round' }], edges: [], initialState: { count: 0 }, mechanics: {
  round: { name: 'round', nodes: [{ id: 'draw', nodeType: 'draw', drawWeights: [{ outcomeId: '001', weight: 3, value: 1 }, { outcomeId: 'zero', weight: 1, value: 0 }] }, { id: 'sink', nodeType: 'metricsSink', winCap: 10 }], edges: [{ id: 'edge', sourceNodeId: 'draw', sourcePort: 'out', targetNodeId: 'sink', targetPort: 'in', opaque: { keep: true } }], expressions: {} },
} };
const accepted: GraphValidator = async () => ({ isValid: true });
test.beforeEach(() => { useAppStore.setState({ projectId: 'project-a', projects: {}, graphTrail: [], verificationSource: null }); loadProject(structuredClone(graph)); });
function edit() { return prepareGraphEdit('Change one weight', base => {
  (base.mechanics!.round.nodes[0].drawWeights as { weight: number }[])[0].weight = 1; return base;
}); }

test('Atomic commit keeps nested context/layout/opaque source and one undo restores the complete graph', async () => {
  expect(peekRootProject()).toEqual(graph);
  openMechanic('round');
  useAppStore.setState(state => ({ nodes: state.nodes.map(node => ({ ...node, position: { x: 42, y: 85 } })), selectedNodeId: 'draw' }));
  const before = peekRootProject(), updates: unknown[] = [];
  const prepared = edit(); const unsubscribe = useAppStore.subscribe(state => updates.push(state));
  const result = await commitGraphEdit(prepared, undefined, accepted); unsubscribe();
  expect(updates).toHaveLength(1); expect(useAppStore.getState().projectId).toBe('project-a');
  expect(useAppStore.getState().graphTrail.map(entry => entry.mechanic)).toEqual(['round']);
  expect(useAppStore.getState().nodes[0].position).toEqual({ x: 42, y: 85 }); expect(useAppStore.getState().selectedNodeId).toBe('draw');
  expect(peekRootProject()).toMatchObject({ unknown: { retained: true }, mechanics: { round: { edges: graph.mechanics.round.edges, nodes: [{ drawWeights: [{ weight: 1 }, { weight: 1 }] }, {}] } } });
  undoGraphEdit(result); expect(peekRootProject()).toEqual(before); expect(useAppStore.getState().graphTrail).toHaveLength(1);
});
test('No-op preserves identity and avoids validation/store mutation; rejected candidate leaves source unchanged', async () => {
  const before = peekRootProject(), prepared = prepareGraphEdit('No-op', base => base); let calls = 0;
  const state = useAppStore.getState(); await commitGraphEdit(prepared, undefined, async () => { calls++; return { isValid: true }; });
  expect(calls).toBe(0); expect(useAppStore.getState()).toBe(state);
  await expect(commitGraphEdit(edit(), undefined, async () => ({ isValid: false, errors: [{ message: 'Invalid candidate' }] }))).rejects.toThrow('Invalid candidate');
  expect(peekRootProject()).toEqual(before);
});
test('Late validation cannot cross identical projects, source change-and-restore or explicit cancellation', async () => {
  for (const action of ['project', 'source', 'cancel']) {
    useAppStore.setState({ projectId: 'project-a', graphTrail: [] }); loadProject(structuredClone(graph));
    const prepared = edit(), controller = new AbortController(); let release!: () => void; let signal!: AbortSignal;
    const gate = new Promise<void>(resolve => { release = resolve; });
    const pending = commitGraphEdit(prepared, controller.signal, async (_, cancellation) => { signal = cancellation; await gate; return { isValid: true }; });
    const rejected = expect(pending).rejects.toThrow();
    if (action === 'project') useAppStore.setState({ projectId: 'project-b' });
    if (action === 'source') { useAppStore.setState({ configName: 'Changed' }); useAppStore.setState({ configName: graph.name }); }
    if (action === 'cancel') controller.abort();
    expect(signal.aborted).toBe(true); release(); await rejected; expect(peekRootProject()).toEqual(graph);
  }
});
test('Undo refuses a newer edit or another project and malformed editor conversion is mutation-free', async () => {
  const applied = await commitGraphEdit(edit(), undefined, accepted);
  useAppStore.setState({ configName: 'A newer model' }); expect(() => undoGraphEdit(applied)).toThrow('source changed');
  useAppStore.setState({ projectId: 'project-b' }); expect(() => undoGraphEdit(applied)).toThrow('source changed');
  const before = useAppStore.getState(); expect(() => projectEditorState({ nodes: [{ id: 1 }], edges: [] })).toThrow('Invalid node'); expect(useAppStore.getState()).toBe(before);
});

test('All 65 corpus graphs retain executable JSON through editor conversion without touching authored fixtures', () => {
  const { graphs } = graphStudiosCorpus(); expect(graphs).toHaveLength(65);
  for (const { caseId, graph: source } of graphs) {
    const original = canonicalJson(source); useAppStore.setState({ projectId: caseId, graphTrail: [] }); loadProject(source as Record<string, unknown>);
    expect(canonicalJson(peekRootProject()), caseId).toBe(original); expect(canonicalJson(source), caseId).toBe(original);
  }
});

test('Unnamed imported graphs retain absent optional metadata rather than inventing executable fields', () => {
  const custom = { nodes: [{ id: '001', nodeType: 'draw', drawWeights: [{ outcomeId: '001', weight: 1, value: 0 }] }], edges: [], opaque: { keep: true } };
  loadProject(custom); expect(peekRootProject()).toEqual(custom);
  useAppStore.setState({ configName: 'An intentional name' }); expect(peekRootProject()).toEqual({ ...custom, name: 'An intentional name' });
});
