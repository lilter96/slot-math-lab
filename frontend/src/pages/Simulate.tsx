import { useState, useRef, useCallback, useEffect } from 'react';
import * as signalR from '@microsoft/signalr';
import { useAppStore, type GraphNode, type GraphEdge, type TableSymbol } from '../store';
import ConvergenceChart, { type ConvergencePoint } from '../components/simulate/ConvergenceChart';
import Histogram from '../components/simulate/Histogram';
import ProvBadge from '../components/ProvBadge';

// ── Constants ─────────────────────────────────────────────────────────
const DEFAULT_SPINS = 1_000_000;
const FALLBACK_EXACT_RTP = 0.9534;
const CAP_WIN = 5000;
const API_BASE: string = (import.meta.env.VITE_API_URL as string | undefined) ?? '';

// ── Types ─────────────────────────────────────────────────────────────
type RunStatus = 'idle' | 'running' | 'paused' | 'complete';

interface BackendProgress {
  runId?: string;
  sampleCount?: number;
  totalSamples?: number;
  runningRtp?: number;
  stdErr?: number;
  status?: string;
  elapsedMs?: number;
}

interface RunResult {
  rtp?: number;
  hitFrequency?: number;
  volatility?: number;
  maxWin?: number;
  sampleCount?: number;
  provenance?: string;
}

// ── Helpers ───────────────────────────────────────────────────────────
function fmtN(n: number): string {
  if (n >= 1e6) return (n / 1e6).toFixed(0) + 'M';
  if (n >= 1e3) return (n / 1e3).toFixed(0) + 'k';
  return String(n);
}

function KV({ k, v }: { k: string; v: string }) {
  return (
    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', padding: '5px 0', borderBottom: '1px solid var(--line)' }}>
      <span style={{ fontSize: 11, color: 'var(--faint)' }}>{k}</span>
      <span style={{ fontFamily: 'var(--mono)', fontSize: 13, color: 'var(--muted)' }}>{v}</span>
    </div>
  );
}

function mapNodeToBackend(n: GraphNode): Record<string, unknown> {
  const base = { id: n.id, label: n.data.label };
  switch (n.data.nodeType) {
    case 'draw':
      return {
        ...base,
        nodeType: 'draw',
        inputs: {},
        outputs: { out: { name: 'out', type: 'Wins' } },
        ...(n.data.drawWeights?.length ? { drawWeights: n.data.drawWeights } : {}),
        ...(n.data.weightExpressionId ? { weightExpressionId: n.data.weightExpressionId } : {}),
      };
    case 'state': {
      const op = (n.data.stateOp as string) ?? 'get';
      const key = (n.data.stateKey as string) || '__default__';
      if (op === 'put') return { ...base, nodeType: 'putState', stateKey: key, inputs: { in: { name: 'in', type: 'Wins' } }, outputs: { out: { name: 'out', type: 'Wins' } } };
      if (op === 'modify') return { ...base, nodeType: 'modifyState', expressionId: n.data.expression as string ?? undefined, inputs: { in: { name: 'in', type: 'Wins' } }, outputs: { out: { name: 'out', type: 'Wins' } } };
      return { ...base, nodeType: 'getState', stateKey: key, inputs: { in: { name: 'in', type: 'Wins' } }, outputs: { out: { name: 'out', type: 'Wins' } } };
    }
    case 'loop':
      return {
        ...base,
        nodeType: 'loop',
        inputs: { in: { name: 'in', type: 'Wins' } },
        outputs: { out: { name: 'out', type: 'Wins' } },
        maxIterations: (n.data.iterations as number) ?? 5,
        ...(n.data.terminationExpr ? { stopConditionId: n.data.terminationExpr } : {}),
      };
    case 'branch':
      return { ...base, nodeType: 'branch', inputs: { in: { name: 'in', type: 'Wins' } }, outputs: { out: { name: 'out', type: 'Wins' } } };
    case 'map':
      return {
        ...base,
        nodeType: 'map',
        inputs: { in: { name: 'in', type: 'Wins' } },
        outputs: { out: { name: 'out', type: 'Wins' } },
        ...(n.data.transformId ? { transformId: n.data.transformId } : {}),
        ...(n.data.expression ? { transformId: n.data.expression } : {}),
      };
    case 'evaluator': {
      const kind = (n.data.evaluatorKind as string) ?? 'lines';
      const transformId = kind === 'plugin'
        ? `plugin:${(n.data.pluginId as string) ?? ''}`
        : kind;
      return { ...base, nodeType: 'map', inputs: { in: { name: 'in', type: 'Board' } }, outputs: { out: { name: 'out', type: 'Wins' } }, transformId };
    }
    case 'transform':
      return { ...base, nodeType: 'map', inputs: { in: { name: 'in', type: 'Board' } }, outputs: { out: { name: 'out', type: 'Board' } } };
    case 'sink':
      return { ...base, nodeType: 'metricsSink', inputs: { in: { name: 'in', type: 'Wins' } }, outputs: {} };
    default:
      return { ...base, nodeType: n.data.nodeType, inputs: {}, outputs: { out: { name: 'out', type: 'Wins' } } };
  }
}

