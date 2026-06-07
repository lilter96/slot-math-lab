import ProvBadge from './ProvBadge';
import type { Provenance } from './ProvBadge';

interface MetricCell {
  label: string;
  value: string;
  unit?: string;
  sub?: string;
  prov?: Provenance;
  accent?: string;
}

interface MetricStripProps {
  metrics?: MetricCell[] | null;
  rationalStr?: string;
}

/** Default empty state showing placeholder dashes — matches prototype layout. */
const EMPTY_METRICS: MetricCell[] = [
  {
    label: 'Return to player',
    value: '—',
    unit: '%',
    sub: '—',
    prov: { kind: 'Exact' },
    accent: 'var(--exact)',
  },
  {
    label: 'Hit frequency',
    value: '—',
    unit: '%',
    sub: '1 win / — spins',
    prov: { kind: 'Exact' },
  },
  {
    label: 'Base volatility',
    value: '—',
    unit: 'σ',
    sub: '— · base game',
    prov: { kind: 'Exact' },
  },
  {
    label: 'Feature trigger',
    value: '—',
    unit: '%',
    sub: '~1 in — spins',
    prov: { kind: 'Exact' },
  },
  {
    label: 'Max line win',
    value: '—',
    unit: '×',
    sub: '×— in free spins',
    prov: { kind: 'Exact' },
  },
];

export default function MetricStrip({ metrics }: MetricStripProps) {
  const cells = metrics ?? EMPTY_METRICS;

  return (
    <div className="metric-strip">
      {cells.map((cell, i) => (
        <div key={i} className="metric-cell">
          <div className="metric-label">
            <span>{cell.label}</span>
            {cell.prov && <ProvBadge p={cell.prov} mini />}
          </div>
          <div className="metric-value" style={cell.accent ? { color: cell.accent } : undefined}>
            {cell.value}
            {cell.unit && <span className="unit">{cell.unit}</span>}
          </div>
          {cell.sub && <div className="metric-sub">{cell.sub}</div>}
        </div>
      ))}
    </div>
  );
}
