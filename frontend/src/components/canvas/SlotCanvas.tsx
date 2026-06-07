import { useCallback, useRef } from 'react';
import {
  ReactFlow,
  ReactFlowProvider,
  Background,
  Controls,
  type Connection,
  type Node,
} from '@xyflow/react';
import { useAppStore, NODE_DEFAULTS, type GraphNodeData } from '../../store';
import { nodeTypes } from './nodes';
import CanvasToolbar from './CanvasToolbar';
import type { GraphNode } from '../../store';

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
      const defaults = NODE_DEFAULTS[type] ?? {};
      const wrapper = reactFlowWrapper.current;
      if (!wrapper) return;
      const bounds = wrapper.getBoundingClientRect();
      const id = `node-${++idCounter.current}`;
      const newNode: GraphNode = {
        id,
        type: type as string,
        position: { x: e.clientX - bounds.left - 80, y: e.clientY - bounds.top - 20 },
        data: { nodeType: type as GraphNodeData['nodeType'], label: defaults.label ?? type, ...defaults },
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
        onPaneClick={onPaneClick}
        onDragOver={onDragOver}
        onDrop={onDrop}
        nodeTypes={nodeTypes}
        defaultEdgeOptions={defaultEdgeOptions}
        fitView
        deleteKeyCode={['Backspace', 'Delete']}
        multiSelectionKeyCode="Shift"
        selectionKeyCode="Shift"
        style={{ background: 'var(--bg-canvas)' }}
      >
        <Background
          gap={26}
          size={1}
          color="oklch(0.32 0.014 255)"
        />
        <Controls
          style={{
            background: 'var(--bg-1)',
            border: '1px solid var(--line)',
            borderRadius: '9px',
            boxShadow: 'var(--shadow)',
          }}
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