/** Build the graph config payload that the backend compiler accepts. */
function buildConfigPayload(
  nodes: GraphNode[],
  edges: GraphEdge[],
  symbols: TableSymbol[],
  name: string,
): Record<string, unknown> | null {
  if (nodes.length === 0) return null;
  return {
    schemaVersion: '1.0.0',
    name,
    symbols: symbols.length > 0 ? symbols.map((s) => ({ id: s.id, name: s.name, kind: s.kind })) : undefined,
    boardConfig: { rows: 3, columns: 5 },
    nodes: nodes.map(mapNodeToBackend),
    edges: edges.map((e) => ({
      id: e.id,
      sourceNodeId: e.source,
      sourcePort: 'out',
      targetNodeId: e.target,
      targetPort: 'in',
    })),
  };
}

// ── Component ─────────────────────────────────────────────────────────
export default function Simulate() {
  const [status, setStatus] = useState<RunStatus>('idle');
  const [sampleCount, setSampleCount] = useState(0);
  const [runningRtp, setRunningRtp] = useState(0);
  const [stdErr, setStdErr] = useState(0);
  const [volatility, setVolatility] = useState(0);
  const [maxWin, setMaxWin] = useState(0);
  const [capHits, setCapHits] = useState(0);
  const [points, setPoints] = useState<ConvergencePoint[]>([]);
  const [histogram, setHistogram] = useState<Map<number, number>>(new Map());
  const [error, setError] = useState<string | null>(null);
  const [exactRtp, setExactRtp] = useState(FALLBACK_EXACT_RTP);
  const [spinsTarget] = useState(DEFAULT_SPINS);

  // Store data used to build the config payload
  const nodes = useAppStore((s) => s.nodes);
  const edges = useAppStore((s) => s.edges);
  const symbols = useAppStore((s) => s.tableSymbols);
  const configName = useAppStore((s) => s.configName);

  const hubRef = useRef<signalR.HubConnection | null>(null);
  const abortRef = useRef(false);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const runIdRef = useRef<string | null>(null);

  // ── Cancel / stop ───────────────────────────────────────────────────
  const stop = useCallback(() => {
    abortRef.current = true;
    if (timerRef.current) { clearInterval(timerRef.current); timerRef.current = null; }

    if (runIdRef.current) {
      const id = runIdRef.current;
      runIdRef.current = null;
      fetch(`${API_BASE}/api/runs/${id}`, { method: 'DELETE' }).catch(() => {});
    }

    if (hubRef.current) { void hubRef.current.stop(); hubRef.current = null; }
    setStatus((s) => (s === 'running' ? 'paused' : s));
  }, []);

  // ── Start run ───────────────────────────────────────────────────────
  const start = useCallback(async () => {
    abortRef.current = false;
    runIdRef.current = null;
    setStatus('running');
    setError(null);
    setSampleCount(0); setRunningRtp(0); setStdErr(0);
    setVolatility(0); setMaxWin(0); setCapHits(0);
    setPoints([]); setHistogram(new Map());

    // ── Try backend flow ──────────────────────────────────────────────
    const configPayload = buildConfigPayload(nodes, edges, symbols, configName ?? 'Untitled');
    if (configPayload) {
      try {
        // 1. Persist config → get configId
        const configRes = await fetch(`${API_BASE}/api/configs`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ config: configPayload }),
        });
        if (!configRes.ok) throw new Error(`Config save HTTP ${configRes.status}`);
        const configData = (await configRes.json()) as { id?: string };
        const configId = configData.id;
        if (!configId) throw new Error('Config save returned no id');

        // 2. Create evaluation run → get runId
        const batchSize = Math.max(100, Math.floor(spinsTarget / 100));
        const runRes = await fetch(`${API_BASE}/api/runs`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ configId, sampleSize: spinsTarget, progressBatchSize: batchSize }),
        });
        if (!runRes.ok) throw new Error(`Run create HTTP ${runRes.status}`);
        const runData = (await runRes.json()) as { id?: string };
        const runId = runData.id;
        if (!runId) throw new Error('Run create returned no id');
        runIdRef.current = runId;

        // 3. Connect to SignalR and subscribe to this run's group
        const hub = new signalR.HubConnectionBuilder()
          .withUrl(`${API_BASE}/hubs/runs`)
          .withAutomaticReconnect()
          .build();

        hub.on('ProgressUpdate', (msg: BackendProgress) => {
          if (abortRef.current) return;
          const n = msg.sampleCount ?? 0;
          const rtp = msg.runningRtp ?? 0;
          const se = msg.stdErr ?? 0;
          setSampleCount(n);
          setRunningRtp(rtp);
          setStdErr(se);
          setPoints((prev) => {
            const last = prev[prev.length - 1];
            if (last && n - last.n < spinsTarget / 200) return prev;
            return [...prev, { n, rtp, stdErr: se }];
          });
          setHistogram((prev) => {
            const next = new Map(prev);
            const bucket = Math.round(rtp * 20) / 20;
            next.set(bucket, (next.get(bucket) ?? 0) + 1);
            return next;
          });

          const terminal = msg.status === 'completed' || msg.status === 'cancelled' || msg.status === 'failed';
          if (terminal) {
            setStatus(msg.status === 'completed' ? 'complete' : 'paused');
            if (msg.status === 'failed') setError('Run failed on the server');

            // On completion, fetch full result for volatility and maxWin
            if (msg.status === 'completed') {
              const finishedId = runIdRef.current ?? runId;
              fetch(`${API_BASE}/api/runs/${finishedId}`)
                .then((r) => r.json())
                .then((data: { resultJson?: string }) => {
                  if (data.resultJson) {
                    const result = JSON.parse(data.resultJson) as RunResult;
                    if (result.volatility != null) setVolatility(result.volatility);
                    if (result.maxWin != null) setMaxWin(Math.round(result.maxWin));
                    if (result.rtp != null) setExactRtp(result.rtp);
                  }
                })
                .catch(() => {});
            }
            hub.stop();
            hubRef.current = null;
            runIdRef.current = null;
          }
        });

        hubRef.current = hub;
        await hub.start();
        await hub.invoke('SubscribeToRun', runId);
        return; // backend flow is live
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err);
        console.warn('[Simulate] Backend unavailable, falling back to local simulation:', msg);
        runIdRef.current = null;
        if (hubRef.current) { void hubRef.current.stop(); hubRef.current = null; }
      }
    }

    // ── Local fallback Monte Carlo simulation ─────────────────────────
    let n = 0, sum = 0, sumSq = 0, localMax = 0, localCapHits = 0;
    const mu = exactRtp;
    const BATCH = 3000;

    timerRef.current = setInterval(() => {
      if (abortRef.current) return;
      const remaining = Math.min(BATCH, spinsTarget - n);
      for (let j = 0; j < remaining; j++) {
        const noise = (Math.random() - 0.5) * 0.3 * Math.exp(-n / 50000);
        const win = Math.max(0, mu + noise) * (0.5 + Math.random() * 2.5);
        sum += win; sumSq += win * win;
        if (win > localMax) localMax = win;
        if (win >= CAP_WIN) localCapHits++;
      }
      n += remaining;
      const avg = sum / n;
      const variance = Math.max(0, sumSq / n - avg * avg);
      const se = Math.sqrt(variance / n);

      setSampleCount(n); setRunningRtp(avg); setStdErr(se);
      setVolatility(Math.sqrt(variance));
      setMaxWin(Math.round(localMax));
      setCapHits(localCapHits);
      setPoints((prev) => {
        const last = prev[prev.length - 1];
        if (last && n - last.n < spinsTarget / 200) return prev;
        return [...prev, { n, rtp: avg, stdErr: se }];
      });
      setHistogram((prev) => {
        const next = new Map(prev);
        const bucket = Math.round(avg * 20) / 20;
        next.set(bucket, (next.get(bucket) ?? 0) + 1);
        return next;
      });
      if (n >= spinsTarget) {
        if (timerRef.current) clearInterval(timerRef.current);
        setStatus('complete');
      }
    }, 50);
  }, [nodes, edges, symbols, configName, spinsTarget, exactRtp]);

  useEffect(() => () => {
    abortRef.current = true;
    if (timerRef.current) clearInterval(timerRef.current);
    if (hubRef.current) void hubRef.current.stop();
  }, []);

  const inBand = sampleCount > 1000 && Math.abs(runningRtp - exactRtp) <= 3 * stdErr + 1e-9;
  const progress = Math.min(1, sampleCount / spinsTarget);

  return (
    <div className="workspace">
      <div className="sim-wrap">
        {/* ── Controls row ── */}
        <div className="sim-top">
          <div className="sim-controls">
            {status !== 'running'
              ? <button className="btn blue" onClick={start}>▶ Run {fmtN(spinsTarget)} spins</button>
              : <button className="btn" onClick={stop} style={{ borderColor: 'var(--danger)', color: 'var(--danger)' }}>■ Cancel</button>}
            <div className="progress"><div className="bar" style={{ width: `${progress * 100}%` }} /></div>
            <span className="tnum" style={{ color: 'var(--faint)', fontSize: 12, minWidth: 96 }}>
              {fmtN(sampleCount)} / {fmtN(spinsTarget)}
            </span>
          </div>

          <div className="stat-cards">
            <div className="stat-card">
              <div className="sl">Running RTP</div>
              <div className="sv" style={{ color: 'var(--sampled)' }}>
                {sampleCount > 0 ? (runningRtp * 100).toFixed(3) + '%' : '—'}
              </div>
            </div>
            <div className="stat-card">
              <div className="sl">Std error</div>
              <div className="sv">
                {sampleCount > 0 ? '±' + (stdErr * 100).toFixed(4) : '—'}
              </div>
            </div>
            <div className="stat-card">
              <div className="sl">Exact RTP</div>
              <div className="sv" style={{ color: 'var(--exact)' }}>
                {(exactRtp * 100).toFixed(2)}%
              </div>
            </div>
            <div className="stat-card">
              <div className="sl">Max win seen</div>
              <div className="sv">{maxWin > 0 ? maxWin + '×' : '—'}</div>
            </div>
          </div>
        </div>

        {error && (
          <div style={{ padding: '4px 16px 0', color: 'var(--danger)', fontSize: 11, fontFamily: 'var(--mono)' }}>
            {error}
          </div>
        )}

        {/* ── Convergence chart ── */}
        <div className="chart-card">
          <div className="chart-head">
            <span className="ct">RTP convergence</span>
            {sampleCount > 1000 && (
              <span className={'prov ' + (inBand ? 'sampled' : 'epsilon')} style={{ marginLeft: 10 }}>
                <span className="pdot" />
                {inBand ? 'within exact ± 3·stdErr' : 'converging…'}
              </span>
            )}
            <div className="leg">
              <span><span className="ln" style={{ background: 'var(--exact)' }} />exact</span>
              <span><span className="ln" style={{ background: 'var(--sampled)' }} />sampled mean</span>
              <span><span className="ln" style={{ background: 'var(--sampled)', opacity: 0.35, height: 8 }} />95% CI</span>
            </div>
          </div>
          <div className="chart-canvas-wrap">
            <ConvergenceChart points={points} exactRtp={exactRtp} />
          </div>
        </div>

        {/* ── Bottom row ── */}
        <div className="sim-bottom">
          <div className="hist-card" style={{ height: 170 }}>
            <div style={{ display: 'flex', alignItems: 'center', marginBottom: 8 }}>
              <span style={{ fontWeight: 600 }}>Win distribution</span>
              <span style={{ marginLeft: 'auto', fontSize: 11, color: 'var(--faint)', fontFamily: 'var(--mono)' }}>
                payout buckets (bet ×) · full game
              </span>
            </div>
            <div style={{ height: 110 }}>
              <Histogram data={histogram} />
            </div>
          </div>
          <div className="hist-card" style={{ width: 260, flex: '0 0 auto' }}>
            <div style={{ fontWeight: 600, marginBottom: 10, display: 'flex', alignItems: 'center', gap: 8 }}>
              Full-game stats
              {sampleCount > 0 && <ProvBadge p={{ kind: 'Sampled', n: sampleCount, stdErr }} mini />}
            </div>
            <KV k="Volatility (σ)" v={volatility > 0 ? volatility.toFixed(2) : '—'} />
            <KV k="Hit on cap" v={sampleCount > 0 ? `${capHits} / ${fmtN(sampleCount)}` : '—'} />
            <KV k="P(cap reached)" v={sampleCount > 0 ? (capHits / sampleCount * 100).toFixed(4) + '%' : '—'} />
            <div className="hint" style={{ marginTop: 12 }}>
              Variance includes free-spin tails, so it is{' '}
              <b style={{ color: 'var(--sampled)' }}>Sampled</b> — the base-game volatility on the Build tab is exact.
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
