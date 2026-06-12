import { useMemo, useEffect } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useAppStore } from '../store';
import Palette from '../components/Palette';
import MetricStrip from '../components/MetricStrip';
import SlotCanvas from '../components/canvas/SlotCanvas';
import RightPanel from '../components/RightPanel';
import { useLiveMetrics } from '../hooks/useLiveMetrics';
import ErrorBoundary from '../components/ErrorBoundary';

export default function Build() {
  const addNode = useAppStore((s) => s.addNode);
  const nodes = useAppStore((s) => s.nodes);
  const setLiveMetrics = useAppStore((s) => s.setLiveMetrics);
  const [searchParams] = useSearchParams();

  // Load shared config from URL (?load=...)
  useEffect(() => {
    const encoded = searchParams.get('load');
    if (!encoded || nodes.length > 0) return;
    try {
      const json = decodeURIComponent(atob(encoded));
      const data = JSON.parse(json);
      if (data.nodes) {
        for (const n of data.nodes) {
          addNode({ ...n, position: n.position || { x: 100, y: 100 } });
        }
      }
    } catch {
      // Invalid share link — ignore
    }
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // Collect expressions from branch/map nodes
  const expressions = useMemo(() => {
    const exprMap: Record<string, string> = {};
    for (const n of nodes) {
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

  // Sync live metrics into store so sink node and other consumers can read it
  useEffect(() => {
    const prov = liveMetrics?.overall?.provenance;
    const validProv: 'Exact' | 'Sampled' | 'NeedsFullRun' | null =
      prov === 'Exact' || prov === 'Sampled' || prov === 'NeedsFullRun' ? prov : null;
    setLiveMetrics(liveMetrics?.overall?.rtp ?? null, validProv);
  }, [liveMetrics?.overall?.rtp, liveMetrics?.overall?.provenance]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <>
      <Palette />
      <div className="workspace">
        <ErrorBoundary>
          <SlotCanvas />
        </ErrorBoundary>
        <MetricStrip liveMetrics={liveMetrics} />
      </div>
      <RightPanel />
    </>
  );
}
