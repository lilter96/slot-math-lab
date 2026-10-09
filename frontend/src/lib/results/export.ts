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
    integrity: inspectRun(evidence.run), comparison: comparison ? { evidence: comparison, assessment: compareRuns(evidence, comparison) } : null };
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
    ['Sampler clipping count', p?.capHits, 'rounds', 'Sampler only; upstream cap events are not instrumented'],
    ['Measurement hash', run.measurementHash, '', 'SHA-256 of pinned collection plan'],
    ...(run.measurements ?? []).flatMap(d => { const m = p?.measurements?.find(v => v.id === d.id); return [
      ...reducers.map(r => [`Measurement: ${d.name} / ${reducerNames[r]}`, statistic(m, r), r === 'count' ? 'observations' : r === 'matchRate' ? 'ratio' : d.unit, `Sampled; ${d.nodeId ?? 'completed paid round'}; average denominator = matching valid observations`]),
      [`Measurement: ${d.name} / Eligible`, m?.observations, 'observations', 'Sampled'], [`Measurement: ${d.name} / Excluded`, m?.excluded, 'observations', 'Filtered out'],
      [`Measurement: ${d.name} / Invalid`, m?.errors, 'observations', m?.firstError ?? 'No evaluation errors']]; })].map(row => row.map(csvCell).join(',')).join('\r\n');
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
  return `<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Results — ${escape(model.name)}</title><style>body{font:15px/1.6 system-ui;color:#152329;max-width:900px;margin:auto;padding:40px}h1{font-size:28px}header{border-bottom:3px solid #178572}table{border-collapse:collapse;width:100%;margin:24px 0}td{padding:12px 6px;border-bottom:1px solid #dde2e5;overflow-wrap:anywhere}td:first-child{width:36%}small{color:#465a65}.notice{padding:16px;background:#edf5f3}@media print{body{padding:12px}tr{break-inside:avoid}}</style><header><small>SLOT MATH LAB · SAVED RUN EVIDENCE · SAMPLED</small><h1>${escape(model.name)}</h1><p>Run ${escape(run.id)} · ${escape(run.createdAt)}</p></header><h2>${escape(assessment.title)}</h2><p>${escape(assessment.note)}</p><table>${rows.map(([name, value]) => `<tr><td>${escape(name)}</td><td>${escape(value)}</td></tr>`).join('')}</table><p class="notice">These sampling intervals do not certify the payout model. Maximum observed is not a theoretical maximum. Partial or interrupted runs are diagnostic evidence. ${escape(reference?.note ?? 'An exact reference has not been calculated.')}</p><p>Generated ${escape(new Date().toISOString())}. Use the JSON evidence bundle for the pinned graph and complete histogram.</p></html>`;
}
export function downloadReport(filename: string, body: string, mime: string) {
  const url = URL.createObjectURL(new Blob([body], { type: mime })), link = document.createElement('a');
  link.href = url; link.download = filename; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
}
