import { useState, useRef, useCallback, useEffect } from 'react';
import * as signalR from '@microsoft/signalr';
import { useAppStore, type GraphNode, type GraphEdge, type TableSymbol } from '../store';
import ConvergenceChart, { type ConvergencePoint } from '../components/simulate/ConvergenceChart';
import Histogram from '../components/simulate/Histogram';
import ProvBadge from '../components/ProvBadge';
import type { components } from '../api/generated-types';

type RunProgressMessage = components['schemas']['RunProgressMessage'];
type EvaluateLightResponse = components['schemas']['EvaluateLightResponse'];

// ── Run state ──────────────────────────────────────────────────────
type RunStatus = 'idle' | 'running' | 'paused' | 'complete';

// ── Backend config helpers ────────────────────────────────────────

function mapNodeToBackend(n: GraphNode): Record<string, unknown> {
  const base = { id: n.id, label: n.data.label };
  // IMPORTANT: nodeType must be FIRST — STJ's [JsonPolymorphic] requires the discriminator
  // to appear before all other properties for its streaming deserializer.
  switch (n.data.nodeType) {
    case 'draw':
      return {
        nodeType: 'draw',
        ...base,
        inputs: { in: { name: 'in', type: 'Wins' } },
        outputs: { out: { name: 'out', type: 'Wins' } },
        ...(n.data.drawWeights?.length ? { drawWeights: n.data.drawWeights } : {}),
        ...(n.data.weightExpressionId ? { weightExpressionId: n.data.weightExpressionId } : {}),
        ...(n.data.stateWriteKey ? { stateWriteKey: n.data.stateWriteKey } : {}),
      };
    case 'state': {
      const op = (n.data.stateOp as string) ?? 'get';
      const key = (n.data.stateKey as string) || '__default__';
      if (op === 'put') return { nodeType: 'putState', ...base, stateKey: key, inputs: { in: { name: 'in', type: 'Wins' } }, outputs: { out: { name: 'out', type: 'Wins' } } };
      if (op === 'modify') return { nodeType: 'modifyState', ...base, expressionId: n.data.expression as string ?? undefined, inputs: { in: { name: 'in', type: 'Wins' } }, outputs: { out: { name: 'out', type: 'Wins' } } };
      return { nodeType: 'getState', ...base, stateKey: key, inputs: { in: { name: 'in', type: 'Wins' } }, outputs: { out: { name: 'out', type: 'Wins' } } };
    }
    case 'loop':
      return {
        nodeType: 'loop',
        ...base,
        inputs: { in: { name: 'in', type: 'Wins' } },
        outputs: {
          body: { name: 'body', type: 'Wins' },
          exit: { name: 'exit', type: 'Wins' },
        },
        maxIterations: (n.data.iterations as number) ?? 5,
        ...(n.data.terminationExpr ? { stopConditionId: n.data.terminationExpr } : {}),
      };
    case 'branch':
      return {
        nodeType: 'branch',
        ...base,
        inputs: { in: { name: 'in', type: 'Wins' } },
        outputs: { out: { name: 'out', type: 'Wins' } },
        ...(n.data.expression ? { conditionId: n.data.expression } : {}),
      };
    case 'map':
      return {
        nodeType: 'map',
        ...base,
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
      return { nodeType: 'map', ...base, inputs: { in: { name: 'in', type: 'Board' } }, outputs: { out: { name: 'out', type: 'Wins' } }, transformId };
    }
    case 'transform':
      return { nodeType: 'map', ...base, inputs: { in: { name: 'in', type: 'Board' } }, outputs: { out: { name: 'out', type: 'Board' } } };
    case 'sink':
      return { nodeType: 'metricsSink', ...base, inputs: { in: { name: 'in', type: 'Wins' } }, outputs: {} };
    default:
      return { nodeType: n.data.nodeType, ...base, inputs: {}, outputs: { out: { name: 'out', type: 'Wins' } } };
  }
}

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
      sourcePort: e.sourceHandle ?? 'out',
      targetNodeId: e.target,
      targetPort: e.targetHandle ?? 'in',
    })),
  };
}

