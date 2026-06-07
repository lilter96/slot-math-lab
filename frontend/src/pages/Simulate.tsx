import { useState, useRef, useCallback, useEffect } from 'react';
import * as signalR from '@microsoft/signalr';
import ConvergenceChart, { type ConvergencePoint } from '../components/simulate/ConvergenceChart';
import Histogram from '../components/simulate/Histogram';
import ProvBadge from '../components/ProvBadge';

const TOTAL_SPINS = 1_000_000;
const EXACT_REFERENCE = 0.9534;
const CAP_WIN = 5000;

type RunStatus = 'idle' | 'running' | 'paused' | 'complete';

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
  const [spinsTarget] = useState(TOTAL_SPINS);

  const hubRef = useRef<signalR.HubConnection | null>(null);
  const abortRef = useRef(false);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const stop = useCallback(() => {
    abortRef.current = true;
    if (timerRef.current) { clearInterval(timerRef.current); timerRef.current = null; }
    if (hubRef.current) { hubRef.current.stop(); hubRef.current = null; }
    if (status === 'running') setStatus('paused');
  }, [status]);

  const start = useCallback(async () => {
    abortRef.current = false;
    setStatus('running');
    setSampleCount(0); setRunningRtp(0); setStdErr(0); setVolatility(0);
    setMaxWin(0); setCapHits(0);
    setPoints([]); setHistogram(new Map());

    // Try SignalR
    try {
      const hub = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/runs')
        .withAutomaticReconnect()
        .build();
      hub.on('RunProgress', (msg: { sampleCount: number; totalSamples: number; runningRtp: number; stdErr: number; status: string }) => {
        if (abortRef.current) return;
        setSampleCount(msg.sampleCount); setRunningRtp(msg.runningRtp); setStdErr(msg.stdErr);
        setPoints((prev) => [...prev, { n: msg.sampleCount, rtp: msg.runningRtp, stdErr: msg.stdErr }]);
        if (msg.status === 'completed' || msg.sampleCount >= msg.totalSamples) { setStatus('complete'); hub.stop(); hubRef.current = null; }
      });
      hubRef.current = hub;
      await hub.start();
      return;
    } catch { /* fall through to local sim */ }

    // Local fallback simulation
    let n = 0, sum = 0, sumSq = 0, localMax = 0, localCapHits = 0;
    const mu = EXACT_REFERENCE;
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
      if (n >= spinsTarget) { if (timerRef.current) clearInterval(timerRef.current); setStatus('complete'); }
    }, 50);
  }, [spinsTarget]);

  useEffect(() => () => {
    abortRef.current = true;
    if (timerRef.current) clearInterval(timerRef.current);
    if (hubRef.current) hubRef.current.stop();
  }, []);

  const inBand = sampleCount > 1000 && Math.abs(runningRtp - EXACT_REFERENCE) <= 3 * stdErr + 1e-9;
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
              <div className="sv" style={{ color: 'var(--sampled)' }}>{(runningRtp * 100).toFixed(3)}%</div>
            </div>
            <div className="stat-card">
              <div className="sl">Std error</div>
              <div className="sv">±{(stdErr * 100).toFixed(4)}</div>
            </div>
            <div className="stat-card">
              <div className="sl">Exact RTP</div>
              <div className="sv" style={{ color: 'var(--exact)' }}>{(EXACT_REFERENCE * 100).toFixed(2)}%</div>
            </div>
            <div className="stat-card">
              <div className="sl">Max win seen</div>
              <div className="sv">{maxWin > 0 ? maxWin + '×' : '—'}</div>
            </div>
          </div>
        </div>

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
              <span><span className="ln" style={{ background: 'var(--exact)' }} />exact (closed form)</span>
              <span><span className="ln" style={{ background: 'var(--sampled)' }} />sampled mean</span>
              <span><span className="ln" style={{ background: 'var(--sampled)', opacity: 0.35, height: 8 }} />95% CI</span>
            </div>
          </div>
          <div className="chart-canvas-wrap">
            <ConvergenceChart points={points} exactRtp={EXACT_REFERENCE} />
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
