import Palette from '../components/Palette';
import Inspector from '../components/Inspector';
import MetricStrip from '../components/MetricStrip';
import SlotCanvas from '../components/canvas/SlotCanvas';

export default function Build() {
  return (
    <>
      <Palette />
      <div className="workspace">
        <SlotCanvas />
        <MetricStrip />
      </div>
      <Inspector />
    </>
  );
}
