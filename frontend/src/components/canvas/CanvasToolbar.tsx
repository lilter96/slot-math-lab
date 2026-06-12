import { useState } from 'react';
import { useReactFlow } from '@xyflow/react';
import { Ic } from '../Icons';
import AiGenerateModal from '../AiGenerateModal';

export default function CanvasToolbar() {
  const { zoomIn, zoomOut, fitView } = useReactFlow();
  const [showAi, setShowAi] = useState(false);

  return (
    <>
      <div className="canvas-toolbar" onMouseDown={(e) => e.stopPropagation()}>
        <button onClick={() => zoomOut()} title="Zoom out" aria-label="Zoom out"><Ic.minus /></button>
        <span className="zoom-label" />
        <button onClick={() => zoomIn()} title="Zoom in" aria-label="Zoom in"><Ic.plus /></button>
        <button onClick={() => fitView({ padding: 0.2 })} title="Reset view" aria-label="Reset view"><Ic.fit /></button>
        <div style={{ width: 1, height: 20, background: 'var(--line)', margin: '0 2px' }} />
        <button
          onClick={() => setShowAi(true)}
          title="AI Generate — describe your game in plain English"
          aria-label="AI Generate graph"
          style={{ color: 'var(--exact)' }}
        >
          <Ic.ai />
        </button>
      </div>

      {showAi && <AiGenerateModal onClose={() => setShowAi(false)} />}
    </>
  );
}
