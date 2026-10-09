import { useState } from 'react';
import type { LiveProgress } from '../../hooks/useSimulation';
import { count } from './format';
export function LiveHistogram({ progress }: { progress: LiveProgress | null }) {
  const [logScale, setLogScale] = useState(true);
  const bands = [{ lo: 0, hi: 0, label: 'No win', count: progress ? progress.sampleCount - progress.nonZeroCount : 0 },
    ...[[0, 1], [1, 5], [5, 10], [10, 20], [20, 50], [50, 100], [100, 1000], [1000, Infinity]].map(([lo, hi]) => ({ lo, hi, count: 0, label: hi === Infinity ? '1k×+' : lo === 0 ? '<1×' : `${lo}–${hi}×` }))];
  for (const bin of progress?.histogram ?? []) {
    const band = bands.find((b, i) => i > 0 && bin.lo >= b.lo && bin.lo < b.hi);
    if (band) band.count += bin.count;
  }
  bands[1].count -= bands[0].count;
  const transform = (n: number) => logScale ? Math.log10(n + 1) : n;
  const max = Math.max(1, ...bands.map(b => transform(b.count)));
  return <div className="live-histogram">
    <div className="hist-scale"><span>All completed paid rounds · bonus included</span><button className="btn" aria-pressed={logScale} onClick={() => setLogScale(!logScale)}>{logScale ? 'Log scale' : 'Linear scale'}</button></div>
    <div className="payout-bars" role="list" aria-label="Observed payout distribution">
      {bands.map((b, i) => <div role="listitem" key={b.label} className="payout-bin" title={`${b.label}: ${b.count.toLocaleString()} rounds (${progress?.sampleCount ? (b.count / progress.sampleCount * 100).toFixed(3) : '0'}%)`}>
        <span className="bin-count">{b.count ? count(b.count) : '—'}</span><div className="bin-track"><div className={`bin-fill ${i === 0 ? 'zero' : ''}`} style={{ height: `${transform(b.count) / max * 100}%` }} /></div><span className="bin-label">{b.label}</span>
      </div>)}
    </div>
    <div className="plot-readout"><span>{progress?.sampleCount ? `${progress.sampleCount.toLocaleString()} observations` : 'Waiting for observed payouts'}</span><span>Stake multiples · counts {logScale ? 'on logarithmic scale' : 'on linear scale'}</span></div>
  </div>;
}
