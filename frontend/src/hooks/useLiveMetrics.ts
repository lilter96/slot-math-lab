import { useState, useEffect, useRef, useCallback } from 'react';
import { useAppStore, type GraphNode, type GraphEdge } from '../store';

// ═══════════════════════════════════════════════════════════════════
// Types
// ═══════════════════════════════════════════════════════════════════

export interface LiveMetric {
  rtp: number;
  hitFrequency: number;
  volatility: number;
  stdErr?: number;
  ci95?: string;
  provenance: 'Exact' | 'Sampled' | 'NeedsFullRun' | 'Error';
  sampleCount?: number;
  elapsedMs?: number;
  strategy?: string;
}

export interface LiveMetricsState {
  /** Overall metrics */
  overall: LiveMetric | null;
  /** Per-edge metrics keyed by edge ID */
  perEdge: Record<string, LiveMetric>;
  /** Loading state */
  loading: boolean;
  /** Error from last call */
  error: string | null;
  /** Timestamp of last update */
  lastUpdated: number | null;
}

const EMPTY: LiveMetricsState = {
  overall: null,
  perEdge: {},
  loading: false,
  error: null,
  lastUpdated: null,
};

// ═══════════════════════════════════════════════════════════════════
// Hook
// ═══════════════════════════════════════════════════════════════════

/** Build a config object from the current graph state. Returns null if not enough data. */
function buildConfigPayload(
  nodes: GraphNode[],
  edges: GraphEdge[],
  symbols: { id: string; name: string; kind: string; color: string }[],
  expressions: Record<string, string>,
): Record<string, unknown> | null {
  if (nodes.length === 0) return null;

  return {
    schemaVersion: '1.0.0',
    name: 'Live Preview',
    symbols: symbols.length > 0 ? symbols.map((s) => ({
      id: s.id,
      name: s.name,
      kind: s.kind,
    })) : undefined,
    boardConfig: { rows: 3, columns: 5 },
    nodes: nodes.map((n) => ({
      id: n.id,
      label: n.data.label,
      inputs: {},
      outputs: { out: { name: 'out', type: 'Weights' } },
    })),
    edges: edges.map((e) => ({
      id: e.id,
      sourceNodeId: e.source,
      sourcePort: 'out',
      targetNodeId: e.target,
      targetPort: 'in',
    })),
    expressions: Object.keys(expressions).length > 0 ? expressions : undefined,
  };
}

export function useLiveMetrics(
  symbols: { id: string; name: string; kind: string; color: string }[] = [],
  expressions: Record<string, string> = {},
) {
  const nodes = useAppStore((s) => s.nodes);
  const edges = useAppStore((s) => s.edges);
  const [state, setState] = useState<LiveMetricsState>(EMPTY);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const abortRef = useRef<AbortController | null>(null);
  const seqRef = useRef(0);

  const evaluate = useCallback(async () => {
    const payload = buildConfigPayload(nodes, edges, symbols, expressions);
    if (!payload) {
      setState(EMPTY);
      return;
    }

    // Cancel previous request
    if (abortRef.current) abortRef.current.abort();
    const controller = new AbortController();
    abortRef.current = controller;
    const seq = ++seqRef.current;

    setState((s) => ({ ...s, loading: true, error: null }));

    try {
      const res = await fetch('/api/evaluate/light', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ config: payload, maxBranches: 10_000, sampleSize: 5_000 }),
        signal: controller.signal,
      });

      if (!res.ok) throw new Error(`HTTP ${res.status}`);

      const data = await res.json();

      // Stale response guard
      if (seq !== seqRef.current) return;

      const provenance: LiveMetric['provenance'] =
        data.strategy === 'Exact' ? 'Exact' :
        data.strategy === 'Sampled' ? 'Sampled' :
        data.strategy === 'NeedsFullRun' ? 'NeedsFullRun' : 'Error';

      const overall: LiveMetric = {
        rtp: data.rtp ?? 0,
        hitFrequency: data.hitFrequency ?? 0,
        volatility: data.volatility ?? 0,
        stdErr: data.stdErr,
        ci95: data.ci95,
        provenance,
        sampleCount: data.sampleCount,
        elapsedMs: data.elapsedMs,
        strategy: data.strategy,
      };

      setState({
        overall,
        perEdge: {},
        loading: false,
        error: data.strategy === 'Error' ? (data.provenance ?? 'Evaluation failed') : null,
        lastUpdated: Date.now(),
      });
    } catch (err: unknown) {
      if (err instanceof DOMException && err.name === 'AbortError') return;
      if (seq !== seqRef.current) return;
      setState((s) => ({
        ...s,
        loading: false,
        error: err instanceof Error ? err.message : 'Evaluation failed',
        lastUpdated: Date.now(),
      }));
    }
  }, [nodes, edges, symbols, expressions]);

  // Debounced evaluation — fires 400ms after last change
  useEffect(() => {
    if (timerRef.current) clearTimeout(timerRef.current);
    timerRef.current = setTimeout(evaluate, 400);
    return () => {
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, [evaluate]);

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      if (abortRef.current) abortRef.current.abort();
    };
  }, []);

  return state;
}
