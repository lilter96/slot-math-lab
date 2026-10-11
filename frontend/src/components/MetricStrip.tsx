import ProvBadge from './ProvBadge';
import { metricProvenance } from '../lib/metricEvidence';
import type { LiveMetricsState } from '../hooks/useLiveMetrics';

export default function MetricStrip({ liveMetrics }: { liveMetrics?: LiveMetricsState | null }) {
  const metric = !liveMetrics?.loading && !liveMetrics?.error ? liveMetrics?.overall : null;
  const proof = metric ? metricProvenance(metric) : null;
  const status = liveMetrics?.loading ? 'Calculating' : liveMetrics?.error ? 'Unavailable'
    : metric?.provenance === 'NeedsFullRun' ? 'Needs full run'
      : metric?.rtp != null && proof ? proof.kind === 'Exact' ? 'Exact' : proof.kind === 'Sampled' ? 'Sampled' : 'Incomplete distribution' : 'Not evaluated';
  const values = [
    { label: 'Return to player', value: metric?.rtp, unit: '%', format: (x: number) => (x * 100).toFixed(2), detail: metric?.provenance === 'ExactInterval' && metric.lo != null && metric.hi != null ? `RTP ratio bounds: ${metric.lo} to ${metric.hi}` : metric?.ci95 ? `95% CI (RTP ratio): ${metric.ci95}` : proof?.kind === 'Exact' ? 'Exact rational calculation' : 'Run analysis for full evidence' },
    { label: 'Hit frequency', value: metric?.hitFrequency, unit: '%', format: (x: number) => (x * 100).toFixed(2), detail: 'Paid rounds with a win' },
    { label: 'Volatility index', value: metric?.volatility, unit: '', format: (x: number) => x.toFixed(2), detail: 'Reported by light evaluation' },
  ];
  return <div className="metric-strip" aria-label="Model evaluation">
    {values.map(({ label, value, unit, format, detail }) => {
      const available = value != null && Number.isFinite(value) && !!proof && (proof.kind !== 'ExactInterval' || label === 'Return to player');
      return <div className="metric-cell" key={label}>
        <div className="metric-label"><span>{label}</span>{available && <ProvBadge p={proof} mini />}</div>
        <div className="metric-value" style={{ color: proof?.kind === 'Sampled' ? 'var(--sampled)' : 'var(--text)' }}>{available ? format(value) : '—'}{available && <span className="unit">{unit}</span>}</div>
        <div className="metric-sub">{available ? detail : status}</div>
      </div>;
    })}
    <div className="metric-cell"><div className="metric-label">Maximum win</div><div className="metric-value">—</div><div className="metric-sub">No proven bound in light evaluation</div></div>
    <div className="metric-cell"><div className="metric-label">Evaluation status</div><div className="metric-value metric-status" role="status">{status}</div><div className="metric-sub">{liveMetrics?.error ?? (liveMetrics?.loading ? liveMetrics.statusMessage : null) ?? (status === 'Needs full run' ? 'Open Simulation to run this model' : metric?.elapsedMs != null ? `${metric.elapsedMs.toFixed(0)} ms` : 'Evidence follows a completed calculation')}</div></div>
  </div>;
}
