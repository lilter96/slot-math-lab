import { useState } from 'react';
import type { LivePoint } from '../../hooks/useSimulation';
import { usePlotWidth } from '../../hooks/usePlotWidth';

import { count } from './format';

export function LiveChart({ points, reference, target, kind = 'rtp', inference = true }: {
  points: LivePoint[]; reference?: number | null; target?: number | null; kind?: 'rtp' | 'rate' | 'precision'; inference?: boolean;
}) {
  const [hover, setHover] = useState<number | null>(null);
  const { ref, width } = usePlotWidth(800);
  const height = 290, left = 62, right = 28, top = 22, bottom = 38;
  const ticks = width < 450 ? [0, .5, 1] : [0, .25, .5, .75, 1];
  const value = (p: LivePoint) => kind === 'rate' ? p.rate : kind === 'precision' ? 1.96 * p.stdErr * 100 : p.rtp * 100;
  const values = points.flatMap(p => kind === 'rtp' && inference && p.n > 1 && p.stdErr > 0 ? [(p.rtp - 1.96 * p.stdErr) * 100, (p.rtp + 1.96 * p.stdErr) * 100] : [value(p)]);
  if (kind === 'rtp') { if (reference != null) values.push(reference * 100); if (target != null) values.push(target * 100); }
  const low = Math.min(...values, kind === 'rtp' ? 90 : 0), high = Math.max(...values, kind === 'rtp' ? 106 : 1);
  const margin = (high - low) * 0.12;
  const min = kind === 'rtp' ? low - margin : 0, max = high + margin;
  const maxN = Math.max(1, points.at(-1)?.n ?? 1);
  const x = (n: number) => left + n / maxN * (width - left - right);
  const y = (v: number) => top + (max - v) / (max - min) * (height - top - bottom);
  const fmt = (v: number) => kind === 'rate' ? count(v) : `${v.toFixed(kind === 'precision' ? 2 : 1)}${kind === 'precision' ? ' pp' : '%'}`;
  const index = hover === null ? points.length - 1 : Math.min(hover, points.length - 1);
  const selected = points[index];
  const line = points.map((p, i) => `${i ? 'L' : 'M'} ${x(p.n)} ${y(value(p))}`).join(' ');
  const ciPoints = points.filter(p => inference && p.n > 1 && p.stdErr > 0);
  const band = ciPoints.map((p, i) => `${i ? 'L' : 'M'} ${x(p.n)} ${y((p.rtp + 1.96 * p.stdErr) * 100)}`).join(' ') + ' ' + ciPoints.toReversed().map(p => `L ${x(p.n)} ${y((p.rtp - 1.96 * p.stdErr) * 100)}`).join(' ') + ' Z';
  return <div ref={ref} className="live-plot">
    <svg role="img" aria-label={`${kind === 'rtp' ? 'RTP convergence' : kind === 'rate' ? 'Simulation throughput' : 'Confidence interval precision'} chart, ${points.length} observations`}
      viewBox={`0 0 ${width} ${height}`} tabIndex={points.length ? 0 : undefined}
      onKeyDown={e => { if (['ArrowLeft', 'ArrowRight'].includes(e.key)) { e.preventDefault(); setHover(Math.max(0, Math.min(points.length - 1, index + (e.key === 'ArrowLeft' ? -1 : 1)))); } }}
      onMouseLeave={() => setHover(null)} onMouseMove={e => {
        if (!points.length) return;
        const rect = e.currentTarget.getBoundingClientRect(), n = ((e.clientX - rect.left) / rect.width * width - left) / (width - left - right) * maxN;
        let nearest = 0; points.forEach((p, i) => { if (Math.abs(p.n - n) < Math.abs(points[nearest].n - n)) nearest = i; }); setHover(nearest);
      }}>
      {ticks.map(t => { const v = min + (max - min) * t; return <g key={t}><line className="plot-grid" x1={left} x2={width - right} y1={y(v)} y2={y(v)} /><text className="plot-label" x={left - 10} y={y(v) + 4} textAnchor="end">{fmt(v)}</text></g>; })}
      {ticks.map(t => <text className="plot-label" key={t} x={x(maxN * t)} y={height - 12} textAnchor="middle">{points.length ? width < 450 ? Math.round(maxN * t).toLocaleString(undefined, { notation: 'compact', maximumFractionDigits: 1 }) : count(maxN * t) : '—'}</text>)}
      {kind === 'rtp' && target != null && <line className="plot-target" x1={left} x2={width - right} y1={y(target * 100)} y2={y(target * 100)} />}
      {kind === 'rtp' && reference != null && <line className="plot-reference" x1={left} x2={width - right} y1={y(reference * 100)} y2={y(reference * 100)} />}
      {ciPoints.length > 1 && kind === 'rtp' && <path className="plot-band" d={band} />}
      {points.length > 1 && <path className={`plot-line ${kind}`} d={line} />}
      {selected && <><line className="plot-cursor" x1={x(selected.n)} x2={x(selected.n)} y1={top} y2={height - bottom} /><circle className={`plot-dot ${kind}`} cx={x(selected.n)} cy={y(value(selected))} r="4" /></>}
      {!points.length && <text className="plot-empty" x={width / 2} y={height / 2} textAnchor="middle">Live observations appear when the worker starts</text>}
    </svg>
    <div className="plot-readout">{selected ? <><span>{selected.n.toLocaleString()} rounds</span><strong>{fmt(value(selected))}{kind === 'rate' ? ' / sec' : ''}</strong>{kind === 'rtp' && inference && selected.n > 1 && selected.stdErr > 0 && <span>95% CI [{((selected.rtp - 1.96 * selected.stdErr) * 100).toFixed(3)}, {((selected.rtp + 1.96 * selected.stdErr) * 100).toFixed(3)}]%</span>}</> : <span>Waiting for observed data</span>}</div>
  </div>;
}
