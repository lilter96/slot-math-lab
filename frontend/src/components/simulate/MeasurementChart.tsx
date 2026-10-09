import { useState } from 'react';
import type { LivePoint } from '../../hooks/useSimulation';
import { formatStatistic } from '../../lib/measurements/model';
export function MeasurementChart({ id, name, unit, points }: { id: string; name: string; unit: string; points: LivePoint[] }) {
  const [hover, setHover] = useState<number | null>(null), [mode, setMode] = useState<'mean' | 'range'>('mean');
  const data = points.flatMap(p => { const metric = p.measurements?.find(m => m.id === id); return metric?.count && metric.mean != null && metric.min != null && metric.max != null ? [{ n: p.n, metric }] : []; });
  const width = 720, height = 220, left = 72, right = 18, top = 20, bottom = 36;
  const values = data.flatMap(d => mode === 'range' ? [d.metric.min!, d.metric.max!] : [d.metric.mean!]), low = Math.min(...values, 0), high = Math.max(...values, 1);
  const margin = (high - low) * .1, min = low - margin, max = high + margin, maxN = Math.max(1, data.at(-1)?.n ?? 1);
  const x = (n: number) => left + n / maxN * (width - left - right), y = (v: number) => top + (max - v) / (max - min) * (height - top - bottom);
  const selected = data[Math.min(hover ?? data.length - 1, data.length - 1)];
  const path = data.map((d, i) => `${i ? 'L' : 'M'}${x(d.n)},${y(d.metric.mean!)}`).join(' ');
  const range = data.map((d, i) => `${i ? 'L' : 'M'}${x(d.n)},${y(d.metric.max!)}`).join(' ') + ' ' + data.toReversed().map(d => `L${x(d.n)},${y(d.metric.min!)}`).join(' ') + ' Z';
  return <div className="measurement-plot"><div className="measurement-chart-controls" aria-label={`${name} chart display`}><span>{mode === 'mean' ? 'Cumulative average' : 'Average with observed range'}</span><button className="btn" aria-pressed={mode === 'mean'} onClick={() => setMode('mean')}>Average trend</button><button className="btn" aria-pressed={mode === 'range'} onClick={() => setMode('range')}>Observed range</button></div><svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label={`${name}: ${mode === 'mean' ? 'cumulative average' : 'cumulative average and observed min/max'}, ${data.length} snapshots`} tabIndex={data.length ? 0 : undefined}
    onKeyDown={e => { if (['ArrowLeft', 'ArrowRight'].includes(e.key)) { e.preventDefault(); setHover(Math.max(0, Math.min(data.length - 1, (hover ?? data.length - 1) + (e.key === 'ArrowRight' ? 1 : -1)))); } }}
    onMouseLeave={() => setHover(null)} onMouseMove={e => { if (!data.length) return; const rect = e.currentTarget.getBoundingClientRect(), n = ((e.clientX - rect.left) / rect.width * width - left) / (width - left - right) * maxN; let index = 0; data.forEach((d, i) => { if (Math.abs(d.n - n) < Math.abs(data[index].n - n)) index = i; }); setHover(index); }}>
    {[0, 1, 2, 3, 4].map(i => { const v = min + (max - min) * i / 4; return <g key={i}><line className="plot-grid" x1={left} x2={width - right} y1={y(v)} y2={y(v)} /><text className="plot-label" x={left - 8} y={y(v) + 4} textAnchor="end">{v.toLocaleString(undefined, { maximumFractionDigits: 2, notation: 'compact' })}</text><text className="plot-label" x={x(maxN * i / 4)} y={height - 10} textAnchor="middle">{Math.round(maxN * i / 4).toLocaleString()}</text></g>; })}
    {data.length > 1 && <>{mode === 'range' && <path className="plot-band" d={range} />}<path className="plot-line" d={path} /></>}
    {selected && <><line className="plot-cursor" x1={x(selected.n)} x2={x(selected.n)} y1={top} y2={height - bottom} /><circle className="plot-dot" cx={x(selected.n)} cy={y(selected.metric.mean!)} r="4" /></>}
    {!data.length && <text className="plot-empty" x={width / 2} y={height / 2} textAnchor="middle">Waiting for matching observations</text>}
  </svg><div className="plot-readout">{selected ? <><span>{selected.n.toLocaleString()} paid rounds</span><strong>Average {formatStatistic(selected.metric.mean, 'mean', unit)}</strong><span>Range {formatStatistic(selected.metric.min, 'min', unit)} – {formatStatistic(selected.metric.max, 'max', unit)}</span><span>{selected.metric.count.toLocaleString()} matches</span></> : <span>Collection starts with the next run</span>}</div><p>{mode === 'range' ? 'Observed range, not a confidence interval.' : 'Descriptive cumulative average; range has its own display.'} Repeated events within a round may be correlated.</p></div>;
}
