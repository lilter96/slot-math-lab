import { useState } from 'react';
import type { LiveProgress } from '../../hooks/useSimulation';
import { number, percent, quantileBounds, tailBounds } from '../../lib/results/model';
export function Distribution({ progress: p, complete }: { progress?: LiveProgress; complete: boolean }) {
  const [log, setLog] = useState(true), [view, setView] = useState<'histogram' | 'cdf'>('histogram');
  if (!p?.sampleCount) return <div className="results-empty"><h3>No payout observations</h3><p>A failed or queued run has no distribution to analyze yet.</p></div>;
  const bins = p.histogram.filter(bin => bin.count > 0), total = p.sampleCount;
  const transform = (v: number) => log ? Math.log10(v + 1) : v;
  const max = Math.max(1, ...bins.map(bin => transform(bin.count)));
  const width = 760, height = 270, left = 48, bottom = 35, top = 18, right = 18;
  let cumulative = 0;
  const distribution = [];
  for (const bin of bins) { cumulative += bin.count; distribution.push({ ...bin, cumulative }); }
  const x = (index: number) => left + index * (width - left - right) / Math.max(1, bins.length);
  const y = (value: number) => height - bottom - value * (height - bottom - top);
  const bounds = (lo: number, hi: number | null) => `${number(lo)}–${hi == null ? '∞' : number(hi)}×`;
  const tail = tailBounds(p, 100);
  return <>
    <div className="results-card-head"><div><h3>Observed payout distribution</h3><p>Completed paid rounds, including their complete bonus. Original bin ranges and counts are retained.</p></div><div className="results-chart-controls"><button className="btn" aria-pressed={view === 'histogram'} onClick={() => setView('histogram')}>Histogram</button><button className="btn" aria-pressed={view === 'cdf'} onClick={() => setView('cdf')}>Cumulative</button>{view === 'histogram' && <button className="btn" aria-pressed={log} onClick={() => setLog(!log)}>{log ? 'Log counts' : 'Linear counts'}</button>}</div></div>
    <figure className="results-distribution"><svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label={`${view === 'cdf' ? 'Cumulative payout probability' : 'Payout histogram'}, ${total.toLocaleString()} rounds, ${bins.length} nonempty bins`}>
      {[0, .25, .5, .75, 1].map(value => <g key={value}><line x1={left} x2={width - right} y1={y(value)} y2={y(value)} className="results-plot-grid" /><text x={left - 8} y={y(value) + 4} textAnchor="end" className="results-plot-label">{view === 'cdf' ? percent(value, 0) : log ? number(Math.round(10 ** (value * max) - 1)) : number(Math.round(value * max))}</text></g>)}
      {view === 'histogram' ? distribution.map((bin, i) => <rect key={i} x={x(i) + 2} y={y(transform(bin.count) / max)} width={Math.max(1, (width - left - right) / bins.length - 4)} height={height - bottom - y(transform(bin.count) / max)} rx="3" className="results-hist-bar"><title>{bounds(bin.lo, bin.hi)}: {bin.count.toLocaleString()} rounds, {percent(bin.count / total)}</title></rect>) : <path d={distribution.map((bin, i) => `${i ? 'H' : 'M'} ${x(i)}${i ? '' : ` ${y(0)}`} V ${y(bin.cumulative / total)} H ${x(i + 1)}`).join(' ')} className="results-cdf-path" />}
      {distribution.map((bin, i) => i === 0 || i === bins.length - 1 || i % Math.max(1, Math.ceil(bins.length / 6)) === 0 ? <text key={bin.lo} x={x(i)} y={height - 12} className="results-plot-label">{number(bin.lo)}×</text> : null)}
    </svg><figcaption>Bars represent count per original bin; the horizontal spacing is categorical. Cumulative probability is exact at bin boundaries.</figcaption></figure>
    <div className="results-risk-grid"><article><span>No payout</span><strong>{percent(1 - p.hitFrequency)}</strong><small>{(total - p.nonZeroCount).toLocaleString()} rounds</small></article>{[.5, .9, .99].map(q => {
      const v = quantileBounds(p, q); return <article key={q}><span>P{q * 100} payout</span><strong>{v ? v.lo === v.hi ? `${number(v.lo)}×` : bounds(v.lo, v.hi) : '—'}</strong><small>{v?.lo === v?.hi ? 'Exact observed value' : 'Bounded by histogram resolution'}</small></article>;
    })}</div>
    <div className="results-tail-note"><strong>100× and above</strong><span>{tail.lower === tail.upper ? number(tail.lower) : `${number(tail.lower)}–${number(tail.upper)}`} observed rounds · {tail.lower === tail.upper ? percent(tail.lower / total) : `${percent(tail.lower / total)}–${percent(tail.upper / total)}`}</span>{complete && tail.zeroEventUpper95 != null && <p>No such payout was observed. Under independent fixed-count sampling, the one-sided 95% binomial upper bound is {percent(tail.zeroEventUpper95, 5)}. An unobserved payout is still possible.</p>}</div>
    <details className="results-bin-details"><summary>Inspect {bins.length} nonempty bins and cumulative counts</summary><div className="results-table-scroll"><table className="results-table"><thead><tr><th>Payout range</th><th>Rounds</th><th>Probability</th><th>Cumulative</th></tr></thead><tbody>{distribution.map(bin => <tr key={bin.lo}><td>[{number(bin.lo)}, {bin.hi == null ? '∞' : number(bin.hi)})×</td><td>{number(bin.count)}</td><td>{percent(bin.count / total)}</td><td>{percent(bin.cumulative / total)}</td></tr>)}</tbody></table></div></details>
  </>;
}