export default function Simulate() {
  const nodes = useAppStore((s) => s.nodes);
  const edges = useAppStore((s) => s.edges);
  const symbols = useAppStore((s) => s.tableSymbols);
  const configName = useAppStore((s) => s.configName);

  const [status, setStatus] = useState<RunStatus>('idle');
  const [sampleCount, setSampleCount] = useState(0);
  const [runningRtp, setRunningRtp] = useState(0);
  const [stdErr, setStdErr] = useState(0);
  const [points, setPoints] = useState<ConvergencePoint[]>([]);
  const [histogram, setHistogram] = useState<Map<number, number>>(new Map());
  const [error, setError] = useState<string | null>(null);
  const [spinsTarget, setSpinsTarget] = useState(100_000);

  // Exact RTP from /api/evaluate/light — null until fetched
  const [exactRtp, setExactRtp] = useState<number | null>(null);
  const [exactProvenance, setExactProvenance] = useState<string | null>(null);
  const [needsFullRun, setNeedsFullRun] = useState(false);

  const hubRef = useRef<signalR.HubConnection | null>(null);
  const abortRef = useRef(false);

  // ── Stop / cleanup ──────────────────────────────────────────────
  const stop = useCallback(() => {
    abortRef.current = true;
    if (hubRef.current) {
      hubRef.current.stop();
      hubRef.current = null;
    }
    if (status === 'running') setStatus('paused');
  }, [status]);

  // ── Start run ───────────────────────────────────────────────────
  const start = useCallback(async () => {
    abortRef.current = false;
    setStatus('running');
    setError(null);
    setSampleCount(0);
    setRunningRtp(0);
    setStdErr(0);
    setPoints([]);
    setHistogram(new Map());
    setExactRtp(null);
    setExactProvenance(null);
    setNeedsFullRun(false);

    const configPayload = buildConfigPayload(nodes, edges, symbols, configName ?? 'Untitled');
    if (!configPayload) {
      setError('No graph nodes. Build a graph in the Build tab first.');
      setStatus('idle');
      return;
    }

    try {
      // ── Step 1: get exact RTP from /api/evaluate/light ──────────
      try {
        const evalRes = await fetch('/api/evaluate/light', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ config: configPayload }),
        });
        if (evalRes.ok) {
          const evalData = (await evalRes.json()) as EvaluateLightResponse;
          const strat = (evalData.strategy ?? '').toLowerCase();
          if (strat === 'needsfullrun') {
            setNeedsFullRun(true);
          } else if (evalData.rtp != null) {
            setExactRtp(evalData.rtp);
            setExactProvenance(evalData.provenance ?? evalData.strategy ?? 'Exact');
          }
        }
      } catch {
        // evaluate/light failure is non-fatal — continue without exact reference
      }

      // ── Step 2: persist config ──────────────────────────────────
      const configRes = await fetch('/api/configs', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ config: configPayload }),
      });
      if (!configRes.ok) throw new Error(`Config save failed: HTTP ${configRes.status}`);
      const configData = (await configRes.json()) as { id?: string };
      const configId = configData.id;
      if (!configId) throw new Error('Config save returned no id');

      // ── Step 3: create run ──────────────────────────────────────
      const runRes = await fetch('/api/runs', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          configId,
          sampleSize: spinsTarget,
          progressBatchSize: Math.max(100, Math.floor(spinsTarget / 100)),
        }),
      });
      if (!runRes.ok) throw new Error(`Run create failed: HTTP ${runRes.status}`);
      const runData = (await runRes.json()) as { id?: string };
      const runId = runData.id;
      if (!runId) throw new Error('Run returned no id');

      // ── Step 4: SignalR streaming ───────────────────────────────
      const hub = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/runs')
        .withAutomaticReconnect()
        .build();

      hub.on('ProgressUpdate', (msg: RunProgressMessage) => {
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

        const terminal =
          msg.status === 'completed' || msg.status === 'cancelled' || msg.status === 'failed';
        if (terminal) {
          setStatus(msg.status === 'completed' ? 'complete' : 'paused');
          if (msg.status === 'failed') setError('Run failed on the server.');
          hub.stop();
          hubRef.current = null;
        }
      });

      hubRef.current = hub;
      await hub.start();
      await hub.invoke('SubscribeToRun', runId);

    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      setError(`Backend error: ${msg}. Is the backend running on this host?`);
      setStatus('idle');
      if (hubRef.current) { void hubRef.current.stop(); hubRef.current = null; }
    }
  }, [spinsTarget, nodes, edges, symbols, configName]);

  // ── Cleanup on unmount ──────────────────────────────────────────
  useEffect(() => {
    return () => {
      abortRef.current = true;
      if (hubRef.current) hubRef.current.stop();
    };
  }, []);

  const inCI =
    exactRtp != null &&
    runningRtp >= exactRtp - 1.96 * stdErr &&
    runningRtp <= exactRtp + 1.96 * stdErr;

  return (
    <div className="workspace" style={{ overflow: 'auto' }}>
      <div className="sim-wrap">
        {/* ── Controls ── */}
        <div className="sim-top">
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
            <div className="field" style={{ margin: 0 }}>
              <label style={{ fontSize: 10, color: 'var(--faint)' }}>Spins</label>
              <input
                className="inp"
                type="number"
                value={spinsTarget}
                onChange={(e) => setSpinsTarget(parseInt(e.target.value) || 100_000)}
                disabled={status === 'running'}
                style={{ width: 100, fontFamily: 'var(--mono)' }}
              />
            </div>
            {status === 'idle' && (
              <button className="btn primary" onClick={start}>
                <span style={{ color: '#06140d' }}>▶</span> Start Run
              </button>
            )}
            {status === 'running' && (
              <button className="btn" onClick={stop} style={{ borderColor: 'var(--danger)', color: 'var(--danger)' }}>
                ■ Cancel
              </button>
            )}
            {(status === 'paused' || status === 'complete') && (
              <button className="btn primary" onClick={start}>
                ▶ Restart
              </button>
            )}
          </div>

          {/* Stat cards */}
          <div className="stat-cards" style={{ marginLeft: 'auto' }}>
            <div className="stat-card">
              <div className="sl">Samples</div>
              <div className="sv">{sampleCount.toLocaleString()}</div>
            </div>
            <div className="stat-card">
              <div className="sl">Running RTP</div>
              <div className="sv" style={{ color: 'var(--exact)' }}>
                {sampleCount > 0 ? `${(runningRtp * 100).toFixed(3)}%` : '—'}
              </div>
            </div>
            <div className="stat-card">
              <div className="sl">±95% CI</div>
              <div className="sv" style={{ color: 'var(--sampled)' }}>
                {sampleCount > 0 ? `±${(1.96 * stdErr * 100).toFixed(3)}%` : '—'}
              </div>
            </div>
            <div className="stat-card">
              <div className="sl">Status</div>
              <div className="sv" style={{
                fontSize: 13,
                color: status === 'complete' ? 'var(--exact)'
                  : status === 'running' ? 'var(--sampled)'
                  : 'var(--faint)',
              }}>
                {status}
              </div>
            </div>
          </div>
        </div>

        {/* Progress bar */}
        {status === 'running' && (
          <div style={{ padding: '0 16px', marginBottom: 8 }}>
            <div className="progress">
              <div className="bar" style={{ width: `${(sampleCount / spinsTarget) * 100}%` }} />
            </div>
            <div className="hint" style={{ textAlign: 'right' }}>
              {((sampleCount / spinsTarget) * 100).toFixed(0)}%
            </div>
          </div>
        )}

        {error && (
          <div style={{ padding: '8px 16px', color: 'var(--danger)', fontSize: 12, fontFamily: 'var(--mono)' }}>
            {error}
          </div>
        )}

        {needsFullRun && !error && (
          <div style={{ padding: '4px 16px', color: 'var(--epsilon)', fontSize: 11, fontFamily: 'var(--mono)' }}>
            Graph is too complex for exact evaluation — exact reference line unavailable; sampled only.
          </div>
        )}

        {/* ── Convergence chart ── */}
        <div className="chart-card" style={{ flex: 1, margin: '0 16px 16px' }}>
          <div className="chart-head">
            <span className="ct">RTP Convergence</span>
            <span className="leg">
              {exactRtp != null && (
                <span>
                  <span className="ln" style={{ background: 'var(--exact)', display: 'inline-block', width: 14, height: 2, borderRadius: 2, verticalAlign: 'middle', marginRight: 4 }} />
                  Exact ({(exactRtp * 100).toFixed(2)}%)
                </span>
              )}
              <span>
                <span className="ln" style={{ background: 'var(--sampled)', display: 'inline-block', width: 14, height: 2, borderRadius: 2, verticalAlign: 'middle', marginRight: 4 }} />
                Running
              </span>
              <span>
                <span className="ln" style={{ background: 'var(--sampled-dim)', display: 'inline-block', width: 14, height: 8, borderRadius: 3, verticalAlign: 'middle', marginRight: 4 }} />
                95% CI
              </span>
            </span>
          </div>
          <div className="chart-canvas-wrap">
            <ConvergenceChart
              points={points}
              exactRtp={exactRtp}
              width={900}
              height={280}
            />
          </div>
        </div>

        {/* ── Bottom row: histogram + comparison ── */}
        <div className="sim-bottom">
          <div className="hist-card">
            <div className="section-label" style={{ marginBottom: 8 }}>Hit Distribution</div>
            <Histogram data={histogram} width={420} height={180} />
          </div>
          <div className="hist-card">
            <div className="section-label" style={{ marginBottom: 8 }}>Exact vs Sampled</div>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '8px 0' }}>
                <span style={{ color: 'var(--muted)', fontSize: 12 }}>Exact RTP</span>
                <span style={{ fontFamily: 'var(--mono)', fontSize: 16, color: 'var(--exact)', fontWeight: 500 }}>
                  {exactRtp != null ? `${(exactRtp * 100).toFixed(2)}%` : needsFullRun ? 'needs full run' : '—'}
                </span>
                {exactRtp != null && (
                  <ProvBadge p={{ kind: exactProvenance?.toLowerCase().includes('sampled') ? 'Sampled' : 'Exact' }} mini />
                )}
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '8px 0' }}>
                <span style={{ color: 'var(--muted)', fontSize: 12 }}>Sampled RTP</span>
                <span style={{ fontFamily: 'var(--mono)', fontSize: 16, color: 'var(--sampled)', fontWeight: 500 }}>
                  {sampleCount > 0 ? `${(runningRtp * 100).toFixed(3)}%` : '—'}
                </span>
                {sampleCount > 0 && (
                  <ProvBadge p={{ kind: 'Sampled', n: sampleCount, stdErr }} mini />
                )}
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '8px 0' }}>
                <span style={{ color: 'var(--muted)', fontSize: 12 }}>95% CI band</span>
                <span style={{ fontFamily: 'var(--mono)', fontSize: 13, color: 'var(--sampled)' }}>
                  {sampleCount > 0
                    ? `[${((runningRtp - 1.96 * stdErr) * 100).toFixed(3)}% – ${((runningRtp + 1.96 * stdErr) * 100).toFixed(3)}%]`
                    : '—'}
                </span>
                <span style={{
                  fontSize: 10,
                  padding: '2px 8px',
                  borderRadius: 10,
                  background: sampleCount === 0 ? 'transparent'
                    : exactRtp == null ? 'var(--sampled-dim)'
                    : inCI ? 'var(--exact-dim)' : 'var(--danger-dim)',
                  color: sampleCount === 0 ? 'var(--faint)'
                    : exactRtp == null ? 'var(--sampled)'
                    : inCI ? 'var(--exact)' : 'var(--danger)',
                  fontFamily: 'var(--mono)',
                }}>
                  {sampleCount === 0 ? '—'
                    : exactRtp == null ? 'no ref'
                    : inCI ? '✓ in band' : '✗ outside'}
                </span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
