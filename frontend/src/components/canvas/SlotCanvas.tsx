import { useCallback, useRef, useEffect } from 'react';
import {
  ReactFlow,
  ReactFlowProvider,
  Background,
  useNodesInitialized,
  useReactFlow,
  type Connection,
  type Node,
} from '@xyflow/react';
import { useAppStore, NODE_DEFAULTS, type GraphNodeData } from '../../store';
import { nodeTypes } from './nodes';
import CanvasToolbar from './CanvasToolbar';
import type { GraphNode } from '../../store';
import { openMechanic } from '../../lib/projectFiles';

function FitLoadedGraph() {
  const initialized = useNodesInitialized();
  const { fitView } = useReactFlow();
  const graphKey = useAppStore(s => `${s.configName}:${s.graphTrail.map(x => x.mechanic).join('/')}:${s.nodes.map(x => x.id).join(',')}`);
  useEffect(() => {
    if (!initialized) return;
    const frame = requestAnimationFrame(() => { void fitView({ padding: 0.15, maxZoom: 1 }); });
    return () => cancelAnimationFrame(frame);
  }, [initialized, graphKey, fitView]);
  return null;
}

const defaultEdgeOptions = {
  type: 'default',
  style: {
    stroke: 'oklch(0.40 0.016 255)',
    strokeWidth: 2,
  },
};

export default function SlotCanvas() {
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
  const idCounter = useRef(0);

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
      const bounds = wrapper.getBoundingClientRect();
      const id = `node-${++idCounter.current}`;

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
        position: { x: e.clientX - bounds.left - 80, y: e.clientY - bounds.top - 20 },
        data: {
          nodeType: type as GraphNodeData['nodeType'],
          label: defaults.label ?? type,
          ...defaults,
          ...extra,
        },
      };
      addNode(newNode);
    },
    [addNode],
  );

  return (
    <ReactFlowProvider>
    <div ref={reactFlowWrapper} className="canvas-wrap" style={{ flex: 1 }}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
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
        fitView
        minZoom={0.1}
        maxZoom={2}
        deleteKeyCode={['Backspace', 'Delete']}
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
    </ReactFlowProvider>
  );
}
