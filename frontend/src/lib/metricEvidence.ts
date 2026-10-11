import type { Provenance } from '../components/ProvBadge';
import type { LiveMetric } from '../hooks/useLiveMetrics';

export function metricProvenance(metric: LiveMetric): Provenance | null {
  if (metric.provenance === 'Sampled') return metric.sampleCount != null && metric.sampleCount > 0
    ? { kind: 'Sampled', n: metric.sampleCount, stdErr: metric.stdErr } : null;
  if (['Exact', 'ExactInterval', 'ExactWithMassLoss'].includes(metric.provenance)) {
    return { kind: metric.provenance as Provenance['kind'] };
  }
  return null;
}

