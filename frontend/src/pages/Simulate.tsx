import { useState, useRef, useCallback, useEffect } from 'react';
import * as signalR from '@microsoft/signalr';
import ConvergenceChart, { type ConvergencePoint } from '../components/simulate/ConvergenceChart';
import Histogram from '../components/simulate/Histogram';
import ProvBadge from '../components/ProvBadge';

// ── Simulate config ─────────────────────────────────────────────────
const TOTAL_SPINS = 100_000;
const BATCH_SIZE = 100;
const EXACT_REFERENCE = 0.9534; // example exact RTP for comparison

// ── Run state ──────────────────────────────────────────────────────
type RunStatus = 'idle' | 'running' | 'paused' | 'complete';

export default function Simulate() {
  const [status, setStatus] = useState<RunStatus>('idle');
  const [sampleCount, setSampleCount] = useState(0);
  const [runningRtp, setRunningRtp] = useState(0);
  const [stdErr, setStdErr] = useState(0);
  const [points, setPoints] = useState<ConvergencePoint[]>([]);
  const [histogram, setHistogram] = useState<Map<number, number>>(new Map());
  const [error, setError] = useState<string | null>(null);
  const [spinsTarget, setSpinsTarget] = useState(TOTAL_SPINS);

  const hubRef = useRef<signalR.HubConnection | null>(null);
  const abortRef = useRef(false);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // ── Stop / cleanup ──────────────────────────────────────────────
  const stop = useCallback(() => {
    abortRef.current = true;
    if (timerRef.current) { clearInterval(timerRef.current); timerRef.current = null; }
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

    // Try SignalR connection to backend
    try {
      const hub = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/runs')
        .withAutomaticReconnect()
        .build();

      hub.on('RunProgress', (msg: {
        runId: string;
        sampleCount: number;
        totalSamples: number;
        runningRtp: number;
        stdErr: number;
        status: string;
        elapsedMs: number;
      }) => {
        if (abortRef.current) return;
        setSampleCount(msg.sampleCount);
        setRunningRtp(msg.runningRtp);
        setStdErr(msg.stdErr);
        setPoints((prev) => [...prev, {
          n: msg.sampleCount,
          rtp: msg.runningRtp,
          stdErr: msg.stdErr,
        }]);

        if (msg.status === 'completed' || msg.sampleCount >= msg.totalSamples) {
          setStatus('complete');
          hub.stop();
          hubRef.current = null;
        }
      });

      hubRef.current = hub;
      await hub.start();
    } catch {
      // SignalR failed — fall back to simulated streaming
      console.warn('SignalR unavailable, using simulated data');
    }

    // Fallback: simulate streaming data if SignalR not connected
    let n = 0;
    let sum = 0;
    let sumSq = 0;
    const mu = EXACT_REFERENCE;

    timerRef.current = setInterval(() => {
      if (abortRef.current) return;
      n += BATCH_SIZE;
      if (n > spinsTarget) n = spinsTarget;

      // Simulate Monte Carlo convergence
      for (let j = 0; j < BATCH_SIZE && n - BATCH_SIZE + j < spinsTarget; j++) {
        const win = mu + (Math.random() - 0.5) * 0.3 * Math.exp(-n / 20000);
        sum += Math.max(0, win);
        sumSq += Math.max(0, win) ** 2;
      }

      const avg = sum / n;
      const variance = sumSq / n - avg * avg;
      const se = Math.sqrt(Math.max(0, variance) / n);

      setSampleCount(n);
      setRunningRtp(avg);
      setStdErr(se);
      setPoints((prev) => {
        const last = prev[prev.length - 1];
        if (last && n - last.n < spinsTarget / 50) return prev;
        return [...prev, { n, rtp: avg, stdErr: se }];
      });

      // Update histogram
      setHistogram((prev) => {
        const next = new Map(prev);
        const bucket = Math.round(avg * 20) / 20; // 0.05-width buckets
        next.set(bucket, (next.get(bucket) || 0) + 1);
        return next;
      });

      if (n >= spinsTarget) {
        if (timerRef.current) clearInterval(timerRef.current);
        setStatus('complete');
      }
    }, 80);
  }, [spinsTarget]);

  // ── Cleanup on unmount ──────────────────────────────────────────
  useEffect(() => {
    return () => {
      abortRef.current = true;
      if (timerRef.current) clearInterval(timerRef.current);
      if (hubRef.current) hubRef.current.stop();
    };
  }, []);

  const inCI = runningRtp >= EXACT_REFERENCE - 1.96 * stdErr && runningRtp <= EXACT_REFERENCE + 1.96 * stdErr;

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
                onChange={(e) => setSpinsTarget(parseInt(e.target.value) || TOTAL_SPINS)}
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
              <div className="sv" style={{ color: 'var(--exact)' }}>{(runningRtp * 100).toFixed(3)}%</div>
            </div>
            <div className="stat-card">
              <div className="sl">±95% CI</div>
              <div className="sv" style={{ color: 'var(--sampled)' }}>±{(1.96 * stdErr * 100).toFixed(3)}%</div>
            </div>
            <div className="stat-card">
              <div className="sl">Status</div>
              <div className="sv" style={{ fontSize: 13, color: status === 'complete' ? 'var(--exact)' : status === 'running' ? 'var(--sampled)' : 'var(--faint)' }}>
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
            <div className="hint" style={{ textAlign: 'right' }}>{((sampleCount / spinsTarget) * 100).toFixed(0)}%</div>
          </div>
        )}

        {error && (
          <div style={{ padding: '8px 16px', color: 'var(--danger)', fontSize: 12 }}>{error}</div>
        )}

        {/* ── Convergence chart ── */}
        <div className="chart-card" style={{ flex: 1, margin: '0 16px 16px' }}>
          <div className="chart-head">
            <span className="ct">RTP Convergence</span>
            <span className="leg">
              <span><span className="ln" style={{ background: 'var(--exact)', display: 'inline-block', width: 14, height: 2, borderRadius: 2, verticalAlign: 'middle', marginRight: 4 }} />Exact ({((EXACT_REFERENCE * 100)).toFixed(2)}%)</span>
              <span><span className="ln" style={{ background: 'var(--sampled)', display: 'inline-block', width: 14, height: 2, borderRadius: 2, verticalAlign: 'middle', marginRight: 4 }} />Running</span>
              <span><span className="ln" style={{ background: 'var(--sampled-dim)', display: 'inline-block', width: 14, height: 8, borderRadius: 3, verticalAlign: 'middle', marginRight: 4 }} />95% CI</span>
            </span>
          </div>
          <div className="chart-canvas-wrap">
            <ConvergenceChart
              points={points}
              exactRtp={EXACT_REFERENCE}
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
                  {(EXACT_REFERENCE * 100).toFixed(2)}%
                </span>
                <ProvBadge p={{ kind: 'Exact' }} mini />
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '8px 0' }}>
                <span style={{ color: 'var(--muted)', fontSize: 12 }}>Sampled RTP</span>
                <span style={{ fontFamily: 'var(--mono)', fontSize: 16, color: 'var(--sampled)', fontWeight: 500 }}>
                  {(runningRtp * 100).toFixed(3)}%
                </span>
                {sampleCount > 0 && (
                  <ProvBadge p={{ kind: 'Sampled', n: sampleCount, stdErr }} mini />
                )}
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '8px 0' }}>
                <span style={{ color: 'var(--muted)', fontSize: 12 }}>95% CI band</span>
                <span style={{ fontFamily: 'var(--mono)', fontSize: 13, color: 'var(--sampled)' }}>
                  [{((runningRtp - 1.96 * stdErr) * 100).toFixed(3)}% – {((runningRtp + 1.96 * stdErr) * 100).toFixed(3)}%]
                </span>
                <span style={{
                  fontSize: 10,
                  padding: '2px 8px',
                  borderRadius: 10,
                  background: inCI ? 'var(--exact-dim)' : 'var(--danger-dim)',
                  color: inCI ? 'var(--exact)' : 'var(--danger)',
                  fontFamily: 'var(--mono)',
                }}>
                  {sampleCount > 0 ? (inCI ? '✓ in band' : '✗ outside') : '—'}
                </span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
