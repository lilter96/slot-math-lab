import {catalogExamples} from '../../lib/catalogExamples';
import {
    closeMechanic,
    inspectRootProject,
    loadCoinExample,
    loadProject,
    projectContentScope,
    switchProject
} from '../../lib/projectFiles';
import {importProjectFile} from '../../lib/authoring/projectEnvelope';
import {useNavigate} from 'react-router-dom';
import {useCallback, useEffect, useRef, useState} from 'react';
import {useReactFlow, useViewport} from '@xyflow/react';
import {Ic} from '../Icons';
import AiGenerateModal from '../AiGenerateModal';
import {useAppStore} from '../../store';
import {DEFAULT_FEATURES, useFeaturesQuery} from '../../api/hooks';
import ProjectDataEditor from '../editor/ProjectDataEditor';
import ConnectNodesDialog from '../editor/ConnectNodesDialog';

const graphScope = () => {
    const state = useAppStore.getState();
    return JSON.stringify([state.projectId, state.graphTrail.map(entry => entry.mechanic)]);
};

/** Live zoom readout (e.g. "100%") driven by the React Flow viewport. */
function ZoomLevel() {
    const {zoom} = useViewport();
    return <>{Math.round(zoom * 100)}%</>;
}

export default function CanvasToolbar({canConnect = true}: { canConnect?: boolean }) {
    const navigate = useNavigate();
    const {zoomIn, zoomOut, fitView, setViewport, getViewport} = useReactFlow();
    const fileRef = useRef<HTMLInputElement>(null);
    const [fileError, setFileError] = useState<string | null>(null);
    const [showAi, setShowAi] = useState(false);
    const [showData, setShowData] = useState(false);
    const [connectionScope, setConnectionScope] = useState<string | null>(null);
    const projectId = useAppStore(state => state.projectId);
    const trail = useAppStore(s => s.graphTrail);
    const nodes = useAppStore(s => s.nodes);
    const verificationSource = useAppStore(s => s.verificationSource);
    const features = useFeaturesQuery().data ?? DEFAULT_FEATURES;
    const scope = JSON.stringify([projectId, trail.map(entry => entry.mechanic)]);
    const frames = useRef<number[]>([]), focusTask = useRef(0), importTask = useRef(0);
    const pendingScope = useRef<string | null>(null), importScope = useRef<string | null>(null);
    const cancelFocus = useCallback(() => {
        focusTask.current++;
        frames.current.forEach(cancelAnimationFrame);
        frames.current = [];
    }, []);
    useEffect(() => {
        if (pendingScope.current !== scope) cancelFocus();
        if (importScope.current !== scope) importTask.current++;
    }, [scope, cancelFocus]);
    useEffect(() => () => {
        cancelFocus();
        importTask.current++;
    }, [cancelFocus]);
    const focusGraph = useCallback((id: string, kind: 'node' | 'edge') => {
        cancelFocus();
        const origin = graphScope(), token = focusTask.current;
        pendingScope.current = origin;
        frames.current.push(requestAnimationFrame(() => {
            frames.current.push(requestAnimationFrame(() => {
                if (token !== focusTask.current || origin !== graphScope()) return;
                const state = useAppStore.getState();
                if (kind === 'node') {
                    if (state.selectedNodeId === id && state.nodes.some(node => node.id === id)) void fitView({
                        nodes: [{id}],
                        padding: 1,
                        maxZoom: 1
                    });
                } else {
                    const edge = state.edges.find(item => item.id === id && item.selected);
                    const endpoints = edge ? state.nodes.filter(node => node.id === edge.source || node.id === edge.target) : [];
                    if (!state.selectedNodeId && endpoints.length) void fitView({
                        nodes: endpoints,
                        padding: 0.5,
                        maxZoom: 1
                    });
                }
            }));
        }));
    }, [fitView, cancelFocus]);
    useEffect(() => {
        const node = (event: Event) => focusGraph((event as CustomEvent<string>).detail, 'node');
        const edge = (event: Event) => focusGraph((event as CustomEvent<string>).detail, 'edge');
        window.addEventListener('slotmath:focus-node', node);
        window.addEventListener('slotmath:focus-edge', edge);
        return () => {
            window.removeEventListener('slotmath:focus-node', node);
            window.removeEventListener('slotmath:focus-edge', edge);
            cancelFocus();
        };
    }, [focusGraph, cancelFocus]);

    return (
        <>
            <div className="canvas-toolbar" onMouseDown={(e) => e.stopPropagation()}>
                {verificationSource && <button onClick={() => {
                    loadProject(verificationSource);
                    useAppStore.setState({verificationSource: null, graphTrail: []});
                }}>Return to game graph</button>}
                {trail.length > 0 &&
                    <button aria-label="Save subgraph and return" onClick={closeMechanic}>← Save & return</button>}
                <button aria-label="Choose a project or template" onClick={() => navigate('/projects')}>Projects
                </button>
                <button aria-label="Edit project data and expressions" onClick={() => setShowData(true)}>Data / AST
                </button>
                <button aria-label="Connect graph ports" disabled={!canConnect}
                        onClick={() => setConnectionScope(scope)}>Connect ports
                </button>
                <select aria-label="Focus graph node" value="" onChange={e => {
                    const id = e.target.value;
                    if (!id) return;
                    useAppStore.getState().selectNode(id);
                    focusGraph(id, 'node');
                }}>
                    <option value="">Find node…</option>
                    {nodes.map(node => <option key={node.id} value={node.id}>{node.data.label}</option>)}
                </select>
                <input ref={fileRef} type="file" accept="application/json,.json" aria-label="Import project file"
                       style={{display: 'none'}} onChange={async e => {
                    const file = e.target.files?.[0];
                    if (!file) return;
                    e.target.value = '';
                    const token = ++importTask.current, origin = graphScope(), base = projectContentScope();
                    importScope.current = origin;
                    try {
                        const content = await file.text();
                        if (token !== importTask.current || origin !== graphScope()) return;
                        if (base !== projectContentScope()) {
                            setFileError('The model changed while reading this file. Select the file again to import it.');
                            return;
                        }
                        importProjectFile(content);
                        setFileError(null);
                    } catch (err) {
                        if (token === importTask.current && origin === graphScope()) setFileError(err instanceof Error ? err.message : 'Invalid project');
                    }
                }}/>
                <select aria-label="Load catalog example" value="" onChange={e => {
                    if (e.target.value) switchProject(catalogExamples[e.target.value]);
                }}>
                    <option value="">Catalog examples</option>
                    {Object.keys(catalogExamples).map(name => <option key={name} value={name}>{name}</option>)}
                </select>
                <button onClick={loadCoinExample} aria-label="Load coin example" title="Load REF-A example">REF-A
                </button>
                <button onClick={() => fileRef.current?.click()} aria-label="Import project"
                        title="Import project">Import
                </button>
                <button aria-label="Export project" title="Export project" onClick={() => {
                    try {
                        const snapshot = inspectRootProject();
                        if (snapshot.error) throw new Error(snapshot.error);
                        const content = snapshot.config;
                        if (!content) return;
                        const url = URL.createObjectURL(new Blob([JSON.stringify(content, null, 2)], {type: 'application/json'}));
                        const link = document.createElement('a');
                        link.href = url;
                        link.download = 'slotmath-project.json';
                        link.click();
                        URL.revokeObjectURL(url);
                    } catch (err) {
                        setFileError(err instanceof Error ? err.message : 'Invalid project');
                    }
                }}>Save
                </button>
                {fileError && <span role="alert">{fileError}</span>}
                {features.ai && (
                    <>
                        <div style={{width: 1, height: 20, background: 'var(--line)', margin: '0 2px'}}/>
                        <button
                            onClick={() => setShowAi(true)}
                            title="AI Generate — describe your game in plain English"
                            aria-label="AI Generate graph"
                            style={{color: 'var(--exact)'}}
                        >
                            <Ic.ai/>
                        </button>
                    </>
                )}
            </div>

            <div className="canvas-zoom" onMouseDown={(e) => e.stopPropagation()}>
                <button onClick={() => zoomOut()} title="Zoom out" aria-label="Zoom out"><Ic.minus/></button>
                <span className="zoom-label"><ZoomLevel/></span>
                <button onClick={() => zoomIn()} title="Zoom in" aria-label="Zoom in"><Ic.plus/></button>
                <button onClick={() => {
                    const selected = nodes.filter(node => node.selected || node.id === useAppStore.getState().selectedNodeId);
                    if (selected.length) void fitView({nodes: selected, padding: 0.3, maxZoom: 1});
                }} title="Fit selection" aria-label="Fit selection"><Ic.target/></button>
                <button onClick={() => setViewport({...getViewport(), zoom: 1})} title="100% zoom"
                        aria-label="100% zoom">1:1
                </button>
                <button onClick={() => fitView({padding: 0.2, maxZoom: 1})} title="Reset view" aria-label="Reset view">
                    <Ic.fit/></button>
            </div>

            {showAi && features.ai && <AiGenerateModal onClose={() => setShowAi(false)}/>}
            {showData && <ProjectDataEditor onClose={() => setShowData(false)}/>}
            {canConnect && connectionScope === scope && <ConnectNodesDialog onClose={() => setConnectionScope(null)}/>}
        </>
    );
}
