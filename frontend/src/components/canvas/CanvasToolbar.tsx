import { useReactFlow } from '@xyflow/react';
import { Ic } from '../Icons';

export default function CanvasToolbar() {
  const { zoomIn, zoomOut, fitView } = useReactFlow();

  return (
    <div className="canvas-toolbar" onMouseDown={(e) => e.stopPropagation()}>
      <button onClick={() => zoomOut()} title="Zoom out" aria-label="Zoom out"><Ic.minus /></button>
      <span className="zoom-label" />
      <button onClick={() => zoomIn()} title="Zoom in" aria-label="Zoom in"><Ic.plus /></button>
      <button onClick={() => fitView({ padding: 0.2 })} title="Reset view" aria-label="Reset view"><Ic.fit /></button>
    </div>
  );
}
