import { useMemo } from 'react';
import { useAppStore } from '../store';
import Palette from '../components/Palette';
import Inspector from '../components/Inspector';
import MetricStrip from '../components/MetricStrip';
import SlotCanvas from '../components/canvas/SlotCanvas';
import TablesPanel from '../components/tables/TablesPanel';
import MechanicsPanel from '../components/mechanics/MechanicsPanel';
import { useLiveMetrics } from '../hooks/useLiveMetrics';

export default function Build() {
  const selectedNodeId = useAppStore((s) => s.selectedNodeId);
  const symbols = useAppStore((s) => s.tableSymbols);
  const nodes = useAppStore((s) => s.nodes);

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
        <SlotCanvas />
        <MetricStrip liveMetrics={liveMetrics} />
      </div>
      <TablesPanel />
      <MechanicsPanel />
      {selectedNodeId && <Inspector />}
    </>
  );
}
