import { useAppStore } from '../store';
import Palette from '../components/Palette';
import Inspector from '../components/Inspector';
import MetricStrip from '../components/MetricStrip';
import SlotCanvas from '../components/canvas/SlotCanvas';
import TablesPanel from '../components/tables/TablesPanel';
import MechanicsPanel from '../components/mechanics/MechanicsPanel';

export default function Build() {
  const selectedNodeId = useAppStore((s) => s.selectedNodeId);

  return (
    <>
      <Palette />
      <div className="workspace">
        <SlotCanvas />
        <MetricStrip />
      </div>
      <TablesPanel />
      <MechanicsPanel />
      {selectedNodeId && <Inspector />}
    </>
  );
}
