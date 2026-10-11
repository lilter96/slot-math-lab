import { useMemo, useEffect } from 'react';
import { useAppStore, withoutSelection } from '../store';
import Palette from '../components/Palette';
import MetricStrip from '../components/MetricStrip';
import SlotCanvas from '../components/canvas/SlotCanvas';
import RightPanel from '../components/RightPanel';
import { useLiveMetrics } from '../hooks/useLiveMetrics';
import ErrorBoundary from '../components/ErrorBoundary';
import ModelNavigation from '../components/editor/ModelNavigation';

export default function Build() {
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
    const prov = liveMetrics?.overall?.provenance;
    const validProv: 'Exact' | 'ExactInterval' | 'ExactWithMassLoss' | 'Sampled' | 'NeedsFullRun' | null =
      prov === 'Exact' || prov === 'ExactInterval' || prov === 'ExactWithMassLoss' || prov === 'Sampled' || prov === 'NeedsFullRun' ? prov : null;
    setLiveMetrics(liveMetrics?.overall?.rtp ?? null, validProv);
  }, [liveMetrics?.overall?.rtp, liveMetrics?.overall?.provenance]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <>
      <Palette />
      <ModelNavigation />
      <div className="workspace">
        {savedDraft && <div className="model-navigation"><div className="model-navigation-heading"><b>Saved model opened from Results</b><span>Your earlier editor draft is preserved, including open subgraphs.</span></div><div className="model-navigation-actions"><button className="btn" onClick={() => useAppStore.setState({ ...savedDraft, nodes: withoutSelection(savedDraft.nodes) ?? [], selectedNodeId: null, resultsDraft: null })}>Restore previous editor draft</button></div></div>}
        <ErrorBoundary>
          <SlotCanvas />
        </ErrorBoundary>
        {proof || trail.length ? <div className="metric-strip" role="status" style={{ padding: 16 }}>{proof ? 'Expectation proof · Data / AST → Execute & inspect → expectedRtp. This graph calculates expectation; it does not represent a payout distribution.' : 'Editing subgraph · Save & return to evaluate the complete model.'}</div> : <MetricStrip liveMetrics={liveMetrics} />}
      </div>
      <RightPanel />
    </>
  );
}
