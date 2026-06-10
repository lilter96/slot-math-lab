import { useState, useEffect, useRef, useMemo, useCallback } from 'react';
import { useAppStore } from '../store';
import { buildConfigPayload } from '../lib/configPayload';

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

  // Serialize the payload once per change.  Debouncing keys off this
  // string, so re-renders with unchanged content (or unstable [] / {}
  // default-parameter references) never trigger extra requests.
  const payloadJson = useMemo(() => {
    const payload = buildConfigPayload(nodes, edges, symbols, {
      name: 'Live Preview',
      expressions,
    });
    return payload ? JSON.stringify(payload) : null;
  }, [nodes, edges, symbols, expressions]);

  const evaluate = useCallback(async (configJson: string | null) => {
    if (!configJson) {
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
        body: `{"config":${configJson},"maxBranches":10000,"sampleSize":5000}`,
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
  }, []);

  // Debounced evaluation — fires 400ms after the last *content* change.
  useEffect(() => {
    if (timerRef.current) clearTimeout(timerRef.current);
    timerRef.current = setTimeout(() => void evaluate(payloadJson), 400);
    return () => {
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, [payloadJson, evaluate]);

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      if (abortRef.current) abortRef.current.abort();
    };
  }, []);

  return state;
}
