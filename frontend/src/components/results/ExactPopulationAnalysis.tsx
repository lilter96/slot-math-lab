import {useEffect, useRef, useState} from 'react';
import type {components} from '../../api/generated-types';
import type {RunEvidence} from '../../lib/results/model';
import {calculationRequest} from '../../lib/measurements/requests';
import {downloadJson} from '../../lib/graphApi';
import {formatNumber} from '../../lib/numberFormat';
import './ExactPopulationAnalysis.css';

type Output = components['schemas']['EvaluationOutput'];
type ExactNumber = components['schemas']['ExactNumberOutput'];
type Analysis = components['schemas']['PayoutAnalysisOutput'];
type Evidence = 'Mean' | 'Moments' | 'Report';
const split = (text: string) => text.split(',').map(value => value.trim()).filter(Boolean);

function Value({value}: { value?: ExactNumber | null }) {
    return value ? <span
            className="population-number"><strong>{value.display != null && Number.isFinite(value.display) ? formatNumber(value.display, 6) : 'Exact rational'}</strong><code>{value.rational}</code></span> :
        <span className="population-unavailable">Unavailable</span>;
}

/** Population results describe resets of the pinned graph, separately from saved observations. */
export function ExactPopulationAnalysis({evidence}: { evidence: RunEvidence }) {
    const [scope, setScope] = useState<Evidence>('Moments'), [order, setOrder] = useState(4);
    const [stake, setStake] = useState('1'), [width, setWidth] = useState('1/100');
    const [levels, setLevels] = useState('19/20, 99/100'), [thresholds, setThresholds] = useState('');
    const [horizon, setHorizon] = useState(false), [rounds, setRounds] = useState(100), [reserve, setReserve] = useState('');
    const [diagnostics, setDiagnostics] = useState(false), [busy, setBusy] = useState(false), [status, setStatus] = useState(''), [error, setError] = useState('');
    const [result, setResult] = useState<{
        request: components['schemas']['GraphEvaluationRequest'];
        output: Output
    } | null>(null);
    const operation = useRef<AbortController | null>(null);
    useEffect(() => () => operation.current?.abort(), []);
    const full = scope === 'Report';

    async function calculate() {
        if (!evidence.inputVerified || !evidence.pinnedConfig) return;
        const abort = new AbortController();
        operation.current = abort;
        setBusy(true);
        setError('');
        setResult(null);
        const request: components['schemas']['GraphEvaluationRequest'] = {
            config: evidence.pinnedConfig, mode: 'Exact', evidence: scope, diagnostics,
            seed: 42, samples: 1, degreeOfParallelism: 1, epsilon: .00001, maxBranches: 10000,
            analysis: scope === 'Mean' ? null : {
                momentOrder: order, stake, rtpHalfWidth: width || null,
                tailProbabilities: full ? split(levels) : [], payoutThresholds: full ? split(thresholds) : [],
                independentRounds: full && horizon ? {rounds, initialReserve: reserve || null} : null,
            },
        };
        try {
            const output = await calculationRequest<Output>('/api/evaluate/graph', request, abort.signal, setStatus, 60000);
            abort.signal.throwIfAborted();
            if (output.configHash !== evidence.run.configHash) throw new Error('The result fingerprint differs from this pinned graph.');
            setResult({request, output});
            setStatus('Calculation complete');
        } catch (err) {
            if (!abort.signal.aborted) {
                setStatus('Calculation failed. No result retained.');
                setError(err instanceof Error ? err.message : 'Exact analysis failed.');
            }
        } finally {
            if (!abort.signal.aborted) setBusy(false);
        }
    }

    return <section className="results-card exact-population" aria-labelledby="exact-population-heading">
        <div className="results-card-head">
            <div><h3 id="exact-population-heading">Exact population analysis</h3><p>Moments, payout tails and operator
                exposure from the pinned graph.</p></div>
            <span className="results-source-tag">Reset-to-initial-state model</span></div>
        <p className="population-note">Each round starts from the graph’s initial state and includes its complete
            bounded features. These mathematical population results are separate from the saved sample.</p>
        {!!evidence.run.execution?.persistentKeys.length &&
            <p className="population-note">The saved run retains state between rounds. This analysis describes
                independent resets and does not establish that run’s session or stationary return.</p>}
        <form onSubmit={event => {
            event.preventDefault();
            void calculate();
        }} onChange={() => {
            setResult(null);
            setError('');
            setStatus('');
        }}>
            <fieldset disabled={busy || !evidence.inputVerified}>
                <div className="population-fields"><label>Calculate<select aria-label="Calculate" value={scope}
                                                                           onChange={event => setScope(event.target.value as Evidence)}>
                    <option value="Moments">Moments and hit frequency</option>
                    <option value="Report">Full law and risk metrics</option>
                    <option value="Mean">Mean only</option>
                </select></label>
                    {scope !== 'Mean' && <><label>Highest moment<select aria-label="Highest moment" value={order}
                                                                        onChange={event => setOrder(Number(event.target.value))}>
                        <option value={2}>2 · variance</option>
                        <option value={3}>3 · skewness</option>
                        <option value={4}>4 · excess kurtosis</option>
                    </select></label><label>Fixed stake · credits<input required maxLength={160} value={stake}
                                                                        onChange={event => setStake(event.target.value)}/></label><label>RTP
                        half-width · optional<input maxLength={160} value={width}
                                                    placeholder="1/100 = 1 percentage point"
                                                    onChange={event => setWidth(event.target.value)}/></label></>}
                </div>
                <p className="population-note">{scope === 'Mean' ? 'Returns the unit-stake graph expectation. Higher moments, hit frequency and payout law are not requested.' : full ? 'Requests a complete exact law within the solver limits. Unavailable tails remain withheld when only moments can be proved.' : 'Skips the optional payout law. Caps and exact settlement can still require a law internally. Tail risk cannot be inferred from four moments.'} Use
                    integers or fractions such as 19/20 for exact inputs.</p>
                {full && <>
                    <div className="population-fields"><label>Tail levels · comma separated<input maxLength={2575}
                                                                                                  value={levels}
                                                                                                  onChange={event => setLevels(event.target.value)}/></label><label>Payout
                        thresholds · optional<input maxLength={2575} value={thresholds} placeholder="10, 100, 1000"
                                                    onChange={event => setThresholds(event.target.value)}/></label>
                    </div>
                    <label className="population-check"><input type="checkbox" checked={horizon}
                                                               onChange={event => setHorizon(event.target.checked)}/>Include
                        independent-round operator exposure</label>
                    {horizon &&
                        <div className="population-fields"><label>Settled rounds<input type="number" required min={0}
                                                                                       max={1000000} step={1}
                                                                                       value={rounds}
                                                                                       onChange={event => setRounds(Number(event.target.value))}/></label><label>Initial
                            reserve · optional credits<input maxLength={160} value={reserve}
                                                             placeholder="1000 · enables first-passage ruin"
                                                             onChange={event => setReserve(event.target.value)}/></label>
                        </div>}
                </>}
                <label className="population-check"><input type="checkbox" checked={diagnostics}
                                                           onChange={event => setDiagnostics(event.target.checked)}/>Collect
                    solver and logical storage diagnostics</label>
            </fieldset>
            <div className="population-actions">
                <button className="btn primary"
                        disabled={busy || !evidence.inputVerified || !evidence.pinnedConfig}>{busy ? 'Calculating exact evidence…' : 'Calculate exact population'}</button>
                {busy && <button type="button" className="btn" onClick={() => {
                    operation.current?.abort();
                    setBusy(false);
                    setStatus('Calculation cancelled. No result retained.');
                }}>Cancel analysis</button>}{status && <span role="status">{status}</span>}</div>
        </form>
        {!evidence.inputVerified &&
            <p className="population-note">Verify the saved input before calculating population evidence.</p>}
        {error && <p className="results-error" role="alert">{error}</p>}
        {result && <PopulationResult output={result.output} diagnostics={diagnostics}/>}
        {result && <button className="btn"
                           onClick={() => downloadJson(`run-${evidence.run.id}-exact-population.json`, {runId: evidence.run.id, ...result})}>Export
            population evidence</button>}
    </section>;
}

