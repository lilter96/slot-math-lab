import { useState, useEffect, useRef, useMemo, useCallback } from 'react';
import { useAppStore } from '../store';
import { calculationRequest } from '../lib/measurements/requests';
import { buildConfigPayload } from '../lib/configPayload';

// ═══════════════════════════════════════════════════════════════════
// Types
// ═══════════════════════════════════════════════════════════════════


interface LightResponse { rtp?: number; hitFrequency?: number; volatility?: number; stdErr?: number; sampleCount?: number; elapsedMs?: number; strategy?: string; ci95?: string; lo?: number; hi?: number; prunedMass?: number; provenance?: string; errors?: { message: string }[] }
export interface LiveMetric {
  rtp?: number;
  hitFrequency?: number;
  volatility?: number;
  stdErr?: number;
  ci95?: string;
  lo?: number;
  hi?: number;
  prunedMass?: number;
  provenance: 'Exact' | 'ExactInterval' | 'ExactWithMassLoss' | 'Sampled' | 'NeedsFullRun' | 'Error';
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
  statusMessage?: string;
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
  expressions: Record<string, string> = {},
) {
  const nodes = useAppStore((s) => s.nodes);
  const edges = useAppStore((s) => s.edges);
  const tables = useAppStore((s) => s.tables);
  const trail = useAppStore(s => s.graphTrail);
  const [state, setState] = useState<LiveMetricsState & { configJson?: string | null }>(EMPTY);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const abortRef = useRef<AbortController | null>(null);
  const seqRef = useRef(0);

  const payloadJson = useMemo(() => {
    if (tables.uiAnalysisKind === 'expectationProof' || trail.length) return null;
    try {
    const payload = buildConfigPayload(nodes, edges, {
      name: 'Live Preview',
      tables,
      expressions,
    });
    return payload ? JSON.stringify(payload) : null;
    } catch { return JSON.stringify({ schemaVersion: '1.0.0', name: 'Invalid expression', nodes: [] }); }
  }, [nodes, edges, expressions, tables, trail]);

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

    setState({ ...EMPTY, loading: true, configJson });

    try {
      const data = await calculationRequest<LightResponse>('/api/evaluate/light', { config: JSON.parse(configJson), maxBranches: 10000, sampleSize: 5000 }, controller.signal, message => {
        if (seq === seqRef.current) setState(current => ({ ...current, statusMessage: message.replace('Cancel to stop waiting.', 'Switch views to stop waiting.') }));
      }, 10000);

      // Stale response guard
      if (seq !== seqRef.current) return;

      const provenance: LiveMetric['provenance'] =
        data.provenance === 'ExactWithMassLoss' ? 'ExactWithMassLoss' :
        data.provenance === 'ExactInterval' ? 'ExactInterval' :
        data.strategy === 'Exact' ? 'Exact' :
        data.strategy === 'Sampled' ? 'Sampled' :
        data.strategy === 'NeedsFullRun' ? 'NeedsFullRun' : 'Error';

      const overall: LiveMetric = {
        rtp: typeof data.rtp === 'number' && Number.isFinite(data.rtp) ? data.rtp : undefined,
        hitFrequency: typeof data.hitFrequency === 'number' && Number.isFinite(data.hitFrequency) ? data.hitFrequency : undefined,
        volatility: typeof data.volatility === 'number' && Number.isFinite(data.volatility) ? data.volatility : undefined,
        stdErr: (data.sampleCount ?? 0) >= 2 && typeof data.stdErr === 'number' && Number.isFinite(data.stdErr) && data.stdErr >= 0 ? data.stdErr : undefined,
        ci95: (data.sampleCount ?? 0) >= 2 && typeof data.ci95 === 'string' ? data.ci95 : undefined,
        lo: typeof data.lo === 'number' && Number.isFinite(data.lo) ? data.lo : undefined,
        hi: typeof data.hi === 'number' && Number.isFinite(data.hi) ? data.hi : undefined,
        prunedMass: typeof data.prunedMass === 'number' && Number.isFinite(data.prunedMass) ? data.prunedMass : undefined,
        provenance,
        sampleCount: Number.isSafeInteger(data.sampleCount) && (data.sampleCount ?? 0) > 0 ? data.sampleCount : undefined,
        elapsedMs: data.elapsedMs,
        strategy: data.strategy,
      };

      setState({
        configJson,
        overall: provenance === 'Error' ? null : overall,
        perEdge: {},
        loading: false,
        error: data.strategy === 'Error' ? (data.errors?.map((error: { message: string }) => error.message).join('; ') || data.provenance || 'Evaluation failed') : null,
        lastUpdated: Date.now(),
      });
    } catch (err: unknown) {
      if (err instanceof DOMException && err.name === 'AbortError') return;
      if (seq !== seqRef.current) return;
      setState((s) => ({
        ...s,
        overall: null,
        perEdge: {},
        loading: false,
        error: err instanceof Error ? err.message : 'Evaluation failed',
        lastUpdated: Date.now(),
      }));
    }
  }, []);

  // Debounced evaluation — fires 400ms after the last *content* change.
  useEffect(() => {
    ++seqRef.current;
    abortRef.current?.abort();
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

  return state.configJson === payloadJson ? state : { ...EMPTY, loading: !!payloadJson };
}
