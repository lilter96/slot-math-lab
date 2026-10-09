import { percent } from '../../lib/results/model';
export function IntervalPlot({ mean, ci, target, tolerance, reference }: { mean: number; ci: readonly [number, number] | null; target: number | null; tolerance: number; reference?: number }) {
  const values = [mean, ...(ci ?? []), ...(target == null ? [] : [target - tolerance / 100, target + tolerance / 100]), ...(reference == null ? [] : [reference])];
  const low = Math.min(...values), high = Math.max(...values), padding = Math.max(.005, (high - low) * .15);
  const min = low - padding, max = high + padding;
  const x = (n: number) => 55 + (n - min) / (max - min) * 620;
  return <figure className="results-interval-plot"><svg role="img" aria-label={`Observed RTP ${percent(mean)}${ci ? `, approximate 95% interval ${percent(ci[0])} to ${percent(ci[1])}` : ', interval unavailable'}`} viewBox="0 0 730 138">
    {target != null && <rect x={x(target - tolerance / 100)} y="19" width={Math.max(0, x(target + tolerance / 100) - x(target - tolerance / 100))} height="75" className="results-tolerance-band" />}
    <line x1="55" x2="675" y1="95" y2="95" className="results-plot-grid" />
    {[0, 1, 2, 3, 4].map(i => <text key={i} x={55 + i * 155} y="121" textAnchor="middle" className="results-plot-label">{percent(min + (max - min) * i / 4, 2)}</text>)}
    {target != null && <line x1={x(target)} x2={x(target)} y1="19" y2="95" className="results-target-line" />}
    {reference != null && <path d={`M ${x(reference)} 15 l -5 -7 l 10 0 Z`} className="results-reference-marker" />}
    {ci && <><line x1={x(ci[0])} x2={x(ci[1])} y1="57" y2="57" className="results-ci-line" /><path d={`M ${x(ci[0])} 47 V 67 M ${x(ci[1])} 47 V 67`} className="results-ci-cap" /></>}
    <circle cx={x(mean)} cy="57" r="6" className="results-mean-dot" />
  </svg><figcaption><span>● Observed RTP{ci ? ' + 95% interval' : ''}</span>{target != null && <span>▧ Target ± tolerance</span>}{reference != null && <span>▼ Exact expectation</span>}</figcaption></figure>;
}
