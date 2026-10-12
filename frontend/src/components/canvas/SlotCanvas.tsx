import { useCallback, useRef, useEffect, useState } from 'react';
import {
  ReactFlow,
  ReactFlowProvider,
  Background,
  useNodesInitialized,
  useReactFlow,
  type Connection,
  type Node,
  type Viewport,
} from '@xyflow/react';
import { useAppStore, NODE_DEFAULTS, type GraphNodeData } from '../../store';
import { nodeTypes } from './nodes';
import CanvasToolbar from './CanvasToolbar';
import type { GraphNode } from '../../store';
import { openMechanic } from '../../lib/projectFiles';
import { confirmGraphDelete } from '../../lib/confirmGraphDelete';

const cameras = new Map<string, Viewport>();
function FitLoadedGraph() {
  const initialized = useNodesInitialized();
  const { fitView, setViewport } = useReactFlow();
  const graphKey = useAppStore(s => `${s.projectId ?? s.tables.id ?? s.configName}:${s.graphTrail.map(x => x.mechanic).join('/')}`);
  useEffect(() => {
    if (!initialized) return;
    const frame = requestAnimationFrame(() => { const saved = cameras.get(graphKey); if (saved) void setViewport(saved); else void fitView({ padding: 0.15, maxZoom: 1 }); });
    return () => cancelAnimationFrame(frame);
  }, [initialized, graphKey, fitView, setViewport]);
  return null;
}

const defaultEdgeOptions = {
  type: 'default',
  style: {
    stroke: 'oklch(0.40 0.016 255)',
    strokeWidth: 2,
  },
};

function CanvasSurface() {
  const { screenToFlowPosition } = useReactFlow();
  const [smallScreen, setSmallScreen] = useState(() => window.matchMedia('(max-width: 767px)').matches);
  const interactive = useAppStore(s => s.canvasInteractive);
  const graphKey = useAppStore(s => `${s.projectId ?? s.tables.id ?? s.configName}:${s.graphTrail.map(x => x.mechanic).join('/')}`);
  useEffect(() => { const query = window.matchMedia('(max-width: 767px)'); const update = () => setSmallScreen(query.matches); query.addEventListener('change', update); return () => query.removeEventListener('change', update); }, []);
  const nodes = useAppStore((s) => s.nodes);
  const edges = useAppStore((s) => s.edges);
  const onNodesChange = useAppStore((s) => s.onNodesChange);
  const onEdgesChange = useAppStore((s) => s.onEdgesChange);
  const onConnect = useAppStore((s) => s.onConnect);
  const addNode = useAppStore((s) => s.addNode);
  const selectNode = useAppStore((s) => s.selectNode);
  const edgeError = useAppStore((s) => s.edgeValidationError);
  const setEdgeError = useAppStore((s) => s.setEdgeValidationError);
  const reactFlowWrapper = useRef<HTMLDivElement>(null);

  const onNodeClick = useCallback(
    (_: React.MouseEvent, node: Node) => {
      selectNode(node.id);
    },
    [selectNode],
  );

  const onPaneClick = useCallback(() => {
    selectNode(null);
    setEdgeError(null);
  }, [selectNode, setEdgeError]);

  const onDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.dataTransfer.dropEffect = 'move';
  }, []);

  const onDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      const type = e.dataTransfer.getData('application/slotnode');
      if (!type) return;
      const wrapper = reactFlowWrapper.current;
      if (!wrapper) return;
      if (smallScreen || !interactive) return;
      const id = `node-${crypto.randomUUID()}`;

      // For library (catalog) nodes, extra data contains the mechanicName
      let extra: Record<string, string> = {};
      const extraRaw = e.dataTransfer.getData('application/slotnodeextra');
      if (extraRaw) {
        try { extra = JSON.parse(extraRaw); } catch { /* ignore */ }
      }

      // NODE_DEFAULTS key is either the type alone or 'library:<mechanicName>'
      const defaultsKey = type === 'library' && extra.mechanicName
        ? `library:${extra.mechanicName}`
        : type;
      const defaults = NODE_DEFAULTS[defaultsKey] ?? {};

      const newNode: GraphNode = {
        id,
        type: type as string,
        position: screenToFlowPosition({ x: e.clientX, y: e.clientY }),
        data: {
          nodeType: type as GraphNodeData['nodeType'],
          label: defaults.label ?? type,
          ...defaults,
          ...extra,
        },
      };
      addNode(newNode);
    },
    [addNode, screenToFlowPosition, smallScreen, interactive],
  );

  return (
    <div ref={reactFlowWrapper} className="canvas-wrap" style={{ flex: 1 }}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onBeforeDelete={async ({ nodes: removedNodes, edges: removedEdges }) => confirmGraphDelete(removedNodes.length, removedEdges.length)}
        onConnect={(conn: Connection) => {
          setEdgeError(null);
          onConnect(conn);
        }}
        onNodeClick={onNodeClick}
        onNodeDoubleClick={(_, node) => {
          const name = node.data.mechanicName as string | undefined;
          if (name && (useAppStore.getState().tables.mechanics as Record<string, unknown>)?.[name]) openMechanic(name);
        }}
        onPaneClick={onPaneClick}
        onDragOver={onDragOver}
        onDrop={onDrop}
        nodeTypes={nodeTypes}
        defaultEdgeOptions={defaultEdgeOptions}
        onlyRenderVisibleElements
        fitView
        minZoom={0.1}
        maxZoom={2}
        nodesDraggable={!smallScreen && interactive}
        nodesConnectable={!smallScreen && interactive}
        onMoveEnd={(_, viewport) => { cameras.set(graphKey, viewport); if (cameras.size > 50) cameras.delete(cameras.keys().next().value!); }}
        deleteKeyCode={smallScreen || !interactive ? null : ['Backspace', 'Delete']}
        multiSelectionKeyCode="Shift"
        selectionKeyCode="Shift"
        style={{ background: 'var(--bg-canvas)' }}
      >
        <FitLoadedGraph />
        <Background
          gap={26}
          size={1}
          color="oklch(0.32 0.014 255)"
        />
      </ReactFlow>
      <CanvasToolbar />
      {smallScreen && <p className="mobile-graph-notice" role="status">Graph movement and connections are read only on this screen. Open on desktop for full authoring.</p>}
      {edgeError && (
        <div
          style={{
            position: 'absolute',
            top: 60,
            left: '50%',
            transform: 'translateX(-50%)',
            background: 'var(--danger-dim)',
            color: 'var(--danger)',
            border: '1px solid var(--danger)',
            borderRadius: '8px',
            padding: '8px 16px',
            fontSize: '12px',
            fontWeight: 500,
            zIndex: 50,
            fontFamily: 'var(--mono)',
          }}
        >
          {edgeError}
          <button
            onClick={() => setEdgeError(null)}
            style={{
              marginLeft: 12,
              background: 'none',
              border: 'none',
              color: 'var(--danger)',
              cursor: 'pointer',
              fontSize: 14,
              fontWeight: 700,
              padding: 0,
            }}
          >
            ×
          </button>
        </div>
      )}
      <div className="canvas-hint">drag nodes · scroll to zoom · hold space to pan</div>
    </div>
  );
}

export default function SlotCanvas() { return <ReactFlowProvider><CanvasSurface /></ReactFlowProvider>; }
