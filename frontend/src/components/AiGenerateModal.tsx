import { useState, useCallback, useRef, useEffect } from 'react';
import { Ic } from './Icons';
import { useAppStore } from '../store';
import type { GraphNode, GraphEdge } from '../store';

interface AiGenerateModalProps {
  onClose: () => void;
}

interface BackendNode {
  nodeType: string;
  id: string;
  label?: string;
  mechanicName?: string;
  parameters?: Record<string, string>;
  drawWeights?: Array<{ outcomeId: string; weight: number; value: number }>;
  stateKey?: string;
  maxIterations?: number;
  stopConditionId?: string;
  conditionId?: string;
  transformId?: string;
  weightExpressionId?: string;
  stateWriteKey?: string;
  expressionId?: string;
  outputKey?: string;
  metricId?: string;
  winStateKey?: string;
}

interface BackendEdge {
  id: string;
  sourceNodeId: string;
  sourcePort: string;
  targetNodeId: string;
  targetPort: string;
}

interface GeneratedConfig {
  name?: string;
  nodes?: BackendNode[];
  edges?: BackendEdge[];
}

/** Map a backend nodeType string to the frontend canvas node type */
function backendNodeTypeToFrontend(nodeType: string): GraphNode['type'] {
  switch (nodeType) {
    case 'draw': return 'draw';
    case 'loop': return 'loop';
    case 'branch': return 'branch';
    case 'map': return 'map';
    case 'metricsSink': return 'sink';
    case 'library': return 'library';
    case 'getState':
    case 'putState':
    case 'modifyState':
      return 'state';
    default: return 'draw';
  }
}

/** Map a backend node to GraphNodeData for the canvas store */
function mapBackendNodeToGraphNode(n: BackendNode, index: number): GraphNode {
  const frontendType = backendNodeTypeToFrontend(n.nodeType);

  // Position nodes in a simple left-to-right layout
  const x = 80 + index * 200;
  const y = 200 + (index % 2 === 0 ? 0 : 60);

  const base: GraphNode = {
    id: n.id,
    type: frontendType,
    position: { x, y },
    data: {
      label: n.label ?? n.id,
      nodeType: frontendType as GraphNode['data']['nodeType'],
      sub: '',
      level: 'a' as const,
    },
  };

  switch (n.nodeType) {
    case 'draw':
      return {
        ...base,
        data: {
          ...base.data,
          sub: 'Weighted choice',
          drawWeights: n.drawWeights,
          weightExpressionId: n.weightExpressionId,
          stateWriteKey: n.stateWriteKey,
        },
      };

    case 'loop':
      return {
        ...base,
        data: {
          ...base.data,
          sub: 'Fixpoint + stop',
          iterations: n.maxIterations ?? 5,
          terminationExpr: n.stopConditionId ?? '',
        },
      };

    case 'branch':
      return {
        ...base,
        data: {
          ...base.data,
          sub: 'Bind + conditional',
          expression: n.conditionId ?? '',
        },
      };

    case 'map':
      return {
        ...base,
        data: {
          ...base.data,
          sub: 'Transform result',
          transformId: n.transformId ?? '',
        },
      };

    case 'library':
      return {
        ...base,
        data: {
          ...base.data,
          sub: 'Catalog mechanic',
          nodeType: 'library',
          mechanicName: n.mechanicName ?? '',
        },
      };

    case 'getState':
      return {
        ...base,
        data: {
          ...base.data,
          sub: 'Get / Put / Modify',
          nodeType: 'state',
          stateOp: 'get' as const,
          stateKey: n.stateKey ?? '',
        },
      };

    case 'putState':
      return {
        ...base,
        data: {
          ...base.data,
          sub: 'Get / Put / Modify',
          nodeType: 'state',
          stateOp: 'put' as const,
          stateKey: n.stateKey ?? '',
        },
      };

    case 'modifyState':
      return {
        ...base,
        data: {
          ...base.data,
          sub: 'Get / Put / Modify',
          nodeType: 'state',
          stateOp: 'modify' as const,
          expression: n.expressionId ?? '',
          stateKey: n.outputKey ?? '',
        },
      };

    case 'metricsSink':
      return {
        ...base,
        data: {
          ...base.data,
          sub: 'Metrics output',
          nodeType: 'sink',
        },
      };

    default:
      return base;
  }
}

