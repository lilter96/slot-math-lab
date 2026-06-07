import { useMemo, useEffect } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useAppStore } from '../store';
import Palette from '../components/Palette';
import Inspector from '../components/Inspector';
import MetricStrip from '../components/MetricStrip';
import SlotCanvas from '../components/canvas/SlotCanvas';
import TablesPanel from '../components/tables/TablesPanel';
import MechanicsPanel from '../components/mechanics/MechanicsPanel';
import { useLiveMetrics } from '../hooks/useLiveMetrics';
import ErrorBoundary from '../components/ErrorBoundary';

export default function Build() {
  const selectedNodeId = useAppStore((s) => s.selectedNodeId);
  const symbols = useAppStore((s) => s.tableSymbols);
  const setTableSymbols = useAppStore((s) => s.setTableSymbols);
  const addNode = useAppStore((s) => s.addNode);
  const nodes = useAppStore((s) => s.nodes);
  const [searchParams] = useSearchParams();

  // Load shared config from URL (?load=...)
  useEffect(() => {
    const encoded = searchParams.get('load');
    if (!encoded || nodes.length > 0) return; // don't overwrite existing graph
    try {
      const json = decodeURIComponent(atob(encoded));
      const data = JSON.parse(json);
      if (data.schemaVersion && data.symbols) {
        setTableSymbols(data.symbols);
      }
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

  const liveMetrics = useLiveMetrics(symbols, expressions);

  return (
    <>
      <Palette />
      <div className="workspace">
        <ErrorBoundary>
          <SlotCanvas />
        </ErrorBoundary>
        <MetricStrip liveMetrics={liveMetrics} />
      </div>
      <TablesPanel />
      <MechanicsPanel />
      {selectedNodeId && <Inspector />}
    </>
  );
}
