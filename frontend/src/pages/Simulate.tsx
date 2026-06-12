import { useState, useRef, useCallback, useEffect } from 'react';
import * as signalR from '@microsoft/signalr';
import { useAppStore } from '../store';
import { buildConfigPayload } from '../lib/configPayload';
import ConvergenceChart, { type ConvergencePoint } from '../components/simulate/ConvergenceChart';
import Histogram from '../components/simulate/Histogram';
import ProvBadge from '../components/ProvBadge';
import type { components } from '../api/generated-types';

type RunProgressMessage = components['schemas']['RunProgressMessage'];
type EvaluateLightResponse = components['schemas']['EvaluateLightResponse'];

// ── Run state ──────────────────────────────────────────────────────
type RunStatus = 'idle' | 'running' | 'paused' | 'complete';

export default function Simulate() {
  const nodes = useAppStore((s) => s.nodes);
  const edges = useAppStore((s) => s.edges);
  const configName = useAppStore((s) => s.configName);

  const [status, setStatus] = useState<RunStatus>('idle');
  const [sampleCount, setSampleCount] = useState(0);
  const [runningRtp, setRunningRtp] = useState(0);
  const [stdErr, setStdErr] = useState(0);
  const [points, setPoints] = useState<ConvergencePoint[]>([]);
  const [histogram, setHistogram] = useState<Map<number, number>>(new Map());
  const [winHistogram, setWinHistogram] = useState<Map<number, number> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [spinsTarget, setSpinsTarget] = useState(100_000);

  // Exact RTP from /api/evaluate/light — null until fetched
  const [exactRtp, setExactRtp] = useState<number | null>(null);
  const [exactProvenance, setExactProvenance] = useState<string | null>(null);
  const [needsFullRun, setNeedsFullRun] = useState(false);

  const hubRef = useRef<signalR.HubConnection | null>(null);
  const abortRef = useRef(false);
  const runIdRef = useRef<string | null>(null);

  // ── Stop / cleanup ──────────────────────────────────────────────
  const stop = useCallback(() => {
    abortRef.current = true;
    // Cancel the run server-side too — closing the socket alone would
    // leave the job burning CPU on the backend.
    if (runIdRef.current) {
      void fetch(`/api/runs/${runIdRef.current}`, { method: 'DELETE' }).catch(() => {});
      runIdRef.current = null;
    }
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
    setWinHistogram(null);
    setExactRtp(null);
    setExactProvenance(null);
    setNeedsFullRun(false);

    const configPayload = buildConfigPayload(nodes, edges, { name: configName ?? 'Untitled' });
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
      runIdRef.current = runId;

      // ── Step 4: SignalR streaming ───────────────────────────────
      const hub = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/runs')
        .withAutomaticReconnect()
        .build();

      const finishRun = (runStatus: string) => {
        setStatus(runStatus === 'completed' ? 'complete' : 'paused');
        if (runStatus === 'failed') setError('Run failed on the server.');
        runIdRef.current = null;
        hub.stop();
        hubRef.current = null;

        // The persisted result carries the real per-spin win histogram.
        if (runStatus === 'completed') {
          void fetch(`/api/runs/${runId}`)
            .then((r) => (r.ok ? r.json() : null))
            .then((run: { resultJson?: string } | null) => {
              if (!run?.resultJson) return;
              const result = JSON.parse(run.resultJson) as {
                histogram?: { lo: number; hi: number; count: number }[];
              };
              if (!result.histogram?.length) return;
              const bins = new Map<number, number>();
              for (const b of result.histogram) {
                if (b.count > 0) bins.set((b.lo + b.hi) / 2, b.count);
              }
              if (bins.size > 0) setWinHistogram(bins);
            })
            .catch(() => {});
        }
      };

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
        if (terminal) finishRun(msg.status ?? 'completed');
      });

      // Groups are connection-scoped: a reconnected connection has a new
      // id and must re-join the run's group or it goes silent.
      hub.onreconnected(() => {
        if (runIdRef.current) void hub.invoke('SubscribeToRun', runIdRef.current);
      });

      hubRef.current = hub;
      await hub.start();
      await hub.invoke('SubscribeToRun', runId);

      // Fast runs can finish before the subscription lands and their
      // terminal broadcast is gone — poll once to catch up.
      try {
        const statusRes = await fetch(`/api/runs/${runId}`);
        if (statusRes.ok) {
          const run = (await statusRes.json()) as {
            status?: string; sampleCount?: number; runningRtp?: number; stdErr?: number;
          };
          const s = run.status ?? '';
          if (s === 'completed' || s === 'cancelled' || s === 'failed') {
            if (run.sampleCount != null) setSampleCount(run.sampleCount);
            if (run.runningRtp != null) setRunningRtp(run.runningRtp);
            if (run.stdErr != null) setStdErr(run.stdErr);
            finishRun(s);
          }
        }
      } catch {
        // status catch-up is best-effort; progress events remain authoritative
      }

    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      setError(`Backend error: ${msg}. Is the backend running on this host?`);
      setStatus('idle');
      if (hubRef.current) { void hubRef.current.stop(); hubRef.current = null; }
    }
  }, [spinsTarget, nodes, edges, configName]);

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
            <div className="section-label" style={{ marginBottom: 8 }}>
              {winHistogram ? 'Win Distribution (per spin)' : 'Running RTP Distribution'}
            </div>
            <Histogram data={winHistogram ?? histogram} width={420} height={180} />
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
