import { useEffect, useRef, useState } from 'react';
import { calculationRequest } from '../../lib/measurements/requests';
import { downloadReport } from '../../lib/results/export';

interface Atom { value: string; probability: string }
interface Input { left: Atom[]; right: Atom[]; unit: string }
interface Report { equal: boolean; leftMean: string; rightMean: string; meanDifference: string; totalVariation: string; cdfDistance: string;
  support: { value: string; leftProbability: string; rightProbability: string; difference: string }[]; unit: string; assumptions: string;
  authoredInputSha256: string; algorithmVersion: string; coreBinarySha256: string | null }
interface Evidence { input: Input; report: Report; retention?: { retained: boolean; note: string } }
const example = (): Input => ({ left: [{ value: '0', probability: '1/2' }, { value: '1.96', probability: '1/2' }],
  right: [{ value: '0', probability: '9/10' }, { value: '9.8', probability: '1/10' }], unit: '× stake' });
function restore(): Input {
  try {
    const value = JSON.parse(localStorage.getItem('slotmath-law-comparison-v1') ?? 'null');
    const atoms = (v: unknown) => Array.isArray(v) && v.length > 0 && v.length <= 128 && v.every(a => a && typeof a.value === 'string' && a.value.length <= 80 && typeof a.probability === 'string' && a.probability.length <= 80);
    if (value && atoms(value.left) && atoms(value.right) && typeof value.unit === 'string' && value.unit.length <= 24) return value;
  } catch { /* recover the independently authored draft */ }
  return example();
}

/** Independent supplied laws. There is no implied association between either input
 * and the game's compiled behavior; retained evidence preserves that distinction. */
export function ExactLawComparison({ runId }: { runId?: string }) {
  const [draft, setDraft] = useState(restore), [result, setResult] = useState<Evidence | null>(null);
  const [busy, setBusy] = useState(false), [error, setError] = useState(''), [status, setStatus] = useState('');
  const controller = useRef<AbortController | null>(null);
  useEffect(() => () => controller.current?.abort(), []);
  const change = (patch: Partial<Input>) => {
    const next = { ...draft, ...patch }; setDraft(next); setResult(null); setError('');
    try { localStorage.setItem('slotmath-law-comparison-v1', JSON.stringify(next)); } catch { /* keep the in-tab draft */ }
  };
  return <details className="exact-law-comparison" id="exact-law-comparison"><summary>Compare exact payout laws · equality, total variation and discrete CDF</summary>
    <p>Enter two independently supplied complete laws for the same subject and unit. Equal expected values can hide different hit probabilities and rare-win exposure. Probability must sum exactly to one on each side.</p>
    {runId && <p>This comparison will be retained with run <code>{runId}</code>; it verifies the supplied laws, whose relationship to the graph must be established separately.</p>}
    <form onSubmit={async event => {
      event.preventDefault(); const input = structuredClone(draft); controller.current = new AbortController(); setBusy(true); setError(''); setResult(null);
      try {
        const endpoint = `/api/runs/${runId ? `${encodeURIComponent(runId)}/` : ''}measurements/reference/comparison`;
        if (runId) {
          const body = await calculationRequest<{ runId: string; report: Report; retention: Evidence['retention'] }>(endpoint, input, controller.current.signal, setStatus, 20000);
          if (body.runId !== runId) throw new Error('Comparison evidence belongs to a different run.');
          setResult({ input, report: body.report, retention: body.retention });
        } else setResult({ input, report: await calculationRequest<Report>(endpoint, input, controller.current.signal, setStatus, 20000) });
      } catch (failure) { setError(failure instanceof Error ? failure.message : 'Law comparison failed.'); }
      finally { setBusy(false); }
    }}><fieldset disabled={busy} className="measurement-reference-fields"><div className="measurement-form-grid">
      <label>Common value unit<input aria-label="Exact comparison unit" maxLength={24} value={draft.unit} onChange={e => change({ unit: e.target.value })} /></label>
      <button type="button" className="btn" onClick={() => change(example())}>Load equal-98%-mean examples</button>
    </div>{(['left', 'right'] as const).map(side => <div key={side}><h4>{side === 'left' ? 'Left law' : 'Right law'}</h4><div className="results-table-scroll"><table className="results-table"><thead><tr><th>Value</th><th>Exact probability</th><th /></tr></thead><tbody>{draft[side].map((atom, i) => <tr key={i}>
      <td><input aria-label={`${side} comparison value ${i + 1}`} maxLength={80} required value={atom.value} onChange={e => change({ [side]: draft[side].map((a, j) => i === j ? { ...a, value: e.target.value } : a) })} /></td>
      <td><input aria-label={`${side} comparison probability ${i + 1}`} maxLength={80} required value={atom.probability} onChange={e => change({ [side]: draft[side].map((a, j) => i === j ? { ...a, probability: e.target.value } : a) })} /></td>
      <td><button type="button" className="btn" disabled={draft[side].length === 1} onClick={() => change({ [side]: draft[side].filter((_, j) => i !== j) })}>Remove</button></td>
    </tr>)}</tbody></table></div><button type="button" className="btn" disabled={draft[side].length >= 128} onClick={() => change({ [side]: [...draft[side], { value: '', probability: '' }] })}>Add {side} outcome</button></div>)}
      <button type="submit" className="btn primary">{busy ? 'Comparing…' : 'Compare exact laws'}</button></fieldset></form>
    {busy && <p role="status">{status} <button type="button" className="btn" onClick={() => controller.current?.abort()}>Cancel comparison</button></p>}
    {error && <p role="alert" className="measurement-error">{error}</p>}
    {result && <div aria-label="Exact law comparison result" className="measurement-reference"><h3>{result.report.equal ? 'Supplied laws are exactly equal' : 'Supplied laws differ'}</h3>
      <p>Left / right mean {result.report.leftMean} / {result.report.rightMean} {result.report.unit} · mean difference {result.report.meanDifference}</p>
      <p>Exact total variation {result.report.totalVariation} · maximum discrete CDF distance {result.report.cdfDistance}</p>
      <p>{result.report.assumptions}</p><div className="results-table-scroll"><table className="results-table"><thead><tr><th>Exact value</th><th>Left probability</th><th>Right probability</th><th>Left − right</th></tr></thead><tbody>{result.report.support.map(atom => <tr key={atom.value}><td>{atom.value}</td><td>{atom.leftProbability}</td><td>{atom.rightProbability}</td><td>{atom.difference}</td></tr>)}</tbody></table></div>
      {result.retention && <p role="status">{result.retention.note}</p>}
      <details><summary>Comparison identity, inputs and result</summary><pre>{JSON.stringify(result, null, 2)}</pre></details>
      <button type="button" className="btn" onClick={() => downloadReport(`slotmath-${runId ?? 'independent'}-exact-law-comparison.json`, JSON.stringify(result, null, 2), 'application/json')}>Export exact comparison</button>
    </div>}
  </details>;
}
