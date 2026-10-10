import { useRef, useState } from 'react';
import { useReactFlow } from '@xyflow/react';
import { Ic } from '../Icons';
import { exportProject, loadProject, loadCoinExample, catalogExamples } from '../../lib/projectFiles';
import AiGenerateModal from '../AiGenerateModal';
import { useAppStore } from '../../store';
import { DEFAULT_FEATURES, useFeaturesQuery } from '../../api/hooks';
import { closeMechanic } from '../../lib/projectFiles';
import { createDogHouseGraph } from '../../games/doghouse/graph';
import ProjectDataEditor from '../editor/ProjectDataEditor';

export default function CanvasToolbar() {
  const { zoomIn, zoomOut, fitView } = useReactFlow();
  const fileRef = useRef<HTMLInputElement>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const [showAi, setShowAi] = useState(false);
  const [showData, setShowData] = useState(false);
  const trail = useAppStore(s => s.graphTrail);
  const nodes = useAppStore(s => s.nodes);
  const verificationSource = useAppStore(s => s.verificationSource);
  const features = useFeaturesQuery().data ?? DEFAULT_FEATURES;

  return (
    <>
      <div className="canvas-toolbar" onMouseDown={(e) => e.stopPropagation()}>
        {verificationSource && <button onClick={() => { loadProject(verificationSource); useAppStore.setState({ verificationSource: null, graphTrail: [] }); }}>Return to game graph</button>}
        {trail.length > 0 && <button aria-label="Save subgraph and return" onClick={closeMechanic}>← Save & return</button>}
        <button aria-label="Build Dog House graph" onClick={() => { useAppStore.setState({ graphTrail: [], verificationSource: null }); loadProject(createDogHouseGraph()); }}>Dog House · 98%</button>
        <button aria-label="Edit project data and expressions" onClick={() => setShowData(true)}>Data / AST</button>
        <select aria-label="Focus graph node" value="" onChange={e => { if (!e.target.value) return; useAppStore.getState().selectNode(e.target.value); void fitView({ nodes: [{ id: e.target.value }], padding: 1, maxZoom: 1 }); }}>
          <option value="">Find node…</option>{nodes.map(node => <option key={node.id} value={node.id}>{node.data.label}</option>)}
        </select>
        <input ref={fileRef} type="file" accept="application/json,.json" aria-label="Import project file" style={{ display: 'none' }} onChange={async e => {
          const file = e.target.files?.[0]; if (!file) return;
          try { loadProject(JSON.parse(await file.text())); setFileError(null); } catch (err) { setFileError(err instanceof Error ? err.message : 'Invalid project'); }
          e.target.value = '';
        }} />
        <select aria-label="Load catalog example" value="" onChange={e => { if (e.target.value) loadProject(catalogExamples[e.target.value]); }}>
          <option value="">Catalog examples</option>
          {Object.keys(catalogExamples).map(name => <option key={name} value={name}>{name}</option>)}
        </select>
        <button onClick={loadCoinExample} aria-label="Load coin example" title="Load REF-A example">REF-A</button>
        <button onClick={() => fileRef.current?.click()} aria-label="Import project" title="Import project">Import</button>
        <button aria-label="Export project" title="Export project" onClick={() => {
          try { const content = exportProject(); if (!content) return; const url = URL.createObjectURL(new Blob([JSON.stringify(content, null, 2)], { type: 'application/json' }));
            const link = document.createElement('a'); link.href = url; link.download = 'slotmath-project.json'; link.click(); URL.revokeObjectURL(url); }
          catch (err) { setFileError(err instanceof Error ? err.message : 'Invalid project'); }
        }}>Save</button>
        {fileError && <span role="alert">{fileError}</span>}
        <button onClick={() => zoomOut()} title="Zoom out" aria-label="Zoom out"><Ic.minus /></button>
        <span className="zoom-label" />
        <button onClick={() => zoomIn()} title="Zoom in" aria-label="Zoom in"><Ic.plus /></button>
        <button onClick={() => fitView({ padding: 0.2 })} title="Reset view" aria-label="Reset view"><Ic.fit /></button>
        {features.ai && (
          <>
            <div style={{ width: 1, height: 20, background: 'var(--line)', margin: '0 2px' }} />
            <button
              onClick={() => setShowAi(true)}
              title="AI Generate — describe your game in plain English"
              aria-label="AI Generate graph"
              style={{ color: 'var(--exact)' }}
            >
              <Ic.ai />
            </button>
          </>
        )}
      </div>

      {showAi && features.ai && <AiGenerateModal onClose={() => setShowAi(false)} />}
      {showData && <ProjectDataEditor onClose={() => setShowData(false)} />}
    </>
  );
}
