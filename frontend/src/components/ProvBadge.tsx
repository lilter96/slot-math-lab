export interface Provenance {
  kind: 'Exact' | 'ExactWithinEpsilon' | 'ExactInterval' | 'ExactWithMassLoss' | 'Sampled';
  n?: number;
  stdErr?: number;
  ci95?: number;
  bound?: number;
  note?: string;
}

interface ProvBadgeProps {
  p?: Provenance | null;
  mini?: boolean;
}

function fmtN(n: number): string {
  if (n >= 1e6) return (n / 1e6).toFixed(n >= 1e7 ? 0 : 1) + 'M';
  if (n >= 1e3) return (n / 1e3).toFixed(n >= 1e4 ? 0 : 1) + 'k';
  return String(Math.round(n));
}

function provTitle(p: Provenance): string {
  if (p.kind === 'Sampled')
    return `Monte-Carlo estimate${p.n != null ? ` · n=${fmtN(p.n)}` : ' · sample count unavailable'}${p.stdErr != null ? ` · stdErr ${p.stdErr.toFixed(5)}` : ''}${p.ci95 != null ? ` · 95% CI ±${p.ci95.toFixed(5)}` : ''}`;
  if (p.kind === 'ExactWithinEpsilon')
    return `Exact up to ε-pruning · ${p.note || ''} · ${p.bound != null ? `bound ±${p.bound}` : 'bound unavailable'}`;
  if (p.kind === 'ExactWithMassLoss') return 'Pruned probability mass has no certified bound; non-regulatory';
  if (p.kind === 'ExactInterval') return p.note ?? 'Bounded exact RTP interval';
  return 'Exact rational — closed form, no sampling';
}

export default function ProvBadge({ p, mini }: ProvBadgeProps) {
  if (!p) return null;

  const map = {
    Exact: { cls: 'exact', label: 'Exact' },
    ExactWithinEpsilon: { cls: 'epsilon', label: mini ? 'ε-pruned' : 'Exact ± ε' },
    ExactInterval: { cls: 'epsilon', label: 'Exact interval' },
    ExactWithMassLoss: { cls: 'epsilon', label: 'Mass loss' },
    Sampled: { cls: 'sampled', label: 'Sampled' },
  };

  const m = map[p.kind];
  let detail = '';
  if (p.kind === 'Sampled' && p.n) detail = ' n=' + fmtN(p.n);

  return (
    <span className={`prov ${m.cls}`} title={provTitle(p)}>
      <span className="pdot" />
      {m.label}
      {detail}
    </span>
  );
}
