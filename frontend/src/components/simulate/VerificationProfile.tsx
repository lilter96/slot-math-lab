import { useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import type { RunSnapshot } from '../../lib/realtime/runProtocol';
import type { ProfileEvidence, RetainedProfile } from '../../lib/measurements/profile';
import { calculationRequest } from '../../lib/measurements/requests';
import { useRunEvidence } from '../../lib/results/api';
import { downloadReport } from '../../lib/results/export';
import { checkLabels } from '../../lib/measurements/profile';
import { matchesEvidenceSource } from '../../lib/results/evidence';

const labels: Record<string, string> = { criteriaMet: 'Declared criteria met', withinPrecision: 'Within declared precision', noObservedViolations: 'No observed violations', discrepancy: 'Required check failed', invalid: 'Evidence invalid', insufficient: 'Insufficient evidence' };
export function VerificationProfile({ run }: { run: RunSnapshot }) {
  const [result, setResult] = useState<RetainedProfile | null>(null), [busy, setBusy] = useState(false), [error, setError] = useState(''), [status, setStatus] = useState('');
  const controller = useRef<AbortController | null>(null); useEffect(() => () => controller.current?.abort(), [run.id]);
  const archive = useRunEvidence(run.verificationProfile && run.status === 'completed' ? run.id : undefined), client = useQueryClient();
  const saved = archive.data?.diagnostics?.filter(d => d.kind === 'verification-profile').at(-1);
  const output = result?.report ?? saved?.output as ProfileEvidence | undefined;
  const profile = run.verificationProfile; if (!profile) return null;
  const verified = output?.runId === run.id && matchesEvidenceSource(output.source, run) && output.report?.profileHash === run.verificationProfileHash;
  return <details open className="simulation-card verification-profile" id="verification-profile"><summary>Predeclared verification · {profile.name}</summary><p>{profile.criteria.length} required checks · family confidence {100 * profile.familyConfidence}% · completed evidence only. Editing the workspace changes the next launch. Logical checks cover observed cases; the profile does not certify the exact population law.</p>
    <div className="results-table-scroll" tabIndex={0} aria-label="Predeclared required checks"><table className="results-table"><thead><tr><th>Measurement</th><th>Required check</th><th>Matching count required</th></tr></thead><tbody>{profile.criteria.map(c => <tr key={`${c.measurementId}-${c.check}`}><th scope="row">{run.measurements?.find(m => m.id === c.measurementId)?.name ?? c.measurementId}</th><td>{checkLabels[c.check]}</td><td>{c.minimumCount.toLocaleString()}</td></tr>)}</tbody></table></div>
    <button type="button" className="btn" disabled={busy || run.status !== 'completed'} onClick={async () => {
      const attempt = new AbortController(); controller.current?.abort(); controller.current = attempt; setBusy(true); setError('');
      try {
        const body = await calculationRequest<RetainedProfile>(`/api/runs/${encodeURIComponent(run.id)}/verification`, {}, attempt.signal, setStatus);
        attempt.signal.throwIfAborted();
        if (body.runId !== run.id || body.report?.runId !== run.id || body.report.report?.profileHash !== run.verificationProfileHash
          || !matchesEvidenceSource(body.report.source, run) || !labels[body.report.report?.status]) throw new Error('Verification evidence does not match the pinned completed run and its producer.');
        setResult(body); void client.invalidateQueries({ queryKey: ['run-evidence', run.id] });
      } catch (err) { if (!attempt.signal.aborted) setError(err instanceof Error ? err.message : 'Unable to evaluate profile.'); }
      finally { if (controller.current === attempt) setBusy(false); }
    }}>{busy ? 'Evaluating…' : 'Evaluate pinned verification profile'}</button>
    {busy && <><p role="status">{status}</p><button type="button" className="btn" onClick={() => { controller.current?.abort(); setBusy(false); }}>Cancel profile evaluation</button></>}{error && <p className="measurement-error" role="alert">{error}</p>}
    {output && !verified && <p className="measurement-error" role="alert">Retained verification identity differs from the pinned run. Recalculate the profile.</p>}
    {output && verified && <section aria-label="Verification profile result" data-profile-status={output.report.status}><h3>{labels[output.report.status]}</h3><p>Allocated statistical error probability {output.report.allocatedAlpha.toPrecision(5)} · {output.report.algorithmVersion}</p><ul className="verification-decisions">{output.report.criteria.map(c => <li key={`${c.measurementId}-${c.check}`}><strong>{c.name} · {checkLabels[c.check]}</strong><span>{labels[c.status]} · {c.count.toLocaleString()} / {c.minimumCount.toLocaleString()} required observations</span><p>{c.detail}</p>{c.interval && <p>Interval [{c.interval.lower.toPrecision(7)}, {c.interval.upper.toPrecision(7)}] · {c.interval.method}<br />{c.interval.assumptions}</p>}</li>)}</ul><p>{output.report.detail}</p><p role="status">{result?.retention.note ?? 'Retained server profile restored from the saved run.'}</p><button className="btn" type="button" onClick={() => downloadReport(`run-${run.id}-verification-profile.json`, JSON.stringify(result ?? saved, null, 2), 'application/json')}>Export verification profile report</button></section>}
    <p className="measurement-fingerprint">Pinned profile SHA-256 <code>{run.verificationProfileHash}</code></p>
  </details>;
}
