import {type AppState, flushProjectDraftSave, useAppStore} from '../../store';
import {canonicalJson} from '../hash';
import {assertModelNumbers} from '../modelJson';
import {graphRequest} from '../graphApi';
import {peekRootProject, projectContentScope, projectEditorState} from '../projectFiles';
import type {ModelGraph} from '../modelProjection';

type EditorSnapshot = Pick<AppState, 'nodes' | 'edges' | 'tables' | 'graphTrail' | 'configName' | 'selectedNodeId' | 'verificationSource' | 'liveRtp' | 'liveProvenance'>;

export interface GraphEdit {
    projectId: string;
    origin: string;
    label: string;
    base: ModelGraph;
    candidate: ModelGraph;
    changed: boolean;
}

export interface AppliedGraphEdit {
    edit: GraphEdit;
    committedSource: string;
    before: EditorSnapshot;
    after: ModelGraph
}

export type GraphValidator = (config: ModelGraph, signal: AbortSignal) => Promise<{
    isValid: boolean;
    errors?: { message: string }[]
}>;
const conflict = () => new Error('The project or source changed. Keep this draft and reconcile before applying.');

/** Candidate generation is isolated; callers must supply their verified dependency closure. */
export function prepareGraphEdit(label: string, transform: (base: ModelGraph) => ModelGraph): GraphEdit {
    const state = useAppStore.getState(), base = peekRootProject() as ModelGraph | null;
    if (!state.projectId || !base) throw new Error('Open a private project before editing.');
    const origin = projectContentScope();
    const snapshot = structuredClone(base), candidate = transform(structuredClone(snapshot));
    assertModelNumbers(candidate);
    if (state.projectId !== useAppStore.getState().projectId || origin !== projectContentScope()) throw conflict();
    return {
        label,
        projectId: state.projectId,
        origin,
        base: snapshot,
        candidate: structuredClone(candidate),
        changed: canonicalJson(snapshot) !== canonicalJson(candidate)
    };
}

function editorSnapshot(state: AppState): EditorSnapshot {
    const {
        nodes,
        edges,
        tables,
        graphTrail,
        configName,
        selectedNodeId,
        verificationSource,
        liveRtp,
        liveProvenance
    } = state;
    return structuredClone({
        nodes,
        edges,
        tables,
        graphTrail,
        configName,
        selectedNodeId,
        verificationSource,
        liveRtp,
        liveProvenance
    });
}

function rebasedEditor(config: ModelGraph, state: AppState) {
    let current = config as Record<string, unknown>;
    const graphTrail: AppState['graphTrail'] = [];
    for (const entry of state.graphTrail) {
        const definition = (current.mechanics as Record<string, Record<string, unknown>> | undefined)?.[entry.mechanic];
        if (!definition) throw new Error('The edited model removed the open mechanic. Return to the root graph before applying.');
        graphTrail.push({graph: current, mechanic: entry.mechanic});
        const {nodes: _nodes, edges: _edges, ...shared} = current;
        void _nodes;
        void _edges;
        current = {...shared, ...definition, expressions: definition.expressions ?? {}};
    }
    const draft = projectEditorState(current);
    const positions = new Map(state.nodes.map(node => [node.id, node.position]));
    draft.nodes = draft.nodes.map(node => ({...node, position: positions.get(node.id) ?? node.position}));
    const selectedNodeId = draft.nodes.some(node => node.id === state.selectedNodeId) ? state.selectedNodeId : null;
    return {...draft, graphTrail, selectedNodeId};
}

export async function commitGraphEdit(edit: GraphEdit, signal?: AbortSignal, validate: GraphValidator = (config, cancellation) => graphRequest('validate', {config}, cancellation)): Promise<AppliedGraphEdit> {
    const current = () => useAppStore.getState().projectId === edit.projectId && projectContentScope() === edit.origin;
    if (!current() || canonicalJson(peekRootProject()) !== canonicalJson(edit.base)) throw conflict();
    let before = editorSnapshot(useAppStore.getState());
    signal?.throwIfAborted();
    if (!edit.changed) return {edit, before, after: edit.base, committedSource: canonicalJson(edit.base)};
    const candidate = structuredClone(edit.candidate);
    assertModelNumbers(candidate);
    const controller = new AbortController(),
        combined = signal ? AbortSignal.any([signal, controller.signal]) : controller.signal;
    const unsubscribe = useAppStore.subscribe(() => {
        if (!current()) controller.abort(conflict());
    });
    try {
        combined.throwIfAborted();
        const result = await validate(candidate, combined);
        combined.throwIfAborted();
        if (!current()) throw conflict();
        if (result.isValid !== true) throw new Error(result.errors?.map(error => error.message).join('; ') || 'Compiler validation failed. The current graph is unchanged.');
        before = editorSnapshot(useAppStore.getState());
        const next = rebasedEditor(candidate, useAppStore.getState());
        unsubscribe();
        useAppStore.setState({...next, verificationSource: null, liveRtp: null, liveProvenance: null});
        flushProjectDraftSave();
        const after = peekRootProject() as ModelGraph;
        return {edit, before, after, committedSource: canonicalJson(after)};
    } finally {
        unsubscribe();
    }
}

/** One undo restores the entire source/context, but never crosses a project or newer edit. */
export function undoGraphEdit(applied: AppliedGraphEdit): void {
    const state = useAppStore.getState();
    if (state.projectId !== applied.edit.projectId || canonicalJson(peekRootProject()) !== applied.committedSource) throw conflict();
    if (applied.edit.changed) {
        useAppStore.setState(applied.before);
        flushProjectDraftSave();
    }
}
