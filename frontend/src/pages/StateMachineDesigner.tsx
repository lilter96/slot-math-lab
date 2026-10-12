import {useEffect, useMemo, useRef, useState} from 'react';
import {Link, useNavigate, useSearchParams} from 'react-router-dom';
import {
    Background,
    BaseEdge,
    Controls,
    type Edge,
    type EdgeProps,
    getSmoothStepPath,
    Handle,
    MarkerType,
    type Node,
    type NodeProps,
    Position,
    ReactFlow,
    type ReactFlowInstance
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import {flushProjectDraftSave, useAppStore} from '../store';
import {useModelSnapshot} from '../hooks/useModelProjection';
import {canonicalJson, configHash} from '../lib/hash';
import {browserStorageKey} from '../lib/browserIdentity';
import {downloadJson, graphRequest, type GraphRound} from '../lib/graphApi';
import {openMechanic, rootProject} from '../lib/projectFiles';
import type {ModelGraph} from '../lib/modelProjection';
import {parseModelJson} from '../lib/modelJson';
import {reelObject} from '../lib/reelCommands';
import type {Expr} from '../games/shared/graph/expr';
import {controlScopes, controlSource, expressionLabel, projectControl} from '../lib/authoring/controlProjection';
import {changeControlSource, type ControlEdit} from '../lib/authoring/controlEdits';
import {indexGraphSources} from '../lib/authoring/sourceIndex';
import {readMachineSource} from '../lib/authoring/machineSource';
import {connectMachine, copyMachineUse, createMachine, updateMachine} from '../lib/authoring/machineLifecycle';
import {isMachineInput, type MachineIR, validateMachine} from '../lib/authoring/machineIR';
import {changeRespinCount, respinContracts} from '../lib/authoring/respinContract';
import {copySourceUse, type SourceConnection} from '../lib/authoring/sourceLifecycle';
import {type AppliedGraphEdit, commitGraphEdit, prepareGraphEdit, undoGraphEdit} from '../lib/authoring/transactions';
import {projectEnvelope} from '../lib/authoring/projectEnvelope';
import {ExpressionInput, MachineStateForm} from '../components/authoring/MachineForms';
import './reel-paytable-studio.css';
import './state-machine-designer.css';

const newIdentity = (prefix: string) => `${prefix}-${crypto.randomUUID().slice(0, 8)}`;
type Draft = {
    version: 1;
    projectId: string;
    baseHash: string;
    scope: string | null;
    edits: ControlEdit[];
    machine?: MachineIR;
    fresh?: boolean;
    connection: SourceConnection;
    payoutKey?: string;
    respins?: number;
    copy?: { nodeKey: string; name: string; kind?: 'machine' | 'source' };
    reuse?: string;
    capPolicy: 'keep' | 'recompute'
};
const storageKey = (id: string) => browserStorageKey(`slotmath-machine-draft-v1:${id}`);
const errorText = (value: unknown) => value instanceof Error ? value.message : 'The operation could not be completed.';

function readDraft(id: string): { draft: Draft | null; error: string | null } {
    try {
        const raw = localStorage.getItem(storageKey(id));
        if (!raw) return {draft: null, error: null};
        const value = parseModelJson(raw) as unknown as Draft;
        if (value.version !== 1 || value.projectId !== id || !/^[a-f0-9]{64}$/.test(value.baseHash) || !Array.isArray(value.edits) || value.machine !== undefined && !isMachineInput(value.machine) || !value.connection || !['keep', 'recompute'].includes(value.capPolicy) || value.scope !== null && typeof value.scope !== 'string') throw new Error('The saved machine draft has an unsupported format. Download it for recovery or discard it.');
        return {draft: value, error: null};
    } catch (error) {
        return {draft: null, error: errorText(error)};
    }
}

function transform(graph: ModelGraph, draft: Draft): ModelGraph {
    if (draft.copy) return draft.copy.kind === 'source' ? copySourceUse(graph, draft.copy.nodeKey, draft.copy.name) : copyMachineUse(graph, draft.copy.nodeKey, draft.copy.name);
    if (draft.reuse) return connectMachine(graph, draft.reuse, draft.connection, draft.payoutKey);
    if (draft.machine) {
        if (draft.fresh) return createMachine(graph, draft.machine, draft.connection, draft.payoutKey);
        const binding = draft.scope ? readMachineSource(graph, draft.scope) : null;
        if (!binding) throw new Error('The authored machine no longer matches its source. Inspect the original graph.');
        return updateMachine(graph, binding, draft.machine);
    }
    const linked = respinContracts(graph);
    if (draft.edits.some(edit => edit.kind === 'expression' && linked.some(binding => edit.nodeKey === JSON.stringify([binding.scope, binding.start]) || edit.nodeKey === JSON.stringify([binding.body, binding.reset])))) throw new Error('Use the linked respin rule to change initialization, reset and the proven iteration bound together.');
    let next = changeControlSource(graph, draft.scope, projectControl(graph, draft.scope).revision, draft.edits, draft.capPolicy);
    if (draft.respins !== undefined) {
        const binding = respinContracts(graph).find(binding => binding.scope === draft.scope || binding.body === draft.scope);
        if (!binding) throw new Error('The linked respin counter contract is unavailable.');
        next = changeRespinCount(next, respinContracts(next).find(value => value.scope === binding.scope)!, draft.respins);
    }
    return next;
}

type CanvasData = {
    title: string;
    kind: string;
    summary: string;
    entry: boolean;
    finish: boolean;
    nested: boolean;
    outgoing?: string[]
};

function StateNode({data, selected}: NodeProps<Node<CanvasData>>) {
    return <div className={`machine-canvas-node${selected ? ' selected' : ''} ${data.kind}`}><Handle type="target"
                                                                                                     id="state"
                                                                                                     position={Position.Top}/><Handle
        type="target" id="side" position={Position.Left}/>{data.kind === 'loop' &&
        <Handle type="target" id="return" position={Position.Right} style={{top: '70%'}}/>}
        <div
            className="machine-node-role">{data.entry ? 'Start' : data.finish ? 'Finish boundary' : data.kind === 'decision' ? 'Decision' : data.kind === 'loop' ? 'Repeat' : data.kind === 'operation' ? 'Custom graph operation' : 'State'}{data.nested &&
            <span>Nested feature</span>}</div>
        <strong>{data.title}</strong><p>{data.summary}</p>{data.outgoing?.length ? data.outgoing.map((key, index) =>
                <Handle key={key} type="source" id={key} position={Position.Bottom}
                        style={{left: `${(index + 1) * 100 / (data.outgoing!.length + 1)}%`}}/>) :
            <Handle type="source" id={data.kind === 'decision' ? 'false' : data.kind === 'loop' ? 'exit' : 'state'}
                    position={Position.Bottom}/>}{['decision', 'loop'].includes(data.kind) &&
            <Handle type="source" id={data.kind === 'decision' ? 'true' : 'body'} position={Position.Right}
                    style={{top: '35%'}}/>}</div>;
}

const nodeTypes = {state: StateNode};

function SelfTransition({sourceX, sourceY, targetX, targetY, markerEnd, style, label, labelStyle}: EdgeProps) {
    const path = `M ${sourceX},${sourceY} C ${sourceX + 360},${sourceY + 90} ${targetX + 360},${targetY - 90} ${targetX},${targetY}`;
    return <BaseEdge path={path} markerEnd={markerEnd} style={style} label={label} labelX={sourceX + 270}
                     labelY={(sourceY + targetY) / 2 - 8} labelStyle={labelStyle} labelBgStyle={{fill: '#f6f8fc'}}
                     labelBgPadding={[7, 5]}/>;
}

function GuardTransition(props: EdgeProps) {
    const [path] = getSmoothStepPath(props), rank = Number(props.data?.rank ?? 0),
        count = Number(props.data?.count ?? 1);
    const labelY = props.data?.return ? props.sourceY + 32 : props.data?.side ? props.sourceY - 75 : props.sourceY + (props.targetY > props.sourceY ? (props.targetY - props.sourceY) * (rank + 1) / (count + 1) : 25 + rank * 24);
    return <BaseEdge path={path} markerEnd={props.markerEnd} style={props.style} label={props.label}
                     labelX={(props.sourceX + props.targetX) / 2} labelY={labelY} labelStyle={props.labelStyle}
                     labelBgStyle={{fill: '#f6f8fc'}} labelBgPadding={[7, 5]}/>;
}

const edgeTypes = {self: SelfTransition, guard: GuardTransition};

/** Arrange the existing main path and side branches; view coordinates never become executable edges. */
function regionLayout(items: { key: string; entry: boolean; kind: string }[], edges: {
    source: string;
    target: string;
    virtual: boolean;
    port?: string
}[]) {
    const points: Record<string, { x: number; y: number }> = {}, branches: { key: string; x: number; y: number }[] = [];
    const walk = (entry: string, x: number, top: number) => {
        let current: string | undefined = entry, y = top;
        while (current && !points[current]) {
            points[current] = {x, y};
            const item = items.find(item => item.key === current),
                outgoing = edges.filter(edge => edge.source === current && !edge.virtual);
            const primary = outgoing.find(edge => item?.kind === 'loop' ? edge.port === 'exit' : item?.kind === 'decision' ? edge.port === 'false' : edge.port === 'state') ?? outgoing[0];
            for (const edge of outgoing.filter(edge => edge !== primary)) branches.push({
                key: edge.target,
                x: x + 340,
                y: y + 35
            });
            current = primary?.target;
            y += 235;
        }
    };
    const entry = items.find(item => item.entry)?.key ?? items[0]?.key;
    if (entry) walk(entry, 50, 45);
    while (branches.length) {
        const branch = branches.shift()!;
        if (!points[branch.key]) walk(branch.key, branch.x, branch.y);
    }
    let extra = Math.max(45, ...Object.values(points).map(point => point.y)) + 235;
    for (const item of items) if (!points[item.key]) {
        walk(item.key, 50, extra);
        extra = Math.max(...Object.values(points).map(point => point.y)) + 235;
    }
    return points;
}

export default function StateMachineDesigner() {
    const projectId = useAppStore(state => state.projectId), snapshot = useModelSnapshot();
    if (!projectId || !snapshot.config || snapshot.error) return <section className="graph-studio studio-unavailable">
        <h1>State Machine Designer</h1>
        <p>{snapshot.error ?? 'Choose a private project to inspect or author its gameplay lifecycle.'}</p><Link
        className="studio-primary"
        to={snapshot.error ? '/build' : '/projects'}>{snapshot.error ? 'Repair source graph' : 'Choose a project'}</Link>
    </section>;
    return <MachineWorkspace key={projectId} projectId={projectId} graph={snapshot.config as ModelGraph}/>;
}

function MachineWorkspace({projectId, graph}: { projectId: string; graph: ModelGraph }) {
    const navigate = useNavigate(), [params, setParams] = useSearchParams(),
        views = useAppStore(state => state.authoringViews), canonical = canonicalJson(graph);
    const [restored] = useState(() => readDraft(projectId)), [draft, setDraft] = useState<Draft | null>(restored.draft),
        draftRef = useRef<Draft | null>(restored.draft);
    const [digest, setDigest] = useState<{ source: string; hash: string } | null>(null),
        hash = digest?.source === canonical ? digest.hash : null;
    const flow = useRef<ReactFlowInstance<Node<CanvasData>, Edge> | null>(null), [selectedTransition, setSelectedTransition] = useState<string | null>(null);
    const [selected, setSelected] = useState<string | null>(null), [search, setSearch] = useState(''), [mode, setMode] = useState<'diagram' | 'table'>('diagram'), [mobileExplorer, setMobileExplorer] = useState(false);
    const [error, setError] = useState<string | null>(restored.error), [storageError, setStorageError] = useState<string | null>(null), [recoveryBlocked, setRecoveryBlocked] = useState(!!restored.error), [busy, setBusy] = useState<'apply' | 'play' | null>(null), [applied, setApplied] = useState<AppliedGraphEdit | null>(null), [round, setRound] = useState<{
        hash: string;
        value: GraphRound
    } | null>(null), [seed, setSeed] = useState('42');
    const controller = useRef<AbortController | null>(null), inspector = useRef<HTMLElement | null>(null),
        creation = useRef<HTMLDialogElement | null>(null), [createOpen, setCreateOpen] = useState(false), [createKind, setCreateKind] = useState<'new' | 'reuse'>('new'), [name, setName] = useState('new-feature'), [reused, setReused] = useState(''), [connection, setConnection] = useState('definition'), [payoutKey, setPayoutKey] = useState('coins');
    const [positions, setPositions] = useState<Record<string, {
        x: number;
        y: number
    }>>(() => views?.machine?.positions ?? {});
    const scopes = useMemo(() => {
            const hidden = new Set(Object.keys(graph.mechanics ?? {}).flatMap(name => readMachineSource(graph, name)?.scopes.filter(scope => scope !== name) ?? []));
            return controlScopes(graph).filter(value => value.key === null || !hidden.has(value.key));
        }, [graph]), scopeQuery = params.get('scope'),
        scope = draft ? draft.scope : scopeQuery && Object.hasOwn(graph.mechanics ?? {}, scopeQuery) ? scopeQuery : null;
    const projected = useMemo(() => projectControl(graph, scope), [graph, scope]),
        binding = useMemo(() => scope ? readMachineSource(graph, scope) : null, [graph, scope]),
        machine = draft?.machine ?? binding?.machine;
    const sources = useMemo(() => indexGraphSources(graph), [graph]),
        uses = sources.libraryUses.filter(use => use.definition === scope),
        respin = useMemo(() => respinContracts(graph).find(value => value.scope === scope || value.body === scope), [graph, scope]);
    const conflict = !!draft && !!hash && draft.baseHash !== hash,
        disabled = !hash || !!busy || conflict || recoveryBlocked;
    useEffect(() => {
        let active = true;
        controller.current?.abort();
        void configHash(JSON.parse(canonical)).then(value => {
            if (active) setDigest({source: canonical, hash: value});
        });
        return () => {
            active = false;
            controller.current?.abort();
        };
    }, [canonical]);
    useEffect(() => {
        if (createOpen) creation.current?.showModal(); else creation.current?.close();
    }, [createOpen]);
    const persist = (next: Draft) => {
        draftRef.current = next;
        setDraft(next);
        setError(null);
        try {
            localStorage.setItem(storageKey(projectId), JSON.stringify(next));
            setStorageError(null);
        } catch {
            setStorageError('Pending inputs could not be saved in this browser. Keep this page open and download the draft.');
        }
    };
    const baseDraft = (): Draft => draftRef.current ?? {
        version: 1,
        projectId,
        baseHash: hash!,
        scope,
        edits: [],
        connection: {kind: 'definition'},
        capPolicy: 'keep'
    };
    const stageMachine = (machine: MachineIR) => {
        if (!disabled) persist({...baseDraft(), machine});
    };
    const stageEdit = (edit: ControlEdit) => {
        if (disabled) return;
        const key = (value: ControlEdit) => `${value.nodeKey}:${value.kind === 'expression' ? value.field : value.kind}`,
            previous = baseDraft();
        persist({...previous, edits: [...previous.edits.filter(value => key(value) !== key(edit)), edit]});
    };
    const discard = () => {
        try {
            localStorage.removeItem(storageKey(projectId));
            setStorageError(null);
            setRecoveryBlocked(false);
        } catch {
            setStorageError('The stored draft could not be removed. Download it before leaving.');
        }
        draftRef.current = null;
        setDraft(null);
        setError(null);
    };
    const candidate = useMemo(() => {
        try {
            const value = draft ? transform(graph, draft) : graph;
            return {graph: value, error: null, changed: canonicalJson(value) !== canonical};
        } catch (error) {
            return {graph: null, error: errorText(error), changed: true};
        }
    }, [graph, draft, canonical]);
    let breadcrumb: string[];
    try {
        const value = JSON.parse(params.get('path') ?? '[]');
        breadcrumb = Array.isArray(value) && value.every(name => typeof name === 'string' && graph.mechanics?.[name]) && value.at(-1) === scope ? value : scope ? [scope] : [];
    } catch {
        breadcrumb = scope ? [scope] : [];
    }
    const chooseScope = (next: string | null, nested = false) => {
        if (draft) {
            setError('Apply or discard pending changes before opening another feature.');
            return;
        }
        const path = next ? nested ? [...breadcrumb, next] : [next] : [];
        setParams(previous => {
            const value = new URLSearchParams(previous);
            if (next) {
                value.set('scope', next);
                value.set('path', JSON.stringify(path));
            } else {
                value.delete('scope');
                value.delete('path');
            }
            return value;
        });
        setSelected(null);
        setMobileExplorer(false);
        useAppStore.setState(state => ({
            authoringViews: {
                ...state.authoringViews ?? {version: 1},
                machine: {
                    positions: state.authoringViews?.machine?.positions ?? {},
                    viewport: (Object.hasOwn(state.authoringViews?.machine?.viewports ?? {}, next ?? 'root') ? state.authoringViews!.machine!.viewports![next ?? 'root'] : {
                        x: 20,
                        y: 30,
                        zoom: 0.9
                    }),
                    viewports: state.authoringViews?.machine?.viewports ?? {},
                    expandedSources: path
                }
            }
        }));
        flushProjectDraftSave();
    };
    const openGraph = (id?: string) => {
        try {
            rootProject();
            if (scope && graph.mechanics?.[scope]) openMechanic(scope);
            if (id) useAppStore.getState().selectNode(id);
            navigate('/build');
        } catch (error) {
            setError(errorText(error));
        }
    };
    const select = (key: string, focus = false) => {
        setSelected(key);
        setSelectedTransition(null);
        if (focus && mode === 'diagram') void flow.current?.fitView({
            nodes: [{id: key}],
            minZoom: 0.85,
            maxZoom: 1,
            duration: matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : 180
        });
        if (focus) requestAnimationFrame(() => {
            if (window.innerWidth < 1250) inspector.current?.scrollIntoView({block: 'start', behavior: 'smooth'});
            inspector.current?.focus({preventScroll: true});
        });
    };
    const display = machine ? machine.states.map(state => ({
        key: `machine:${machine.instance}:${state.id}`,
        stateId: state.id,
        title: state.label,
        kind: 'state',
        summary: state.actions.length ? state.actions.map(action => action.label).join(' → ') : 'No entry actions',
        entry: state.id === machine.entry,
        finish: state.terminal,
        nested: state.actions.some(action => action.kind === 'library')
    })) : projected.regions.map(region => ({
        key: region.key,
        stateId: null,
        title: region.label,
        kind: region.kind,
        summary: region.actions.map(action => action.node.nodeType === 'library' ? String(action.node.mechanicName) : action.node.nodeType === 'modifyState' ? `Set ${String(action.node.outputKey)}` : action.node.nodeType === 'loop' ? `Up to ${String(action.node.maxIterations)} passes` : action.node.nodeType === 'branch' ? expressionLabel(reelObject(controlSource(graph, scope).expressions)?.[String(action.node.conditionId)]) : action.node.nodeType).join(' → '),
        entry: region.entry,
        finish: region.boundary,
        nested: !!region.nested
    }));
    const transitions = machine ? machine.states.flatMap(state => state.transitions.map((transition, priority) => ({
        key: `${state.id}:${transition.id}`,
        source: `machine:${machine.instance}:${state.id}`,
        target: `machine:${machine.instance}:${transition.target}`,
        label: `${priority + 1}. ${transition.guard === null ? 'Otherwise' : expressionLabel(transition.guard)}`,
        virtual: false
    }))) : projected.transitions;
    const visitSpacing = Math.max(235, 180 + Math.max(0, ...machine?.states.map(state => state.transitions.length) ?? []) * 24);
    const arranged = machine ? Object.fromEntries(display.map((value, index) => [value.key, {
        x: 50,
        y: index * visitSpacing + 45
    }])) : regionLayout(display, transitions);
    const defaults = (index: number) => arranged[display[index].key];
    const nodes: Node<CanvasData>[] = display.map((item, index) => ({
        id: item.key,
        type: 'state',
        position: positions[item.key] ?? defaults(index),
        selected: selected === item.key,
        data: {
            title: item.title,
            kind: item.kind,
            summary: item.summary,
            entry: item.entry,
            finish: item.finish,
            nested: item.nested,
            outgoing: machine ? transitions.filter(value => value.source === item.key).map(value => value.key) : undefined
        }
    }));
    const edges: Edge[] = transitions.map(value => ({
        id: value.key,
        source: value.source,
        target: value.target,
        targetHandle: value.virtual ? 'return' : 'port' in value && ['body', 'true'].includes(String(value.port)) ? 'side' : 'state',
        sourceHandle: machine ? value.key : ['decision', 'loop'].includes(display.find(item => item.key === value.source)?.kind ?? '') && 'port' in value ? String(value.port) : 'state',
        label: value.virtual ? 'Return' : 'port' in value && value.port === 'body' ? 'Body' : value.label.length > 95 ? value.label.slice(0, 92) + '…' : value.label,
        type: value.source === value.target ? 'self' : machine || value.virtual || 'port' in value && value.port === 'true' ? 'guard' : 'smoothstep',
        data: {
            rank: transitions.filter(edge => edge.source === value.source).findIndex(edge => edge.key === value.key),
            count: transitions.filter(edge => edge.source === value.source).length,
            side: !machine && 'port' in value && value.port === 'true',
            return: value.virtual
        },
        markerEnd: {type: MarkerType.ArrowClosed, color: '#7693bb'},
        style: {
            stroke: value.virtual ? '#a5b4c9' : '#7693bb',
            strokeWidth: 1.7,
            strokeDasharray: value.virtual ? '6 5' : undefined
        },
        labelStyle: {fontSize: 14, fill: '#334766'},
        labelBgStyle: {fill: '#f6f8fc', fillOpacity: 1},
        labelBgPadding: [6, 4]
    }));
    const selectedItem = display.find(item => item.key === selected),
        selectedState = machine?.states.find(state => state.id === selectedItem?.stateId),
        selectedRegion = projected.regions.find(region => region.key === selected);
    const apply = async () => {
        if (!draft || disabled || !candidate.graph || candidate.error || !candidate.changed) return;
        const cancellation = new AbortController();
        controller.current = cancellation;
        setBusy('apply');
        setError(null);
        try {
            const edit = prepareGraphEdit('State machine inputs', graph => transform(graph, draft));
            const result = await commitGraphEdit(edit, cancellation.signal);
            setApplied(result);
            setRound(null);
            const nextScope = draft.machine?.instance ?? draft.reuse ?? draft.copy?.name ?? scope;
            discard();
            setParams(previous => {
                const value = new URLSearchParams(previous);
                if (nextScope) value.set('scope', nextScope);
                return value;
            });
        } catch (error) {
            if (!cancellation.signal.aborted) setError(errorText(error));
        } finally {
            if (controller.current === cancellation) {
                controller.current = null;
                setBusy(null);
            }
        }
    };
    const play = async () => {
        if (!hash || draft || !/^-?\d+$/.test(seed) || Number(seed) < -2147483648 || Number(seed) > 2147483647) {
            setError('Apply or discard inputs and use a signed 32-bit whole seed.');
            return;
        }
        const cancellation = new AbortController();
        controller.current = cancellation;
        setBusy('play');
        setError(null);
        try {
            const value = await graphRequest<GraphRound>('play/round', {
                config: graph,
                seed: Number(seed),
                roundIndex: 0
            }, cancellation.signal);
            if (!cancellation.signal.aborted) setRound({hash, value});
        } catch (error) {
            if (!cancellation.signal.aborted) setError(errorText(error));
        } finally {
            if (controller.current === cancellation) {
                controller.current = null;
                setBusy(null);
            }
        }
    };
    const saveView = (update: {
        positions?: Record<string, { x: number; y: number }>;
        viewport?: { x: number; y: number; zoom: number }
    }) => {
        useAppStore.setState(state => ({
            authoringViews: {
                ...state.authoringViews ?? {version: 1},
                machine: {
                    positions: state.authoringViews?.machine?.positions ?? {},
                    viewport: state.authoringViews?.machine?.viewport ?? {x: 0, y: 0, zoom: 1},
                    expandedSources: state.authoringViews?.machine?.expandedSources ?? [],
                    viewports: {...state.authoringViews?.machine?.viewports, ...(update.viewport ? {[scope ?? 'root']: update.viewport} : {})}, ...update
                }
            }
        }));
        flushProjectDraftSave();
    };
    const edgeOptions = sources.nodes.flatMap(source => {
        const owner = controlSource(graph, source.scope),
            outgoing = Array.isArray(owner.edges) ? owner.edges as Record<string, unknown>[] : [];
        return outgoing.filter(edge => edge.sourceNodeId === source.id && reelObject(reelObject(source.node.outputs)?.[String(edge.sourcePort)])?.type === 'State').map(edge => ({
            label: `${source.scope ?? 'Current round'} / ${String(source.node.label ?? source.id)} → ${String(edge.targetNodeId)}`,
            value: JSON.stringify({kind: 'edge', scope: source.scope, edgeId: edge.id})
        }));
    });
    const connections = [{
        label: 'Save reusable definition · does not execute yet',
        value: 'definition'
    }, ...edgeOptions, ...sources.libraryUses.map(use => ({
        label: `Replace use: ${use.source.scope ?? 'Current round'} / ${String(use.source.node.label ?? use.source.id)}`,
        value: JSON.stringify({kind: 'replace', nodeKey: use.source.key})
    })), ...(graph.nodes?.length === 1 && graph.nodes[0].nodeType === 'metricsSink' && graph.nodes[0].winStateKey === undefined ? [{
        label: 'Connect to this blank round',
        value: 'empty-round'
    }] : [])];
    const startNew = () => {
        if (disabled) return;
        try {
            const selectedConnection: SourceConnection = connection === 'definition' || connection === 'empty-round' ? {kind: connection} : JSON.parse(connection);
            if (createKind === 'reuse') {
                if (!reused) throw new Error('Choose a definition.');
                persist({...baseDraft(), reuse: reused, connection: selectedConnection, payoutKey});
            } else {
                const machine: MachineIR = {
                    version: 1,
                    instance: name,
                    name: name.replaceAll('-', ' '),
                    entry: 'start',
                    stepBudget: 3,
                    variables: [{key: 'counter', scope: 'private', initial: 0}],
                    states: [{
                        id: 'start',
                        label: 'Start feature',
                        terminal: false,
                        actions: [],
                        transitions: [{id: 'finish', label: 'Continue', target: 'finish', guard: null, effects: []}]
                    }, {id: 'finish', label: 'Finish feature', terminal: true, actions: [], transitions: []}]
                };
                if (validateMachine(machine).length) throw new Error(validateMachine(machine).join(' '));
                persist({...baseDraft(), scope: name, fresh: true, machine, connection: selectedConnection, payoutKey});
                setSelected(`machine:${name}:start`);
            }
            setCreateOpen(false);
        } catch (error) {
            setError(errorText(error));
        }
    };
    const status = busy === 'apply' ? 'Validating complete graph…' : conflict ? 'Source changed · reconcile draft' : candidate.error ? 'Draft needs attention' : draft ? 'Pending changes' : 'Current source';
    return <section className="graph-studio machine-designer" aria-labelledby="machine-title">
        <header className="studio-page-heading">
            <div><p
                className="studio-project-context">{String(graph.name ?? 'Untitled model')}<span>Private project</span>
            </p><h1 id="machine-title">State Machine Designer</h1></div>
            <div className="studio-actions">
                <button disabled={disabled || !!draft} onClick={() => setCreateOpen(true)}>Create / reuse</button>
                <button disabled={!applied || applied.committedSource !== canonical || !!draft || !!busy}
                        onClick={() => {
                            try {
                                undoGraphEdit(applied!);
                                setApplied(null);
                                setRound(null);
                                if (scope && !applied!.edit.base.mechanics?.[scope]) setParams({});
                            } catch (error) {
                                setError(errorText(error));
                            }
                        }}>Undo
                </button>
                <button className="studio-primary" disabled={disabled || !candidate.changed || !!candidate.error}
                        onClick={() => void apply()}>Validate & apply
                </button>
                <button onClick={() => openGraph()}>Open graph</button>
            </div>
        </header>
        <div className="studio-view-bar">
            <div role="tablist" aria-label="Machine views">{(['diagram', 'table'] as const).map(value => <button
                role="tab" aria-selected={mode === value} key={value}
                onClick={() => setMode(value)}>{value === 'diagram' ? 'Diagram' : 'Transition table'}</button>)}</div>
            <span
                className={'studio-status' + (conflict || candidate.error ? ' needs-attention' : draft ? ' pending' : '')}>{status}</span>
        </div>
        <div className="machine-breadcrumb">
            <button disabled={!!draft} onClick={() => chooseScope(null)}>Current round</button>
            {breadcrumb.map((name, index) => <span className="machine-breadcrumb-segment"
                                                   key={`${index}:${name}`}><span>/</span>{index === breadcrumb.length - 1 ?
                <b>{name}</b> :
                <button disabled={!!draft} onClick={() => chooseScope(name)}>{name}</button>}</span>)}<span
            className="machine-view-origin">{machine ? 'Authored machine' : 'Existing structured graph'}</span></div>
        {(error || storageError || conflict) && <div className="machine-notice"
                                                     role="alert">{error ?? storageError ?? 'The current graph changed. Download or discard this pending draft before editing the new source.'}{(draft || recoveryBlocked) &&
            <button
                onClick={() => downloadJson('pending-machine-draft.json', draft ?? localStorage.getItem(storageKey(projectId)))}>Download
                draft</button>}</div>}
        <div className="machine-workspace">
            <aside className={'machine-explorer' + (mobileExplorer ? ' mobile-open' : '')}>
                <header><h2>Feature explorer</h2><input type="search" aria-label="Search features"
                                                        placeholder="Find feature or state" value={search}
                                                        onChange={event => setSearch(event.target.value)}/></header>
                <nav
                    aria-label="Feature definitions">{scopes.filter(value => `${value.key ?? ''} ${value.label}`.toLowerCase().includes(search.toLowerCase())).map(value =>
                    <button key={value.key ?? 'root'} aria-pressed={scope === value.key} disabled={!!draft}
                            onClick={() => chooseScope(value.key)}>
                        <b>{value.key ? value.key.replaceAll('-', ' ') : value.label}</b><small>{value.key ? `${value.uses} use${value.uses === 1 ? '' : 's'}` : 'Entry and settlement'}</small>
                    </button>)}</nav>
                <section>
                    <h3>{machine ? 'Machine states' : 'Source regions'}</h3>{display.filter(value => value.title.toLowerCase().includes(search.toLowerCase())).map(value =>
                    <button key={value.key} aria-pressed={selected === value.key}
                            onClick={() => select(value.key, true)}>
                        <b>{value.title}</b><small>{value.entry ? 'Starting state' : value.finish ? 'Finish boundary' : value.kind}</small>
                    </button>)}{machine && <button disabled={disabled} onClick={() => {
                    const id = newIdentity('state');
                    stageMachine({
                        ...machine,
                        states: [...machine.states, {
                            id,
                            label: 'New state',
                            terminal: false,
                            actions: [],
                            transitions: [{
                                id: newIdentity('fallback'),
                                label: 'Continue',
                                target: machine.states.find(state => state.terminal)?.id ?? machine.entry,
                                guard: null,
                                effects: []
                            }]
                        }]
                    });
                    setSelected(`machine:${machine.instance}:${id}`);
                }}>Add state</button>}</section>
                <footer><Link to="/model/studio">Reels, weights & payouts</Link>
                    <button onClick={() => downloadJson('machine-project.json', projectEnvelope())}>Back up project
                    </button>
                </footer>
            </aside>
            <main className="machine-main">
                <div className="machine-canvas-heading">
                    <div><h2>{machine?.name ?? (scope ? scope.replaceAll('-', ' ') : 'Round lifecycle')}</h2>
                        <p>{display.length} {machine ? 'states' : 'source regions'} · {transitions.filter(value => !value.virtual).length} transitions</p>
                    </div>
                    <div className="studio-actions">
                        <button className="machine-explorer-toggle"
                                onClick={() => setMobileExplorer(value => !value)}>Features
                        </button>
                        <button onClick={() => {
                            const next = {...positions};
                            display.forEach((value, index) => {
                                next[value.key] = defaults(index);
                            });
                            setPositions(next);
                            saveView({positions: next});
                        }}>Arrange
                        </button>
                    </div>
                </div>
                {mode === 'diagram' ?
                    <div className="machine-canvas" aria-label="Machine diagram"><ReactFlow key={scope ?? 'root'}
                                                                                            nodes={nodes} edges={edges}
                                                                                            nodeTypes={nodeTypes}
                                                                                            edgeTypes={edgeTypes}
                                                                                            fitView={false}
                                                                                            defaultViewport={Object.hasOwn(views?.machine?.viewports ?? {}, scope ?? 'root') ? views!.machine!.viewports![scope ?? 'root'] : {
                                                                                                x: 20,
                                                                                                y: 30,
                                                                                                zoom: 0.9
                                                                                            }} minZoom={0.35}
                                                                                            maxZoom={1.6}
                                                                                            nodesConnectable={false}
                                                                                            onInit={instance => {
                                                                                                flow.current = instance;
                                                                                            }}
                                                                                            onNodeClick={(_, node) => select(node.id)}
                                                                                            onEdgeClick={(_, edge) => {
                                                                                                select(edge.source, true);
                                                                                                setSelectedTransition(machine?.states.flatMap(state => state.transitions.map(transition => ({
                                                                                                    key: `${state.id}:${transition.id}`,
                                                                                                    id: transition.id
                                                                                                }))).find(value => value.key === edge.id)?.id ?? null);
                                                                                            }}
                                                                                            onNodesChange={changes => {
                                                                                                const moved = changes.filter(change => change.type === 'position' && change.position);
                                                                                                if (moved.length) setPositions(previous => {
                                                                                                    const next = {...previous};
                                                                                                    for (const change of moved) if (change.type === 'position' && change.position) next[change.id] = change.position;
                                                                                                    return next;
                                                                                                });
                                                                                            }}
                                                                                            onNodeDragStop={(_, node) => saveView({
                                                                                                positions: {
                                                                                                    ...positions,
                                                                                                    [node.id]: node.position
                                                                                                }
                                                                                            })}
                                                                                            onMoveEnd={(_, viewport) => saveView({viewport})}
                                                                                            proOptions={{hideAttribution: true}}><Background
                        color="#cbd5e3" gap={22} size={1}/><Controls showInteractive={false}/></ReactFlow></div> :
                    <div className="machine-table-scroll">
                        <table className="machine-transition-table">
                            <thead>
                            <tr>
                                <th>State / actions</th>
                                <th>Order / guard</th>
                                <th>Destination</th>
                                <th>Source</th>
                            </tr>
                            </thead>
                            <tbody>{display.flatMap(value => {
                                const outgoing = transitions.filter(transition => transition.source === value.key);
                                return (outgoing.length ? outgoing : [{
                                    key: value.key,
                                    label: value.finish ? 'Return / settlement' : 'No outgoing transition',
                                    target: '',
                                    virtual: false
                                }]).map((transition, index) => <tr key={transition.key}>
                                    <th>
                                        <button onClick={() => select(value.key, true)}>{value.title}</button>
                                        <small>{value.summary}</small></th>
                                    <td>{transition.label}{transition.virtual &&
                                        <small>Display-only return</small>}</td>
                                    <td>{display.find(value => value.key === transition.target)?.title ?? 'Feature boundary'}</td>
                                    <td>
                                        <button onClick={() => {
                                            select(value.key, true);
                                            setSelectedTransition(machine?.states.flatMap(state => state.transitions.map(value => ({
                                                key: `${state.id}:${value.id}`,
                                                id: value.id
                                            }))).find(value => value.key === transition.key)?.id ?? null);
                                        }}>Edit {index === 0 ? 'state' : 'transition'}</button>
                                    </td>
                                </tr>);
                            })}</tbody>
                        </table>
                    </div>}
                <section className="machine-rule-strip">
                    <b>{machine ? 'One visit at a time' : 'Original execution order retained'}</b>
                    <p>{machine ? 'Entry actions → first true guard → effects → next state on the next visit. Terminal entry actions execute once before returning.' : 'Branches select the existing true/false paths. Repeat arrows return through the existing Loop boundary. Complex operations keep their original graph source.'}</p>
                </section>
                <section className="machine-review">
                    <header><h2>Validation & proposed changes</h2>
                        <span>{draft ? 'Pending local inputs' : 'No pending changes'}</span></header>
                    {candidate.error ? <p className="studio-error" role="alert">{candidate.error}</p> :
                        <p>{draft ? 'The complete candidate will be checked by the existing compiler before one atomic apply.' : 'Choose a state or transition to edit. Inputs stay in this private project.'}</p>}{draft && <>
                    <p>{draft.machine ? `${draft.machine.states.length} states, ${draft.machine.variables.length} variable bindings; ${Object.keys(candidate.graph?.mechanics ?? {}).length - Object.keys(graph.mechanics ?? {}).length} definitions added.` : `${draft.edits.length} mapped edits${draft.respins !== undefined ? ', linked respin counter and bound' : ''}.`}</p>
                    <div className="studio-actions">
                        <button onClick={discard} disabled={!!busy}>Discard pending changes</button>
                        <button onClick={() => downloadJson('pending-machine-draft.json', draft)}>Download draft
                        </button>
                        {busy === 'apply' &&
                            <button onClick={() => controller.current?.abort()}>Cancel validation</button>}</div>
                </>}{recoveryBlocked && <button onClick={discard}>Discard unreadable saved draft</button>}
                    <details>
                        <summary>Source, callers and execution limits</summary>
                        <p>{scope ?? 'Current round'} · {uses.length} definition uses. Definition edits affect every
                            caller listed below.</p>
                        <ul>{uses.map(use => <li
                            key={use.source.key}>{use.source.scope ?? 'Current round'} / {String(use.source.node.label ?? use.source.id)}</li>)}</ul>
                        <p>The existing outer settlement cap and price stay active. A state-step budget counts terminal
                            visits. Reaching the exact iteration limit remains modelLimit, even when the stop condition
                            also becomes true.</p>{projected.issues.map(issue => <p className="studio-error"
                                                                                    key={issue}>{issue}</p>)}
                        <button onClick={() => openGraph()}>Inspect complete source</button>
                    </details>
                </section>
                <section className="machine-execution">
                    <header>
                        <div><h2>Test the current graph</h2><p>A seeded round uses the actual execution API.</p></div>
                        <div className="studio-actions"><label>Seed<input aria-label="Machine test seed" value={seed}
                                                                          onChange={event => setSeed(event.target.value)}/></label>
                            <button disabled={!!draft || !!busy || !hash}
                                    onClick={() => void play()}>{busy === 'play' ? 'Running…' : 'Test round'}</button>
                        </div>
                    </header>
                    {round && round.hash === hash ? <><p className="machine-round-result">Round
                        payout <strong>{round.value.winNumerator}/{round.value.winDenominator}</strong><span>Seed {round.value.seed} · round {round.value.roundIndex}</span>
                    </p>
                        <details>
                            <summary>Returned state and loop evidence</summary>
                            <pre>{JSON.stringify(round.value.state, null, 2)}</pre>
                        </details>
                    </> : <p className="machine-empty-evidence">No current execution evidence. Apply pending changes
                        before testing.</p>}</section>
            </main>
            <aside ref={inspector} tabIndex={-1} className="machine-inspector" aria-label="Selected state editor">
                <header><h2>{selectedItem ? selectedItem.title : 'Select a state'}</h2>
                    <p>{selectedItem ? 'Actions, conditions and original source' : 'Click a state or transition, or use the transition table.'}</p>
                </header>
                {respin && <section className="machine-form-section machine-linked-rule"><h3>Linked respin rule</h3>
                    <p>Initialize and restore the same counter. Held cells and prize draws stay intact.</p><label>Initial
                        / reset respins<input aria-label="Initial and reset respins" type="number" min="1"
                                              value={draft?.respins ?? respin.count} disabled={disabled}
                                              onChange={event => persist({
                                                  ...baseDraft(),
                                                  respins: Number(event.target.value)
                                              })}/></label><small>Proven loop bound: ({respin.cells} cells
                        − {respin.heldAtStart} guaranteed held at entry) × {draft?.respins ?? respin.count} + 1
                        = {(respin.cells - respin.heldAtStart) * (draft?.respins ?? respin.count) + 1}</small>
                </section>}{selectedState && machine ? <><MachineStateForm key={selectedState.id} state={selectedState}
                                                                           machine={machine}
                                                                           openTransition={selectedTransition}
                                                                           definitions={Object.keys(graph.mechanics ?? {}).filter(name => name !== machine.instance && !binding?.scopes.includes(name))}
                                                                           disabled={disabled}
                                                                           change={updated => stageMachine({
                                                                               ...machine,
                                                                               states: machine.states.map(state => state.id === updated.id ? updated : state)
                                                                           })}/>
                <section className="machine-form-section"><h3>State lifecycle</h3>
                    <button disabled={disabled} onClick={() => stageMachine({...machine, entry: selectedState.id})}>Use
                        as starting state
                    </button>
                    <label>Redirect incoming transitions before removal<select id="machine-reroute" defaultValue="">
                        <option value="">Choose another state</option>
                        {machine.states.filter(state => state.id !== selectedState.id).map(state => <option
                            key={state.id} value={state.id}>{state.label}</option>)}</select></label>
                    <button disabled={disabled || machine.states.length < 2} onClick={() => {
                        const target = (document.getElementById('machine-reroute') as HTMLSelectElement).value;
                        if (!target) {
                            setError('Choose where incoming transitions should go before removing this state.');
                            return;
                        }
                        stageMachine({
                            ...machine,
                            entry: machine.entry === selectedState.id ? target : machine.entry,
                            states: machine.states.filter(state => state.id !== selectedState.id).map(state => ({
                                ...state,
                                transitions: state.transitions.map(transition => transition.target === selectedState.id ? {
                                    ...transition,
                                    target
                                } : transition)
                            }))
                        });
                        setSelected(`machine:${machine.instance}:${target}`);
                    }}>Remove & redirect state
                    </button>
                </section>
            </> : selectedRegion ? <><p
                className="machine-source-id">{selectedRegion.kind === 'operation' ? 'Custom graph operation · source retained' : 'Verified source region'}</p>{selectedRegion.actions.map(source => {
                const node = source.node,
                    staged = draft?.edits.find(edit => edit.nodeKey === source.key && edit.kind === 'label'),
                    owner = controlSource(graph, scope),
                    field = node.nodeType === 'branch' ? 'conditionId' : node.nodeType === 'loop' ? 'stopConditionId' : node.nodeType === 'modifyState' ? 'expressionId' : null,
                    ast = field ? reelObject(owner.expressions)?.[String(node[field])] : undefined,
                    pending = draft?.edits.find(edit => edit.kind === 'expression' && edit.nodeKey === source.key);
                return <section key={source.key} className="machine-form-section">
                    <header><h3>{String(node.label ?? node.id)}</h3><span>{node.nodeType}</span></header>
                    <label>Display name<input disabled={disabled}
                                              value={staged?.kind === 'label' ? staged.value : String(node.label ?? node.id)}
                                              onChange={event => stageEdit({
                                                  kind: 'label',
                                                  nodeKey: source.key,
                                                  value: event.target.value
                                              })}/></label>{field && !!ast && !(respin && node.outputKey === 'respins') &&
                    <ExpressionInput key={JSON.stringify(pending?.kind === 'expression' ? pending.value : ast)}
                                     label={node.nodeType === 'branch' ? 'Transition guard' : node.nodeType === 'loop' ? 'Stop condition' : `Assignment to ${String(node.outputKey)}`}
                                     value={(pending?.kind === 'expression' ? pending.value : ast) as Expr}
                                     disabled={disabled} change={value => stageEdit({
                        kind: 'expression',
                        nodeKey: source.key,
                        field,
                        value
                    })}/>}{respin && node.outputKey === 'respins' &&
                    <p>Initialization, restoration and the iteration bound are edited together in the linked respin rule
                        above.</p>}{node.nodeType === 'loop' &&
                    <label>Iteration limit<input aria-label="Existing iteration limit" type="number" min="1"
                                                 max="100000"
                                                 value={Number(draft?.edits.find(edit => edit.kind === 'loop-budget' && edit.nodeKey === source.key)?.value ?? node.maxIterations)}
                                                 disabled={disabled || !!respin} onChange={event => stageEdit({
                        kind: 'loop-budget',
                        nodeKey: source.key,
                        value: Number(event.target.value)
                    })}/></label>}{node.nodeType === 'library' && <><p>Operation: {String(node.mechanicName)}</p>
                    <button disabled={!!draft} onClick={() => chooseScope(String(node.mechanicName), true)}>Open nested
                        feature
                    </button>
                    <details>
                        <summary>Literal parameter bindings</summary>
                        <pre>{JSON.stringify(node.parameters ?? {}, null, 2)}</pre>
                    </details>
                </>}{node.nodeType === 'draw' && <Link to="/model/studio">Edit this weighted source in Studio</Link>}
                    <button onClick={() => openGraph(node.id)}>Inspect source operation</button>
                </section>;
            })}</> : <div className="machine-inspector-empty">
                <div className="machine-empty-glyph">↳</div>
                <p>Explore the lifecycle</p><small>States show ordered operations. Select a decision to inspect its
                actual guard.</small></div>}
                {!machine &&
                    <section className="machine-form-section"><h3>Settlement cap policy</h3><label>When changing
                        gameplay rules<select aria-label="Machine settlement cap policy" disabled={disabled}
                                              value={draft?.capPolicy ?? 'keep'} onChange={event => persist({
                            ...baseDraft(),
                            capPolicy: event.target.value as 'keep' | 'recompute'
                        })}>
                            <option value="keep">Keep the existing cap as a game rule</option>
                            <option value="recompute">Recompute a proven settlement bound</option>
                        </select></label><small>Recomputation requires a verified dependency contract. The compiler
                        validates the complete candidate.</small></section>}
                {machine && <section className="machine-form-section"><h3>Machine bindings & budget</h3><label>Machine
                    name<input value={machine.name} disabled={disabled}
                               onChange={event => stageMachine({...machine, name: event.target.value})}/></label><label>State-visit
                    budget<input aria-label="State visit budget" type="number" min="1" max="100000"
                                 value={machine.stepBudget} disabled={disabled} onChange={event => stageMachine({
                        ...machine,
                        stepBudget: Number(event.target.value)
                    })}/></label><small>Counts terminal visits. Keep justified slack for natural
                    completion.</small>{machine.variables.map((variable, index) => <div className="machine-variable"
                                                                                        key={index}><label>Variable
                    key<input value={variable.key} disabled={disabled} onChange={event => stageMachine({
                        ...machine,
                        variables: machine.variables.map((value, position) => position === index ? {
                            ...value,
                            key: event.target.value
                        } : value)
                    })}/></label><label>Ownership<select value={variable.scope} disabled={disabled}
                                                         onChange={event => stageMachine({
                                                             ...machine,
                                                             variables: machine.variables.map((value, position) => position === index ? {
                                                                 ...value,
                                                                 scope: event.target.value as 'private' | 'shared'
                                                             } : value)
                                                         })}>
                    <option value="private">Private to this instance</option>
                    <option value="shared">Shared game field</option>
                </select></label><label>Initial value (JSON literal)<input
                    defaultValue={JSON.stringify(variable.initial)} disabled={disabled} onBlur={event => {
                    try {
                        const initial = parseModelJson(`{"value":${event.target.value}}`).value;
                        if (typeof initial !== 'string' && typeof initial !== 'number' && typeof initial !== 'boolean') throw new Error('Use a primitive JSON literal.');
                        stageMachine({
                            ...machine,
                            variables: machine.variables.map((value, position) => position === index ? {
                                ...value,
                                initial
                            } : value)
                        });
                    } catch (error) {
                        setError(errorText(error));
                    }
                }}/></label>
                    <button disabled={disabled} onClick={() => stageMachine({
                        ...machine,
                        variables: machine.variables.filter((_, position) => position !== index)
                    })}>Remove variable
                    </button>
                </div>)}
                    <button disabled={disabled} onClick={() => stageMachine({
                        ...machine,
                        variables: [...machine.variables, {key: newIdentity('variable'), scope: 'private', initial: 0}]
                    })}>Add variable
                    </button>
                    <label>Read-only game fields<input value={machine.reads?.join(', ') ?? ''} disabled={disabled}
                                                       onChange={event => stageMachine({
                                                           ...machine,
                                                           reads: event.target.value.split(',').map(value => value.trim()).filter(Boolean)
                                                       })}/></label><small>Shared initial values already in the game
                        stay unchanged. Private values reset per invocation.</small></section>}
                {scope && !draft?.fresh &&
                    <section className="machine-form-section"><h3>Definition uses</h3><p>{uses.length} callers share
                        this
                        definition. {binding ? 'An independent copy gets its own private machine fields.' : 'An independent definition retains the imported game’s shared state fields. Existing counter and payout rules remain part of that game.'}</p>{uses.map(use =>
                        <button key={use.source.key} disabled={disabled || !!draft} onClick={() => {
                            const name = binding ? `${binding.definition.slice(0, 60)}-copy-${crypto.randomUUID().slice(0, 6)}` : `private-feature-${crypto.randomUUID().slice(0, 6)}`;
                            persist({
                                ...baseDraft(),
                                copy: {nodeKey: use.source.key, name, kind: binding ? 'machine' : 'source'}
                            });
                        }}>{use.source.scope ?? 'Current round'} / {String(use.source.node.label ?? use.source.id)} ·
                            make independent</button>)}</section>}
            </aside>
        </div>
        <footer className="machine-page-footer">
            <span>{hash ? `Source ${hash.slice(0, 12)}` : 'Reading source…'}</span><span>Private project · graph-backed authoring</span>
        </footer>
        <dialog ref={creation} className="studio-import-dialog studio-tabular-dialog machine-create-dialog"
                onCancel={() => setCreateOpen(false)}>
            <header>
                <div><h2>Create or reuse a feature</h2><p>Choose its execution point. The current round keeps its
                    settlement and price.</p></div>
                <button aria-label="Close machine creation" onClick={() => setCreateOpen(false)}>×</button>
            </header>
            <label>Feature source<select value={createKind}
                                         onChange={event => setCreateKind(event.target.value as 'new' | 'reuse')}>
                <option value="new">New authored state machine</option>
                <option value="reuse">Reuse existing graph definition</option>
            </select></label>{createKind === 'new' ?
            <label>Stable instance ID<input aria-label="New machine instance ID" value={name}
                                            onChange={event => setName(event.target.value)}/></label> :
            <label>Existing definition<select value={reused} onChange={event => setReused(event.target.value)}>
                <option value="">Choose a definition</option>
                {Object.keys(graph.mechanics ?? {}).map(name => <option key={name}>{name}</option>)}
            </select></label>}<label>Connect feature<select aria-label="New machine connection" value={connection}
                                                            onChange={event => setConnection(event.target.value)}>{connections.map(value =>
            <option key={value.value}
                    value={value.value}>{value.label}</option>)}</select></label>{connection === 'empty-round' &&
            <label>Shared numeric payout field<input value={payoutKey}
                                                     onChange={event => setPayoutKey(event.target.value)}/></label>}
            <div className="studio-derived">
                <b>{connection === 'definition' ? 'Saved as a reusable definition' : 'Connected to the chosen round action'}</b><span>{connection === 'definition' ? 'It runs only after explicitly adding a Library use.' : 'The connection and generated definitions apply together. A new feature does not add its own payout sink.'}</span>
            </div>
            {error && <p className="studio-error" role="alert">{error}</p>}
            <footer className="studio-import-actions">
                <button onClick={() => setCreateOpen(false)}>Cancel</button>
                <button className="studio-primary" onClick={startNew}>Stage feature</button>
            </footer>
        </dialog>
    </section>;
}
