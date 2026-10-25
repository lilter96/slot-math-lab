import type { Provenance } from '../components/ProvBadge';
import type { LiveMetric } from '../hooks/useLiveMetrics';

export function metricProvenance(metric: LiveMetric): Provenance | null {
  if (metric.provenance === 'Sampled') return metric.sampleCount != null && Number.isSafeInteger(metric.sampleCount) && metric.sampleCount > 0
    ? { kind: 'Sampled', n: metric.sampleCount, stdErr: metric.sampleCount >= 2 ? metric.stdErr : undefined } : null;
  if (metric.provenance === 'ExactInterval') return metric.lo != null && metric.hi != null && Number.isFinite(metric.lo) && Number.isFinite(metric.hi) && metric.lo <= metric.hi
    ? { kind: 'ExactInterval', note: `RTP ratio interval: ${metric.lo} to ${metric.hi}` } : null;
  if (['Exact', 'ExactWithMassLoss'].includes(metric.provenance)) {
    return { kind: metric.provenance as Provenance['kind'] };
  }
  return null;
}

