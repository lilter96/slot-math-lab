import {useCallback, useMemo, useEffect, useState} from 'react';
import {useAppStore, withoutSelection} from '../store';
import Palette from '../components/Palette';
import MetricStrip from '../components/MetricStrip';
import SlotCanvas from '../components/canvas/SlotCanvas';
import RightPanel from '../components/RightPanel';
import {useLiveMetrics} from '../hooks/useLiveMetrics';
import ErrorBoundary from '../components/ErrorBoundary';
import {closeMechanic} from '../lib/projectFiles';
import {DEFAULT_FEATURES, useFeaturesQuery} from '../api/hooks';
import ModelNavigation from '../components/editor/ModelNavigation';
import {metricProvenance} from '../lib/metricEvidence';
import GraphValidation from '../components/editor/GraphValidation';
import GraphSimplification from '../components/editor/GraphSimplification';
import CollectionRepeat from '../components/editor/CollectionRepeat';

export default function Build() {
    const [showPalette, setShowPalette] = useState(false);
    const [panelMode, setPanelMode] = useState<'mechanics' | 'ai' | null>(null);
    const selected = useAppStore(s => s.selectedNodeId);
    const inspectorOpen = useAppStore(s => s.inspectorOpen);
    const features = useFeaturesQuery().data ?? DEFAULT_FEATURES;
    const closeInspector = useCallback(() => {
        useAppStore.getState().setInspectorPinned(false);
        useAppStore.getState().setInspectorOpen(false);
        setPanelMode(null);
        requestAnimationFrame(() => {
            const node = selected ? document.querySelector<HTMLElement>(`.react-flow__node[data-id="${CSS.escape(selected)}"]`) : null;
            (node ?? document.querySelector<HTMLElement>('[aria-label="Show inspector"]'))?.focus();
        });
    }, [selected]);
    useEffect(() => {
        const escape = (event: KeyboardEvent) => {
            if (event.key === 'Escape' && (inspectorOpen || panelMode) && !document.querySelector('dialog[open]')) closeInspector();
        };
        document.addEventListener('keydown', escape);
        return () => document.removeEventListener('keydown', escape);
    }, [closeInspector, inspectorOpen, panelMode]);
    const nodes = useAppStore((s) => s.nodes);
    const setLiveMetrics = useAppStore((s) => s.setLiveMetrics);
    const savedDraft = useAppStore(s => s.resultsDraft);
    // Collect expressions from branch/map nodes
    const expressions = useMemo(() => {
        const exprMap: Record<string, string> = {};
        for (const n of nodes) {
            if (n.data.backendNode) continue;
            const d = n.data;
            if (d.expression && typeof d.expression === 'string' && d.expression.trim()) {
                exprMap[n.id] = d.expression;
            }
            if (d.terminationExpr && typeof d.terminationExpr === 'string' && d.terminationExpr.trim()) {
                exprMap[`${n.id}-term`] = d.terminationExpr;
            }
        }
        return exprMap;
    }, [nodes]);

    const liveMetrics = useLiveMetrics(expressions);
    const proof = useAppStore(s => s.tables.uiAnalysisKind === 'expectationProof');
    const trail = useAppStore(s => s.graphTrail);

    // Sync live metrics into store so sink node and other consumers can read it
    useEffect(() => {
        const prov = liveMetrics?.overall && metricProvenance(liveMetrics.overall) ? liveMetrics.overall.provenance : undefined;
        const validProv: 'Exact' | 'ExactInterval' | 'ExactWithMassLoss' | 'Sampled' | 'NeedsFullRun' | null =
            prov === 'Exact' || prov === 'ExactInterval' || prov === 'ExactWithMassLoss' || prov === 'Sampled' || prov === 'NeedsFullRun' ? prov : null;
        setLiveMetrics(validProv ? liveMetrics.overall?.rtp ?? null : null, validProv);
    }, [liveMetrics.overall, setLiveMetrics]);

    return (
        <>
            {showPalette && <Palette/>}
            <ModelNavigation/>
            <div className="workspace">
                {savedDraft && <div className="model-navigation">
                    <div className="model-navigation-heading"><b>Saved model opened from Results</b><span>Your earlier editor draft is preserved, including open subgraphs.</span>
                    </div>
                    <div className="model-navigation-actions">
                        <button className="btn" onClick={() => useAppStore.setState({
                            ...savedDraft,
                            nodes: withoutSelection(savedDraft.nodes) ?? [],
                            selectedNodeId: null,
                            resultsDraft: null
                        })}>Restore previous editor draft
                        </button>
                    </div>
                </div>}
                <header className="graph-heading">
                    <nav aria-label="Graph breadcrumbs"><span>Logic graph</span>{trail.map((item, index) => <button
                        className="btn ghost" key={index} onClick={() => {
                        while (useAppStore.getState().graphTrail.length > index + 1) closeMechanic();
                    }}>{item.mechanic.replaceAll('-', ' ')}</button>)}</nav>
                    <div className="graph-actions">
                        <button className="btn add-nodes" aria-expanded={showPalette}
                                onClick={() => setShowPalette(value => !value)}>Add nodes
                        </button>
                        <GraphSimplification/>
                        <CollectionRepeat/>
                        <button className="btn" aria-label="Open mechanic manager" onClick={() => {
                            useAppStore.getState().selectNode(null);
                            setPanelMode('mechanics');
                        }}>Mechanics
                        </button>
                        {features.autoTune && <button className="btn" aria-label="Open AI tuning panel" onClick={() => {
                            useAppStore.getState().selectNode(null);
                            setPanelMode('ai');
                        }}>AI</button>}
                        {!inspectorOpen && !panelMode && <button className="btn" aria-label="Show inspector"
                                                                 onClick={() => useAppStore.getState().setInspectorOpen(true)}>Show
                            inspector</button>}
                    </div>
                </header>
                <GraphValidation/>
                <ErrorBoundary>
                    <SlotCanvas/>
                </ErrorBoundary>
                {proof || trail.length ? <div className="metric-strip" role="status"
                                              style={{padding: 16}}>{proof ? 'Expectation proof · Data / AST → Execute & inspect → expectedRtp. This graph calculates expectation; it does not represent a payout distribution.' : 'Editing subgraph · Save & return to evaluate the complete model.'}</div> :
                    <MetricStrip liveMetrics={liveMetrics}/>}
            </div>
            {(inspectorOpen || panelMode) &&
                <RightPanel key={panelMode ?? 'inspector'} initialTab={panelMode ?? 'inspector'}
                            onClose={closeInspector}/>}
        </>
    );
}
