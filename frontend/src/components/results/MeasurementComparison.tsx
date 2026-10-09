import { compareMeasurements } from '../../lib/measurements/comparison';
import type { RunSnapshot } from '../../lib/realtime/runProtocol';
import { number } from '../../lib/results/model';
export function MeasurementComparison({ selected, baseline }: { selected: RunSnapshot; baseline: RunSnapshot }) {
  const metrics = compareMeasurements(selected, baseline);
  return <div className="results-measurement-comparison"><h4>Component, feature and distribution effects</h4><p>Compare the same authored populations across model versions, parameters, seeds or engines. Deltas are selected minus baseline. Changed observation rules and units withhold a numerical difference.</p>
    {!metrics.length && <p>No shared measurement IDs. Pin the same collection plan in both runs to compare components and scoped features.</p>}
    {metrics.map(m => <details key={m.id}><summary>{m.name} · mean difference {number(m.meanDelta)} {m.unit}</summary><p>{m.note}</p><div className="results-table-scroll"><table className="results-table"><thead><tr><th>Statistic</th><th>Selected</th><th>Baseline</th><th>Difference / effect</th></tr></thead><tbody>
      <tr><td>Valid / eligible subjects</td><td>{m.selected.count.toLocaleString()} / {m.selected.observations.toLocaleString()}</td><td>{m.baseline.count.toLocaleString()} / {m.baseline.observations.toLocaleString()}</td><td>Separate denominators</td></tr>
      <tr><td>Mean</td><td>{number(m.selected.mean)}</td><td>{number(m.baseline.mean)}</td><td>{number(m.meanDelta)}</td></tr>
      <tr><td>Mean difference interval</td><td colSpan={3}>{m.meanDifferenceInterval ? `[${number(m.meanDifferenceInterval[0])}, ${number(m.meanDifferenceInterval[1])}]` : 'Withheld'}</td></tr>
      <tr><td>Sample variance</td><td>{number(m.selected.analysis?.moments.sampleVariance)}</td><td>{number(m.baseline.analysis?.moments.sampleVariance)}</td><td>{number(m.varianceDelta)}</td></tr>
      <tr><td>Payout / denominator ratio</td><td>{number(m.selected.analysis?.pair?.ratio)}</td><td>{number(m.baseline.analysis?.pair?.ratio)}</td><td>{number(m.ratioDelta)}</td></tr>
      <tr><td>Ordinary weighted target mean</td><td>{number(m.selected.analysis?.weights?.ordinaryEstimate)}</td><td>{number(m.baseline.analysis?.weights?.ordinaryEstimate)}</td><td>{number(m.weightedMeanDelta)}</td></tr>
      <tr><td>Empirical full-support total variation / CDF distance</td><td colSpan={3}>{number(m.totalVariation)} / {number(m.cdfDistance)} · descriptive effects, no continuous-KS verdict</td></tr>
      <tr><td>Excluded / invalid</td><td>{m.selected.excluded.toLocaleString()} / {m.selected.errors.toLocaleString()}</td><td>{m.baseline.excluded.toLocaleString()} / {m.baseline.errors.toLocaleString()}</td><td>{m.populationCompatible ? 'Compatible authored collection plan' : 'Incompatible collection plan'}</td></tr>
    </tbody></table></div></details>)}
  </div>;
}
