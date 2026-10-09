import { useRef, useState, useEffect } from 'react';
import { MODES, MODE_LABELS, graphRequest, downloadJson, type GraphAnalysis, type MathMode } from './api';
import type { GraphRound } from './api';
import { createExpectationGraph } from './expectationGraph';
import { loadProject } from '../../lib/projectFiles';
import { useAppStore } from '../../store';
import { useNavigate } from 'react-router-dom';
const percent = (value?: number) => value === undefined ? '—' : (value * 100).toFixed(4) + '%';
export default function MathPanel({ config }: { config: Record<string, unknown> }) {
  return <GraphMathPanel key={JSON.stringify(config)} config={config} />;
}
function GraphMathPanel({ config }: { config: Record<string, unknown> }) {
  const [results, setResults] = useState<Partial<Record<MathMode, GraphAnalysis>>>({});
  const [running, setRunning] = useState<MathMode | null>(null);
  const [samples, setSamples] = useState(10000), [seed, setSeed] = useState(42), [dop, setDop] = useState(1), [epsilon, setEpsilon] = useState(0.1), [error, setError] = useState('');
  const controller = useRef<AbortController | null>(null);
  const navigate = useNavigate();
  const [reference, setReference] = useState<GraphRound | null>(null);
  const [proofRunning, setProofRunning] = useState(false);
  const target = Number((config.initialState as Record<string, unknown>)?.targetRtpPercent) / 100;
  const referenceValue = (key: string) => {
    const value = reference?.state[key];
    if (value && typeof value === 'object' && 'displayValue' in value) return Number(value.displayValue);
    if (value && typeof value === 'object' && 'numerator' in value && 'denominator' in value) return Number(value.numerator) / Number(value.denominator);
    return value === undefined ? undefined : Number(value);
  };
  useEffect(() => () => controller.current?.abort(), []);
  const applyCalibration = () => {
    try {
      if (reference?.state.calibrationFeasible !== true) throw new Error('Target is outside the multiplier range. Edit the free-spin reels in the constructor.');
      const weights = [referenceValue('calibratedWeight2')!, referenceValue('calibratedWeight3')!];
      if (weights.some(x => !Number.isSafeInteger(x) || x < 0) || weights.every(x => x === 0)) throw new Error('Calibration produced invalid integer weights.');
      const calibrated = structuredClone(config);
      const mechanic = (calibrated.mechanics as Record<string, { nodes: { id: string; drawWeights?: { outcomeId: string; weight: number; value: number }[] }[] }>)['dog-free-spin'];
      for (let col = 1; col <= 3; col++) mechanic.nodes.find(x => x.id === `wild-${col}`)!.drawWeights = weights.map((weight, i) => ({ outcomeId: String(i + 2), weight, value: 0 }));
      loadProject(calibrated); navigate('/build');
    } catch (e) { setError(e instanceof Error ? e.message : 'Calibration failed'); }
  };
  const run = async (modes: MathMode[]) => {
    if (!Number.isSafeInteger(seed) || !Number.isInteger(samples) || samples < 1 || samples > 1000000 || epsilon <= 0 || epsilon >= 1) {
      setError('Use an integer seed, 1–1,000,000 samples and epsilon between 0 and 1.'); return;
    }
    setError(''); controller.current = new AbortController();
    try { for (const mode of modes) {
      setRunning(mode);
      const result = await graphRequest<GraphAnalysis>('evaluate/graph', { config, mode, seed, samples, degreeOfParallelism: dop, epsilon }, controller.current.signal);
      setResults(prev => ({ ...prev, [mode]: result }));
    } } catch (e) { if (!controller.current.signal.aborted) setError(e instanceof Error ? e.message : 'Analysis failed'); }
    finally { setRunning(null); }
  };
  return <div className="dh-math">
    <p className="dh-model-note">The target is measured per complete paid round, including its free spins. The current constructor graph runs through the shared compiler and interpreters.</p>
    <p data-testid="rtp-target"><b>Target RTP: {percent(target)}</b> · {reference ? (reference.state.targetMet === true ? 'Target verified' : 'Target not met') : 'Run the expectation graph to verify'}</p>
    <div className="dh-reference"><b>Independent expectation graph</b><p>Ordinary AST and bounded loops calculate rational payline expectations, the bonus-count PMF and Sticky Wild marginals. This proves expectation under uniform circular stops and a nonbinding cap; it does not calculate the full payout PMF.</p>
      <div className="dh-actions"><button disabled={proofRunning || !!running} onClick={async () => { setProofRunning(true); setError(''); try { setReference(await graphRequest<GraphRound>('play/round', { config: createExpectationGraph(config) })); } catch(e) { setError(e instanceof Error ? e.message : 'Reference failed'); } finally { setProofRunning(false); } }}>{proofRunning ? 'Calculating…' : 'Run rational expectation graph'}</button>
      <button disabled={proofRunning || !!running} onClick={() => { try { const proof = createExpectationGraph(config); useAppStore.setState({ verificationSource: config, graphTrail: [] }); loadProject(proof); navigate('/build'); } catch(e) { setError(e instanceof Error ? e.message : 'Reference failed'); } }}>Open expectation graph in constructor</button></div>
      {reference && <div data-testid="reference-result"><p><b>Exact expectation: {percent(referenceValue('expectedRtp'))}</b></p><p>Base {percent(referenceValue('baseRtp'))} · scatter {percent(referenceValue('scatterRtp'))} · bonus {percent(referenceValue('bonusRtp'))}</p><code className="dh-rational">{JSON.stringify(reference.state.expectedRtp)}</code>
        <p>The graph checks the target within 10⁻¹⁰ percentage points. Integer probabilities retain their exact rational result above.</p>
        <details><summary>Calibration calculated in the constructor graph</summary><p>All-×2 RTP {percent(referenceValue('calibrationRtp2'))} · all-×3 RTP {percent(referenceValue('calibrationRtp3'))}. P(×3) = (target − RTP₂) / (RTP₃ − RTP₂).</p><p>Suggested free-spin Wild weights: ×2 {referenceValue('calibratedWeight2')} · ×3 {referenceValue('calibratedWeight3')}.</p></details>
        {reference.state.targetMet !== true && <>{reference.state.calibrationFeasible === true ? <button className="dh-primary" disabled={!!running || proofRunning} onClick={applyCalibration}>Apply calibrated Wild weights</button> : <p role="alert">Target is outside the multiplier range. Edit free-spin reel probabilities in Build before calibrating.</p>}</>}
      </div>}
    </div>
    <div className="dh-analysis-controls">
      <label>Samples<input aria-label="Analysis samples" type="number" min="1" max="1000000" value={samples} disabled={!!running} onChange={e => setSamples(Number(e.target.value))} /></label>
      <label>Seed<input aria-label="Analysis seed" type="number" value={seed} disabled={!!running} onChange={e => setSeed(Number(e.target.value))} /></label>
      <label>Workers<select aria-label="Analysis workers" value={dop} disabled={!!running} onChange={e => setDop(Number(e.target.value))}>{[1, 2, 3, 4].map(n => <option key={n}>{n}</option>)}</select></label>
      <label>ε<input aria-label="Analysis epsilon" type="number" step="0.00001" value={epsilon} disabled={!!running} onChange={e => setEpsilon(Number(e.target.value))} /></label>
      <button className="dh-primary" disabled={!!running} onClick={() => void run(MODES)}>{running ? `Running ${MODE_LABELS[running]}…` : 'Verify all modes'}</button>
      {running && <button onClick={() => controller.current?.abort()}>Cancel analysis</button>}
    </div>
    {error && <p role="alert" className="dh-error">{error}</p>}
    <div className="dh-table-scroll"><table className="dh-math-table"><thead><tr><th>Requested</th><th>Round RTP</th><th>Bounds / approximate 95% CI</th><th>Actual evidence</th><th /></tr></thead><tbody>
      {MODES.map(mode => { const r = results[mode]; return <tr key={mode} data-testid={`math-${mode}`}><th>{MODE_LABELS[mode]}</th><td>{percent(r?.rtp)}</td>
        <td>{r?.lower !== undefined ? `${percent(r.lower)} – ${percent(r.upper)}` : '—'}</td>
        <td>{r ? `${r.actualStrategy} · ${r.status}` : 'Pending'}{r && <small>{r.provenance}{r.samples ? ` · n=${r.samples.toLocaleString()}` : ''}</small>}{r?.lower !== undefined && reference && <small>{r.lower <= referenceValue('expectedRtp')! && referenceValue('expectedRtp')! <= r.upper! ? 'Contains expectation reference' : 'Does not contain expectation reference'}</small>}</td>
        <td><button disabled={!!running} onClick={() => void run([mode])}>Run {MODE_LABELS[mode]}</button></td></tr>; })}
    </tbody></table></div>
    <p>Exact and ε-pruned have a 10,000-branch / 3-second budget. Exceeding it produces no RTP. Pruning can produce a broad cap-based interval. Auto uses the existing Exact/Sampled selector; its actual strategy is shown. Calculations stop after 45 seconds and label partial samples.</p>
    {Object.values(results).map(r => <details key={r.requestedMode}><summary>{MODE_LABELS[r.requestedMode]} evidence · {(r.elapsedMs / 1000).toFixed(2)} s</summary>
      {r.note && <p>{r.note}</p>}<p>Graph SHA-256: <code>{r.configHash}</code></p>{r.rationalRtp && <code className="dh-rational">{r.rationalRtp}</code>}
      {r.prunedMass !== undefined && <p>Pruned probability: {r.prunedMass.toExponential(5)}</p>}<p>Seed {r.seed ?? '—'} · workers {r.degreeOfParallelism ?? '—'} · completed samples {r.samples ?? 0} · {r.streamScheme ?? 'no samples'}</p></details>)}
    <div className="dh-actions"><button onClick={() => downloadJson('dog-house-constructor-graph.json', config)}>Export graph JSON</button>
      <button disabled={!Object.keys(results).length && !reference} onClick={() => downloadJson('dog-house-graph-verification.json', { config, targetRtp: target, targetVerified: reference?.state.targetMet === true, results, expectationGraph: reference ? createExpectationGraph(config) : null, reference, exportedAt: new Date().toISOString() })}>Export verification JSON</button></div>
  </div>;
}
