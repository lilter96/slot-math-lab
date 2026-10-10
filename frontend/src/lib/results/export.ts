import { compareMeasurements } from '../measurements/comparison';
import { reducers, reducerNames, statistic, formatStatistic } from '../measurements/model';
import { assess, compareRuns, inspectRun, interval, percent, type Reference, type RunEvidence } from './model';
export function evidenceBundle(evidence: RunEvidence, reference: Reference | undefined, tolerancePp: number, comparison?: RunEvidence) {
  if (reference?.sourceHash !== evidence.run.configHash) reference = undefined;
  const target = evidence.model.targetRtp ?? reference?.rtp ?? null;
  return { schemaVersion: 'slotmath.results.v1', exportedAt: new Date().toISOString(), evidence,
    reference: reference ?? null,
    assessment: { tolerancePp: Number.isFinite(tolerancePp) ? tolerancePp : null, targetRtp: target,
      targetSource: evidence.model.targetRtp != null ? 'Authored' : reference?.rtp != null ? 'CalculatedReference' : 'Unavailable',
      ...assess(evidence.run, target, tolerancePp, evidence.inputVerified),
      intervalMethod: 'Normal approximation, 1.96 × sample standard error; no coverage guarantee for finite or adaptively stopped samples.' },
    integrity: inspectRun(evidence.run), comparison: comparison ? { evidence: comparison, assessment: compareRuns(evidence, comparison), measurements: compareMeasurements(evidence.run, comparison.run) } : null };
}
function csvCell(value: unknown) {
  const text = value == null ? '' : String(value);
  // Spreadsheet formula protection also applies to user-authored model names.
  const safe = /^(?:\s*[=+@-]|[\t\r\n])/.test(text) && typeof value === 'string' ? "'" + text : text;
  return '"' + safe.replaceAll('"', '""') + '"';
}
export function reportCsv(evidence: RunEvidence, reference?: Reference) {
  if (reference?.sourceHash !== evidence.run.configHash) reference = undefined;
  const { run, model } = evidence, p = run.progress, ci = interval(p);
  return [['Metric', 'Value', 'Unit', 'Source'], ['Model', model.name, '', 'Pinned input'], ['Run ID', run.id, '', 'Server'],
    ['Status', run.status, '', 'Server'], ['Input verified', evidence.inputVerified, '', 'Server hash comparison'],
    ['Config hash', run.configHash, '', 'SHA-256'], ['Model hash', model.modelHash, '', 'SHA-256, document ID excluded'],
    ['Config version', run.configVersion, '', 'Pinned input'], ['Seed', run.seed, '', run.streamScheme],
    ['Requested rounds', p?.totalSamples, 'rounds', 'Server'], ['Observed rounds', p?.sampleCount, 'rounds', 'Sampled'],
    ['Observed RTP', p?.sampleCount ? p.runningRtp : null, 'stake ratio', 'Sampled'], ['RTP interval lower', ci?.[0], 'stake ratio', 'Normal approximation'],
    ['RTP interval upper', ci?.[1], 'stake ratio', 'Normal approximation'], ['Authored target', model.targetRtp, 'stake ratio', 'Pinned input; not a verified expectation'],
    ['Reference RTP', reference?.rtp, 'stake ratio', reference?.kind ?? 'Unavailable'], ['Reference rational', reference?.rational, '', reference?.kind ?? 'Unavailable'],
    ['Reference lower bound', reference?.lower, 'stake ratio', reference?.kind ?? 'Unavailable'], ['Reference upper bound', reference?.upper, 'stake ratio', reference?.kind ?? 'Unavailable'],
    ['Hit frequency', p?.sampleCount ? p.hitFrequency : null, 'ratio', 'Sampled'], ['Payout standard deviation', p && p.sampleCount > 1 ? p.volatility : null, 'stake multiples', 'Sampled'],
    ['Maximum observed', p?.sampleCount ? p.maxWin : null, 'stake multiples', 'Sampled; not theoretical maximum'], ['Declared win cap', model.winCap, 'stake multiples', 'Pinned input'],
    ['Win-cap reach count', p?.capHits, 'rounds', 'Settled payout >= declared cap; use raw-payout predicate for actual exceedance'],
    ['Core binary SHA-256', run.runtimeProvenance?.coreBinarySha256, '', 'Executing algorithm identity'],
    ['Execution regime', run.execution?.regime ?? 'independentRounds', '', 'Pinned execution population'],
    ...(p?.execution ? ['attemptedRounds', 'completedRounds', 'interruptedRounds', 'cancelledRounds', 'failedRounds', 'completedSessions', 'interruptedSessions'].map(key => [key, p.execution![key as 'completedRounds'], 'subjects', 'Runtime exposure ledger']) : []),
    ...(p?.execution?.loopTerminations ?? []).flatMap(l => Object.entries(l).map(([key, value]) => [`Loop ${l.nodeId} / ${key}`, value, key.endsWith('Iterations') ? 'iterations' : 'invocations', 'Completed rounds only; authored bounded model'])),
    ['Measurement hash', run.measurementHash, '', 'SHA-256 of pinned collection plan'],
    ['Verification profile hash', run.verificationProfileHash, '', 'SHA-256 of the declaration pinned before launch'],
    ...(run.measurements ?? []).flatMap(d => { const m = p?.measurements?.find(v => v.id === d.id); return [
      ...reducers.map(r => [`Measurement: ${d.name} / ${reducerNames[r]}`, statistic(m, r), r === 'count' ? 'observations' : r === 'matchRate' ? 'ratio' : d.unit, `Sampled; ${d.nodeId ?? 'completed paid round'}; average denominator = matching valid observations`]),
      [`Measurement: ${d.name} / Eligible`, m?.observations, 'observations', 'Sampled'], [`Measurement: ${d.name} / Excluded`, m?.excluded, 'observations', 'Filtered out'],
      [`Measurement: ${d.name} / Invalid`, m?.errors, 'observations', m?.firstError ?? 'No evaluation errors'],
      ...analysisRows(`Measurement: ${d.name}`, m?.analysis, d.unit),
      ...(m?.witnesses ?? []).map(w => [`Measurement: ${d.name} / Witness ${w.kind}`, JSON.stringify(w), 'round / observation coordinates', 'Pinned deterministic stream; bounded diagnostic examples'])]; }),
      ...(p?.execution?.sessionMetrics ?? []).flatMap(m => analysisRows(m.id, m.analysis, 'session units'))].map(row => row.map(csvCell).join(',')).join('\r\n');
}
function analysisRows(prefix: string, analysis: import('../measurements/analysis').MeasurementAnalysis | null | undefined, unit: string): unknown[][] {
  if (!analysis) return [];
  const result: unknown[][] = [];
  const flatten = (value: unknown, path: string) => {
    if (Array.isArray(value)) value.forEach((v, i) => flatten(v, `${path}[${i}]`));
    else if (value && typeof value === 'object') Object.entries(value).forEach(([key, v]) => flatten(v, `${path}.${key}`));
    else result.push([`${prefix} / ${path}`, value, typeof value === 'number' ? analysisUnit(path, unit) : '', 'Pinned measurement analysis; methods and assumptions are exported alongside each interval']);
  };
  flatten(analysis, 'analysis'); return result;
}
function analysisUnit(path: string, unit: string): string {
  const key = path.split('.').at(-1) ?? '';
  if (/^(count|completedRuns|observations|checked|violations|paidRounds|adjacentPairs|equalAdjacentPairs|entries|exits|distinctParents|uniqueAwards|duplicateAwards|unclosedEpisodes|fromExposure|completedGaps|events|degreesOfFreedom|unexpectedObservations|requiredSampleSize)$/.test(key)) return 'subjects';
  if (key === 'externalTurnover') return 'external monetary units';
  if (/^(meanLength|maximumLength|longestEventStreak|longestDrought|meanGap)$/.test(key)) return 'observations';
  if (/^(probability|quantile|tailMass|correlation|ratio|pValue|coefficientOfVariation|skewness|excessKurtosis|totalVariation|cdfDistance|lowerReturnShare|upperReturnShare|chiSquare)$/.test(key) || /probabilityInterval|ratioInterval|autocorrelations|weights/.test(path)) return 'ratio / dimensionless';
  if (/secondMoment|Variance|covariance|sumSquares/.test(path)) return unit ? `(${unit})²` : 'value squared';
  if (/^(from|to|state)$/.test(key)) return 'state code';
  return unit;
}
const escape = (value: unknown) => String(value ?? '').replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#39;');
export function reportHtml(evidence: RunEvidence, reference: Reference | undefined, tolerancePp: number) {
  if (reference?.sourceHash !== evidence.run.configHash) reference = undefined;
  const { run, model } = evidence, p = run.progress, ci = interval(p), assessment = assess(run, model.targetRtp ?? reference?.rtp ?? null, tolerancePp, evidence.inputVerified);
  const rows = [['Observed RTP', percent(p?.sampleCount ? p.runningRtp : null)], ['95% interval (normal approximation)', ci ? `${percent(ci[0])} – ${percent(ci[1])}` : 'Unavailable'],
    ['Authored target', percent(model.targetRtp)], ['Reference', reference?.rtp != null ? `${percent(reference.rtp)} · ${reference.kind}` : reference?.kind === 'ExactInterval' ? `${percent(reference.lower)} – ${percent(reference.upper)} · ExactInterval` : reference?.kind ?? 'Not calculated'],
    ['Observed / requested rounds', `${p?.sampleCount ?? 0} / ${p?.totalSamples ?? 0}`], ['Status', run.status], ['Seed', run.seed], ['Workers', run.degreeOfParallelism],
    ['Config version', run.configVersion], ['Config SHA-256', run.configHash], ['Model SHA-256', model.modelHash], ['PRNG stream', run.streamScheme],
    ['Input hash verified', evidence.inputVerified ? 'Yes' : 'No'], ['Tolerance (percentage points)', tolerancePp], ['Measurement SHA-256', run.measurementHash ?? 'None'],
    ...(run.measurements ?? []).flatMap(d => { const m = p?.measurements?.find(v => v.id === d.id); return reducers.map(r => [`${d.name} / ${reducerNames[r]}`, formatStatistic(statistic(m, r), r, d.unit)]); })];
  const tracked = (run.measurements ?? []).map(d => ({ name: d.name, unit: d.unit, snapshot: p?.measurements?.find(m => m.id === d.id) }));
  const sessions = (p?.execution?.sessionMetrics ?? []).map(m => ({ name: m.id, unit: 'session units', snapshot: m }));
  const analyses = [...tracked, ...sessions].map(({ name, unit, snapshot }) => `<section><h2>${escape(name)}</h2><p>${escape(snapshot?.analysis?.subject ?? 'observations')} · valid / eligible ${snapshot?.count ?? 0} / ${snapshot?.observations ?? 0} · excluded / invalid ${snapshot?.excluded ?? 0} / ${snapshot?.errors ?? 0}</p><table><thead><tr><th>Statistic</th><th>Value</th><th>Unit</th></tr></thead><tbody>${analysisRows(name, snapshot?.analysis, unit).map(([label, value, dimension]) => `<tr><td>${escape(label)}</td><td>${escape(value)}</td><td>${escape(dimension)}</td></tr>`).join('')}</tbody></table>${snapshot?.witnesses?.length ? `<h3>Reproducible witness coordinates</h3><pre>${escape(JSON.stringify(snapshot.witnesses, null, 2))}</pre>` : ''}</section>`).join('');
  return `<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Results — ${escape(model.name)}</title><style>body{font:15px/1.6 system-ui;color:#152329;max-width:900px;margin:auto;padding:40px}h1{font-size:28px}header{border-bottom:3px solid #178572}table{border-collapse:collapse;width:100%;margin:24px 0}td{padding:12px 6px;border-bottom:1px solid #dde2e5;overflow-wrap:anywhere}td:first-child{width:36%}small{color:#465a65}pre{white-space:pre-wrap;overflow-wrap:anywhere}th{text-align:left}.notice{padding:16px;background:#edf5f3}@media print{body{padding:12px}tr{break-inside:avoid}}</style><header><small>SLOT MATH LAB · SAVED RUN EVIDENCE · SAMPLED</small><h1>${escape(model.name)}</h1><p>Run ${escape(run.id)} · ${escape(run.createdAt)}</p></header><h2>${escape(assessment.title)}</h2><p>${escape(assessment.note)}</p><table>${rows.map(([name, value]) => `<tr><td>${escape(name)}</td><td>${escape(value)}</td></tr>`).join('')}</table><p>Execution regime ${escape(run.execution?.regime ?? 'independentRounds')} · Core SHA-256 ${escape(run.runtimeProvenance?.coreBinarySha256 ?? 'unavailable')} · API SHA-256 ${escape(run.runtimeProvenance?.apiBinarySha256 ?? 'unavailable')}</p><h2>Execution evidence</h2><pre>${escape(JSON.stringify(p?.execution ?? null, null, 2))}</pre>${analyses}<p class="notice">These sampling intervals do not certify the payout model. Maximum observed is not a theoretical maximum. Partial or interrupted runs are diagnostic evidence. ${escape(reference?.note ?? 'An exact reference has not been calculated.')}</p><p>Generated ${escape(new Date().toISOString())}. Use the JSON evidence bundle for the pinned graph, measurement definitions and complete histogram. This HTML includes the retained analysis, assumptions and bounded witness coordinates.</p></html>`;
}
export function downloadReport(filename: string, body: string, mime: string) {
  const url = URL.createObjectURL(new Blob([body], { type: mime })), link = document.createElement('a');
  link.href = url; link.download = filename; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
}
