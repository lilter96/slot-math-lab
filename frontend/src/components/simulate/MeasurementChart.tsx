import { useState } from 'react';
import type { LivePoint } from '../../hooks/useSimulation';
import { usePlotWidth } from '../../hooks/usePlotWidth';
import { availableReducers, chartReducer, formatStatistic, reducerNames, type MetricDraft } from '../../lib/measurements/model';
import { trendStatistic } from '../../lib/measurements/trends';

export function MeasurementChart({ metric, points, customize }: { metric: MetricDraft; points: LivePoint[]; customize(patch: Partial<MetricDraft>): void }) {
  const [hover, setHover] = useState<number | null>(null), [showRange, setShowRange] = useState(false);
  const { ref, width } = usePlotWidth(720);
  const reducer = chartReducer(metric), rangeMode = showRange && reducer === 'mean';
  const data = points.map(point => { const value = point.measurements?.find(m => m.id === metric.id); return { n: point.n, metric: value, value: trendStatistic(value, reducer) }; });
  const available = data.filter(d => d.value != null);
  const height = 240, left = 84, right = 28, top = 20, bottom = 52;
  const ticks = width < 450 ? [0, .5, 1] : [0, .25, .5, .75, 1];
  const values = available.flatMap(d => rangeMode && d.metric?.min != null && d.metric.max != null ? [d.metric.min, d.metric.max] : [d.value!]);
  const low = values.length ? Math.min(...values) : 0, high = values.length ? Math.max(...values) : 1;
  const margin = high > low ? (high - low) * .1 : Math.max(1, Math.abs(high)) * .1;
  const min = low - margin, max = high + margin, maxN = Math.max(1, data.at(-1)?.n ?? 1);
  const x = (n: number) => left + n / maxN * (width - left - right), y = (v: number) => top + (max - v) / (max - min) * (height - top - bottom);
  const selected = data[Math.min(hover ?? data.length - 1, data.length - 1)];
  const path = data.map((d, i) => d.value == null ? '' : `${i > 0 && data[i - 1].value != null ? 'L' : 'M'}${x(d.n)},${y(d.value)}`).join(' ');
  // Missing samples split both the line and the observed-range envelope.
  const runs: typeof data[] = [];
  for (const point of rangeMode ? data : []) {
    if (point.value == null || point.metric?.min == null || point.metric.max == null) { if (runs.at(-1)?.length) runs.push([]); continue; }
    if (!runs.length) runs.push([]); runs.at(-1)!.push(point);
  }
  const range = runs.filter(run => run.length > 1).map(run => run.map((d, i) => `${i ? 'L' : 'M'}${x(d.n)},${y(d.metric!.max!)}`).join(' ') + ' ' + run.toReversed().map(d => `L${x(d.n)},${y(d.metric!.min!)}`).join(' ') + ' Z').join(' ');
  function selectAt(element: SVGSVGElement, clientX: number) {
    if (!data.length) return;
    const rect = element.getBoundingClientRect(), n = ((clientX - rect.left) / rect.width * width - left) / (width - left - right) * maxN;
    let index = 0; data.forEach((d, i) => { if (Math.abs(d.n - n) < Math.abs(data[index].n - n)) index = i; }); setHover(index);
  }
  return <div ref={ref} className="measurement-plot"><div className="measurement-chart-controls" aria-label={`${metric.name} chart display`}>
    <label>Chart statistic<select aria-label={`Chart statistic for ${metric.name}`} value={reducer} onChange={e => { customize({ chartStatistic: e.target.value as typeof reducer }); setHover(null); }}>
      {availableReducers(metric).map(r => <option key={r} value={r}>{reducerNames[r]}</option>)}
    </select></label>
    {reducer === 'mean' && <button className="btn" aria-pressed={showRange} onClick={() => setShowRange(v => !v)}>Observed range</button>}
  </div><svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label={`${metric.name}: cumulative ${reducerNames[reducer].toLowerCase()}${rangeMode ? ' and observed min/max' : ''}, ${available.length} available snapshots`} tabIndex={data.length ? 0 : undefined}
    onKeyDown={e => { if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(e.key)) { e.preventDefault(); setHover(e.key === 'Home' ? 0 : e.key === 'End' ? data.length - 1 : Math.max(0, Math.min(data.length - 1, (hover ?? data.length - 1) + (e.key === 'ArrowRight' ? 1 : -1)))); } }}
    onPointerLeave={e => { if (e.pointerType === 'mouse') setHover(null); }} onPointerMove={e => { if (e.pointerType === 'mouse') selectAt(e.currentTarget, e.clientX); }} onPointerDown={e => selectAt(e.currentTarget, e.clientX)}>
    <desc>Each snapshot summarizes the pinned observation population up to its paid-round count. Arrow keys, Home and End inspect snapshots. Missing values break the trend.</desc>
    {ticks.map(t => { const v = min + (max - min) * t; return <g key={t}><line className="plot-grid" x1={left} x2={width - right} y1={y(v)} y2={y(v)} /><text className="plot-label" x={left - 8} y={y(v) + 4} textAnchor="end">{reducer === 'matchRate' ? `${(v * 100).toLocaleString(undefined, { maximumFractionDigits: 1 })}%` : v.toLocaleString(undefined, { maximumFractionDigits: 3, notation: 'compact' })}</text><text className="plot-label" x={x(maxN * t)} y={height - 28} textAnchor="middle">{Math.round(maxN * t).toLocaleString(undefined, { notation: width < 450 ? 'compact' : 'standard', maximumFractionDigits: 1 })}</text></g>; })}
    <text className="plot-label" x={(left + width - right) / 2} y={height - 8} textAnchor="middle">Completed paid rounds</text>
    {available.length > 1 && <>{rangeMode && <path className="plot-band" d={range} />}<path className="plot-line" d={path} /></>}
    {data.map((d, i) => d.value != null && d !== selected && (i === 0 || data[i - 1].value == null) && (i === data.length - 1 || data[i + 1].value == null) ? <circle key={d.n} className="plot-dot" cx={x(d.n)} cy={y(d.value)} r="2.5" /> : null)}
    {selected && <><line className="plot-cursor" x1={x(selected.n)} x2={x(selected.n)} y1={top} y2={height - bottom} />{selected.value != null && <circle className="plot-dot" cx={x(selected.n)} cy={y(selected.value)} r="4" />}</>}
    {!available.length && <text className="plot-empty" x={width / 2} y={height / 2} textAnchor="middle">{data.length ? 'No defined values for this statistic' : 'Waiting for completed paid rounds'}</text>}
  </svg><div className="plot-readout" aria-live="polite">{selected ? <><span>{selected.n.toLocaleString()} paid rounds</span><strong>{reducerNames[reducer]} {formatStatistic(selected.value, reducer, metric.unit)}</strong>{rangeMode && selected.metric && <span>Range {formatStatistic(selected.metric.min, 'min', metric.unit)} – {formatStatistic(selected.metric.max, 'max', metric.unit)}</span>}<span>{(selected.metric?.count ?? 0).toLocaleString()} matches</span></> : <span>Collection starts with the next run</span>}</div>
  <p>Cumulative {reducerNames[reducer].toLowerCase()} for the metric’s full filtered population. {rangeMode ? 'The band is the observed range, not a confidence interval. ' : ''}Snapshots use completed paid rounds on the horizontal axis. {data.some(d => d.value == null) ? 'Unavailable values remain gaps; older checkpoints may contain only basic statistics. ' : ''}Changing the display keeps the pinned collection plan.</p></div>;
}