export default function AiGenerateModal({ onClose }: AiGenerateModalProps) {
  const [prompt, setPrompt] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const addNode = useAppStore((s) => s.addNode);
  const setEdges = useCallback((edges: GraphEdge[]) => {
    // Replace edges by setting them directly
    useAppStore.setState({ edges });
  }, []);
  const existingEdges = useAppStore((s) => s.edges);
  const setConfigName = useAppStore((s) => s.setConfigName);

  // Focus textarea on mount
  useEffect(() => {
    textareaRef.current?.focus();
  }, []);

  // Close on Escape key
  useEffect(() => {
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', handleKey);
    return () => window.removeEventListener('keydown', handleKey);
  }, [onClose]);

  const handleGenerate = useCallback(async () => {
    if (!prompt.trim()) return;

    setLoading(true);
    setError(null);

    try {
      const res = await fetch('/api/ai/generate-graph', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ prompt: prompt.trim() }),
      });

      if (res.status === 503) {
        setError('AI service is not configured on this server (ANTHROPIC__APIKEY not set).');
        return;
      }

      const data: unknown = await res.json();

      if (!res.ok) {
        // 422 with compiler errors or 400
        const errData = data as { error?: string; compilerErrors?: Array<{ message: string }> };
        const msgs = errData.compilerErrors?.map((e) => e.message).join('\n') ?? errData.error ?? 'Unknown error';
        setError(msgs);
        return;
      }

      // Success — data is a GraphConfig
      const config = data as GeneratedConfig;
      const nodes = config.nodes ?? [];
      const edges = config.edges ?? [];

      // Load name
      if (config.name) {
        setConfigName(config.name);
      }

      // Add nodes to canvas
      nodes.forEach((n, i) => {
        addNode(mapBackendNodeToGraphNode(n, i));
      });

      // Add edges (merge with existing)
      const newEdges: GraphEdge[] = edges.map((e) => ({
        id: e.id,
        source: e.sourceNodeId,
        target: e.targetNodeId,
        sourceHandle: e.sourcePort,
        targetHandle: e.targetPort,
        type: 'provenance',
      }));
      setEdges([...existingEdges, ...newEdges]);

      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Request failed');
    } finally {
      setLoading(false);
    }
  }, [prompt, addNode, setEdges, existingEdges, setConfigName, onClose]);

  const handleKeyDown = useCallback((e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
      void handleGenerate();
    }
  }, [handleGenerate]);

  return (
    <div
      style={{
        position: 'fixed',
        inset: 0,
        background: 'rgba(0,0,0,0.55)',
        zIndex: 200,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
      }}
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
      role="dialog"
      aria-modal="true"
      aria-label="AI Generate Graph"
    >
      <div
        style={{
          background: 'var(--bg-1)',
          border: '1px solid var(--line)',
          borderRadius: 'var(--radius-lg)',
          boxShadow: 'var(--shadow-pop)',
          width: '520px',
          maxWidth: '90vw',
          display: 'flex',
          flexDirection: 'column',
          gap: 0,
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div
          style={{
            padding: '14px 16px',
            borderBottom: '1px solid var(--line)',
            display: 'flex',
            alignItems: 'center',
            gap: 10,
          }}
        >
          <span
            style={{
              display: 'grid',
              placeItems: 'center',
              width: 28,
              height: 28,
              borderRadius: 8,
              background: 'var(--bg-3)',
              color: 'var(--exact)',
              flexShrink: 0,
            }}
          >
            <Ic.ai style={{ width: 16, height: 16 }} />
          </span>
          <div>
            <div style={{ fontWeight: 600, fontSize: 13 }}>AI Generate Graph</div>
            <div style={{ fontSize: 11, color: 'var(--faint)', fontFamily: 'var(--mono)' }}>
              describe your slot game in plain English
            </div>
          </div>
          <button
            onClick={onClose}
            style={{
              marginLeft: 'auto',
              background: 'none',
              border: 'none',
              color: 'var(--muted)',
              cursor: 'pointer',
              padding: 4,
              borderRadius: 6,
              display: 'grid',
              placeItems: 'center',
            }}
            title="Close"
            aria-label="Close"
          >
            <Ic.x style={{ width: 16, height: 16 }} />
          </button>
        </div>

        {/* Body */}
        <div style={{ padding: '16px' }}>
          <div className="field">
            <label htmlFor="ai-prompt">Game description</label>
            <textarea
              id="ai-prompt"
              ref={textareaRef}
              className="inp"
              value={prompt}
              onChange={(e) => setPrompt(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="e.g. A 5-reel 3-row slot with 20 paylines, cherries, lemons, bells and a wild. Include a scatter that triggers 10 free spins."
              rows={5}
              style={{ resize: 'vertical', minHeight: 90 }}
              disabled={loading}
            />
            <div className="hint">
              Ctrl+Enter to generate. The AI will produce a graph ready to evaluate.
            </div>
          </div>

          {error && (
            <div
              style={{
                background: 'var(--danger-dim)',
                border: '1px solid var(--danger)',
                borderRadius: 'var(--radius)',
                padding: '10px 12px',
                fontSize: 12,
                color: 'var(--danger)',
                fontFamily: 'var(--mono)',
                whiteSpace: 'pre-wrap',
                wordBreak: 'break-word',
                marginTop: 4,
              }}
            >
              {error}
            </div>
          )}
        </div>

        {/* Footer */}
        <div
          style={{
            padding: '12px 16px',
            borderTop: '1px solid var(--line)',
            display: 'flex',
            gap: 10,
            justifyContent: 'flex-end',
            alignItems: 'center',
          }}
        >
          {loading && (
            <span style={{ fontSize: 11, color: 'var(--faint)', fontFamily: 'var(--mono)' }}>
              generating…
            </span>
          )}
          <button className="btn" onClick={onClose} disabled={loading}>
            Cancel
          </button>
          <button
            className="btn primary"
            onClick={handleGenerate}
            disabled={loading || !prompt.trim()}
          >
            <Ic.ai style={{ width: 14, height: 14 }} />
            {loading ? 'Generating…' : 'Generate'}
          </button>
        </div>
      </div>
    </div>
  );
}