function PopulationResult({output, diagnostics}: { output: Output; diagnostics: boolean }) {
    const a = output.analysis, known = output.provenance === 'Exact' && output.status === 'Complete';
    const moments: [string, ExactNumber | null | undefined][] = [['E[X²]', a?.secondMoment], ['E[X³]', a?.thirdMoment], ['E[X⁴]', a?.fourthMoment], ['Central moment 3', a?.centralThirdMoment], ['Central moment 4', a?.centralFourthMoment], ['Squared coefficient of variation', a?.coefficientOfVariationSquared], ['Squared skewness', a?.skewnessSquared]];
    return <div className="population-result" aria-label="Exact population result">
        <div className={`population-result-heading ${known ? 'known' : 'unavailable'}`}>
            <strong>{known ? a?.status === 'ExactLaw' ? 'Complete exact law' : a?.status === 'ExactMoments' ? 'Exact population moments' : 'Exact expectation' : 'Exact analysis unavailable'}</strong><span>{output.method ?? output.status} · {formatNumber(output.elapsedMs, 1)} ms</span>
        </div>
        <p className="population-note">{output.note}</p>{a?.reason &&
        <p role="status" className="population-note">{a.reason}</p>}
        {known && <>
            <dl className="population-metrics">
                <div>
                    <dt>Mean payout · credits</dt>
                    <dd><Value value={a?.mean ?? {rational: output.rationalRtp ?? '', display: output.rtp ?? null}}/>
                    </dd>
                </div>
                <div>
                    <dt>RTP for declared stake</dt>
                    <dd><Value value={a?.rtpForStake}/></dd>
                </div>
                <div>
                    <dt>Population variance · credits²</dt>
                    <dd><Value value={a?.variance}/></dd>
                </div>
                <div>
                    <dt>Skewness · approximate display</dt>
                    <dd>{a?.skewness != null ? formatNumber(a.skewness, 6) : 'Unavailable'}<small>Undefined at zero
                        variance; requires moment 3.</small></dd>
                </div>
                <div>
                    <dt>Excess kurtosis</dt>
                    <dd><Value value={a?.excessKurtosis}/><small>Undefined at zero variance; requires moment 4.</small>
                    </dd>
                </div>
                <div>
                    <dt>Hit frequency · P(payout &gt; 0)</dt>
                    <dd>{output.hitFrequency != null ? formatNumber(output.hitFrequency, 6) : 'Unavailable'}</dd>
                </div>
            </dl>
            <details>
                <summary>Exact raw and central moments</summary>
                <dl className="population-metrics">{moments.map(([label, value]) => <div key={label}>
                    <dt>{label}</dt>
                    <dd><Value value={value}/></dd>
                </div>)}</dl>
            </details>
            <RiskTables analysis={a}/>
            {a?.status === 'ExactMoments' &&
                <p className="population-note">Tail quantiles, expected shortfall and maximum payout are unavailable
                    without a complete law.</p>}
            {a?.planning && <div className="population-planning"><h4>Fixed-sample RTP planning</h4>
                <p>Half-width {a.planning.rtpHalfWidth} at stake {a.planning.stake}. Normal 95%
                    approximation: <strong>{a.planning.normal95Rounds}</strong> rounds. Conservative Chebyshev 95%
                    bound: <strong>{a.planning.chebyshev95Rounds}</strong> rounds.</p><p
                    className="population-note">{a.planning.assumptions}</p></div>}
            {a?.independentRounds && <Exposure value={a.independentRounds}/>}
        </>}
        {output.solver && <details>
            <summary>Solver diagnostics · {output.solver.plan}</summary>
            <p>Total work, including failed attempts: {output.solver.work.toLocaleString()}. Transform
                convolutions: {output.solver.convolution?.transformConvolutions ?? 0}.</p>
            <div className="results-table-scroll">
                <table className="results-table">
                    <thead>
                    <tr>
                        <th>Loop mode</th>
                        <th>Result</th>
                        <th>Work</th>
                        <th>Time · ms</th>
                        <th>Law requested / required / attempted</th>
                    </tr>
                    </thead>
                    <tbody>{output.solver.attempts.map((attempt, index) => <tr key={index}>
                        <td>{attempt.loopMode}</td>
                        <td>{attempt.succeeded ? 'Succeeded' : attempt.declinedReason}</td>
                        <td>{attempt.work.toLocaleString()}</td>
                        <td>{formatNumber(attempt.elapsedMs, 2)}</td>
                        <td>{[attempt.payoutLawRequested, attempt.payoutLawNeededForSettlement, attempt.payoutLawAttempted].map(value => value ? 'yes' : 'no').join(' / ')}</td>
                    </tr>)}</tbody>
                </table>
            </div>
            <p className="population-note">Logical owned objects and buffer slots, not bytes or total heap usage.
                Category peaks occur at different times and cannot be added. A zero current count can mean working
                scopes ended while the returned result remains alive.</p>
            <div className="results-table-scroll">
                <table className="results-table">
                    <thead>
                    <tr>
                        <th>Storage category</th>
                        <th>Current count</th>
                        <th>Peak count</th>
                    </tr>
                    </thead>
                    <tbody>{output.solver.storage?.map(counter => <tr key={counter.kind}>
                        <td>{counter.kind}</td>
                        <td>{counter.current.toLocaleString()}</td>
                        <td>{counter.peak.toLocaleString()}</td>
                    </tr>)}</tbody>
                </table>
            </div>
        </details>}
        {diagnostics && !output.solver &&
            <p className="population-note">Structured solver diagnostics are unavailable for this execution path.</p>}
        <p className="population-fingerprint">Pinned graph SHA-256 <code>{output.configHash}</code></p>
    </div>;
}

