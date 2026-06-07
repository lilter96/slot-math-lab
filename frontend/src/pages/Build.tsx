import Palette from '../components/Palette';
import Inspector from '../components/Inspector';
import MetricStrip from '../components/MetricStrip';

export default function Build() {
  return (
    <>
      <Palette />
      <div className="workspace">
        <div className="canvas-wrap">
          <div className="canvas-grid" />
          <div className="canvas-placeholder">
            Canvas — drag nodes from the palette (G19)
          </div>
        </div>
        <MetricStrip />
      </div>
      <Inspector />
    </>
  );
}
