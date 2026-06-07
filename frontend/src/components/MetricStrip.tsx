import { useEffect, useRef, useState } from 'react';
import ProvBadge from './ProvBadge';
import type { Provenance } from './ProvBadge';
import type { LiveMetricsState } from '../hooks/useLiveMetrics';

interface MetricStripProps {
  liveMetrics?: LiveMetricsState | null;
}

function provFromLive(p: string): Provenance {
  switch (p) {
    case 'Exact': return { kind: 'Exact' };
    case 'Sampled': return { kind: 'Sampled', n: 5000 };
    case 'NeedsFullRun': return { kind: 'Sampled', n: 0, note: 'needs full run' };
    default: return { kind: 'Exact' };
  }
}

function pctStr(x: number): string {
  return (x * 100).toFixed(2);
}

export default function MetricStrip({ liveMetrics }: MetricStripProps) {
  const prevRtp = useRef<number | null>(null);
  const [flash, setFlash] = useState(false);
  const m = liveMetrics?.overall;
  const loading = liveMetrics?.loading;
  const error = liveMetrics?.error;
  const needsFullRun = m?.provenance === 'NeedsFullRun';

  // Flash on RTP change
  useEffect(() => {
    if (!m) return;
    if (prevRtp.current != null && Math.abs(prevRtp.current - m.rtp) > 1e-9) {
      setFlash(true);
      const t = setTimeout(() => setFlash(false), 480);
      return () => clearTimeout(t);
    }
    prevRtp.current = m.rtp;
  }, [m?.rtp]); // eslint-disable-line react-hooks/exhaustive-deps

  const rtpProv = m ? provFromLive(m.provenance) : { kind: 'Exact' as const };
  const hfProv = m ? provFromLive(m.provenance) : { kind: 'Exact' as const };
  const volProv = m ? provFromLive(m.provenance) : { kind: 'Exact' as const };

  // Loading skeleton
  if (loading && !m) {
    return (
      <div className="metric-strip">
        {['Return to player', 'Hit frequency', 'Base volatility', 'Feature trigger', 'Max line win'].map((label, i) => (
          <div key={i} className="metric-cell">
            <div className="metric-label"><span>{label}</span></div>
            <div className="metric-value" style={{ opacity: 0.4 }}>…</div>
          </div>
        ))}
      </div>
    );
  }

  // NeedsFullRun state
  if (needsFullRun) {
    return (
      <div className="metric-strip">
        {['Return to player', 'Hit frequency', 'Base volatility', 'Feature trigger', 'Max line win'].map((label, i) => (
          <div key={i} className="metric-cell">
            <div className="metric-label"><span>{label}</span></div>
            <div className="metric-value" style={{ color: 'var(--epsilon)', fontSize: 16 }}>Needs full run</div>
            <div className="metric-sub">graph too complex for light eval</div>
          </div>
        ))}
      </div>
    );
  }

  // Error state — show error in first cell, dashes in rest
  if (error && !m) {
    return (
      <div className="metric-strip">
        <div className="metric-cell">
          <div className="metric-label"><span>Return to player</span></div>
          <div className="metric-value" style={{ color: 'var(--danger)', fontSize: 13 }}>{error}</div>
          <div className="metric-sub">Check backend logs</div>
        </div>
        {['Hit frequency', 'Base volatility', 'Feature trigger', 'Status'].map((label, i) => (
          <div key={i} className="metric-cell">
            <div className="metric-label"><span>{label}</span></div>
            <div className="metric-value">—</div>
          </div>
        ))}
      </div>
    );
  }

  // Live metrics
  return (
    <div className="metric-strip">
      <div className={'metric-cell' + (flash ? ' flash' : '')}>
        <div className="metric-label">
          <span>Return to player</span>
          <ProvBadge p={rtpProv} mini />
        </div>
        <div className="metric-value" style={{ color: 'var(--exact)' }}>
          {m ? pctStr(m.rtp) : '—'}<span className="unit">%</span>
        </div>
        <div className="metric-sub">
          {m?.provenance === 'Sampled' && m.ci95
            ? `95% CI: ${m.ci95}`
            : m?.provenance === 'Exact'
              ? 'exact rational'
              : '—'}
        </div>
      </div>

      <div className="metric-cell">
        <div className="metric-label">
          <span>Hit frequency</span>
          <ProvBadge p={hfProv} mini />
        </div>
        <div className="metric-value">
          {m ? pctStr(m.hitFrequency) : '—'}<span className="unit">%</span>
        </div>
        <div className="metric-sub">
          {m && m.hitFrequency > 0
            ? `1 win / ${(1 / m.hitFrequency).toFixed(1)} spins`
            : '—'}
        </div>
      </div>

      <div className="metric-cell">
        <div className="metric-label">
          <span>Base volatility</span>
          <ProvBadge p={volProv} mini />
        </div>
        <div className="metric-value">
          {m ? m.volatility.toFixed(2) : '—'}<span className="unit">σ</span>
        </div>
        <div className="metric-sub">
          {m
            ? m.volatility < 15 ? 'low' : m.volatility < 35 ? 'medium' : 'high'
            : '—'} · base game
        </div>
      </div>

      <div className="metric-cell">
        <div className="metric-label">
          <span>Feature trigger</span>
          <ProvBadge p={{ kind: 'Exact' }} mini />
        </div>
        <div className="metric-value">—<span className="unit">%</span></div>
        <div className="metric-sub">~1 in — spins</div>
      </div>

      <div className="metric-cell">
        <div className="metric-label">
          <span>Status</span>
          <ProvBadge p={m ? provFromLive(m.provenance) : { kind: 'Exact' }} mini />
        </div>
        <div className="metric-value" style={{ fontSize: 14, color: m?.provenance === 'Sampled' ? 'var(--sampled)' : m?.provenance === 'Exact' ? 'var(--exact)' : 'var(--epsilon)' }}>
          {m ? m.provenance : '—'}
        </div>
        <div className="metric-sub">
          {loading ? 'evaluating…' : m?.elapsedMs ? `${m.elapsedMs.toFixed(0)}ms` : '—'}
        </div>
      </div>
    </div>
  );
}