function RiskTables({analysis: a}: { analysis?: Analysis | null }) {
    return <>{!!a?.tails?.length && <><h4>Single-round upper-tail risk · credits</h4><p className="population-note">Loss
        = payout − stake. Quantiles use the left inverse CDF; expected shortfall uses exactly the upper 1 − α mass,
        splitting a tied atom.</p>
        <div className="results-table-scroll">
            <table className="results-table">
                <thead>
                <tr>
                    <th>Level α</th>
                    <th>Payout quantile</th>
                    <th>Payout ES</th>
                    <th>Operator loss VaR</th>
                    <th>Operator loss ES</th>
                </tr>
                </thead>
                <tbody>{a.tails.map(tail => <tr key={tail.probability}>
                    <th>{tail.probability}</th>
                    <td><Value value={tail.payoutQuantile}/></td>
                    <td><Value value={tail.payoutExpectedShortfall}/></td>
                    <td><Value value={tail.operatorLossQuantile}/></td>
                    <td><Value value={tail.operatorLossExpectedShortfall}/></td>
                </tr>)}</tbody>
            </table>
        </div>
    </>}
        {!!a?.thresholds?.length && <div className="results-table-scroll">
            <table className="results-table">
                <thead>
                <tr>
                    <th>Payout x · credits</th>
                    <th>P(X ≤ x)</th>
                    <th>P(X ≥ x)</th>
                </tr>
                </thead>
                <tbody>{a.thresholds.map(point => <tr key={point.threshold}>
                    <th>{point.threshold}</th>
                    <td><Value value={point.cdf}/></td>
                    <td><Value value={point.survivalInclusive}/></td>
                </tr>)}</tbody>
            </table>
        </div>}
    </>;
}

function Exposure({value}: { value: components['schemas']['IndependentRoundExposureOutput'] }) {
    return <section className="population-exposure" aria-label="Independent-round operator exposure"><h4>Operator
        exposure · {value.rounds?.toLocaleString()} independent rounds</h4><p
        className="population-note">{value.assumptions}</p>{value.reason && <p role="status">{value.reason}</p>}
        <dl className="population-metrics">
            <div>
                <dt>Mean final loss · credits</dt>
                <dd><Value value={value.meanLoss}/></dd>
            </div>
            <div>
                <dt>Final loss variance · credits²</dt>
                <dd><Value value={value.lossVariance}/></dd>
            </div>
            <div>
                <dt>P(final loss &gt; reserve)</dt>
                <dd><Value value={value.probabilityFinalLossExceedsReserve}/></dd>
            </div>
            <div>
                <dt>P(first-passage ruin by horizon)</dt>
                <dd><Value value={value.probabilityRuinByHorizon}/></dd>
            </div>
            <div>
                <dt>Expected ruin round · conditional on ruin</dt>
                <dd><Value value={value.conditionalExpectedRuinRound}/></dd>
            </div>
        </dl>
        <p className="population-note">Reserve: {value.initialReserve ?? 'not declared'}. Ruin
            analysis: {value.ruinStatus ?? value.status}. {value.ruinReason}</p>{!!value.tails?.length &&
            <div className="results-table-scroll">
                <table className="results-table">
                    <thead>
                    <tr>
                        <th>Level α</th>
                        <th>Final operator loss VaR · credits</th>
                        <th>Final operator loss ES · credits</th>
                    </tr>
                    </thead>
                    <tbody>{value.tails.map(tail => <tr key={tail.probability}>
                        <th>{tail.probability}</th>
                        <td><Value value={tail.lossQuantile}/></td>
                        <td><Value value={tail.lossExpectedShortfall}/></td>
                    </tr>)}</tbody>
                </table>
            </div>}</section>;
}
