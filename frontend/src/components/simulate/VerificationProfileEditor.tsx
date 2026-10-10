import { useEffect, useRef, useState } from 'react';
import { useMeasurementWorkspace } from '../../lib/measurements/store';
import { allowedProfileChecks, allocateProfile, validateProfile, checkLabels, type VerificationCriterion, type VerificationProfile } from '../../lib/measurements/profile';
import { definition } from '../../lib/measurements/model';
import { calculationRequest } from '../../lib/measurements/requests';
import { useAppStore } from '../../store';
import { peekRootProject } from '../../lib/projectFiles';
import { createDogHouseGraph } from '../../games/doghouse/graph';

export function VerificationProfileEditor({ close }: { close(): void }) {
  const dialog = useRef<HTMLDialogElement>(null), controller = useRef<AbortController | null>(null);
  const workspace = useMeasurementWorkspace(); const metrics = workspace.metrics.map(definition);
  const [name, setName] = useState(workspace.verificationProfile?.name ?? 'Required mathematical checks'), [confidence, setConfidence] = useState(workspace.verificationProfile?.familyConfidence ?? .95);
  const [criteria, setCriteria] = useState<VerificationCriterion[]>(workspace.verificationProfile?.criteria ?? (metrics[0] ? [{ measurementId: metrics[0].id, check: 'observation-integrity', minimumCount: 1 }] : []));
  const [allocate, setAllocate] = useState(true), [busy, setBusy] = useState(false), [error, setError] = useState(''), [status, setStatus] = useState('');
  const [config] = useState(() => useAppStore.getState().verificationSource ?? (useAppStore.getState().nodes.length ? peekRootProject() : createDogHouseGraph()));
  useEffect(() => { const el = dialog.current!; el.showModal(); return () => { controller.current?.abort(); el.close(); }; }, []);
  const dismiss = () => { controller.current?.abort(); close(); };
  const update = (i: number, patch: Partial<VerificationCriterion>) => setCriteria(c => c.map((v, j) => j === i ? { ...v, ...patch } : v));
  const statistical = criteria.filter(c => c.check === 'mean-equivalence').length;
  return <dialog ref={dialog} className="measurement-dialog" aria-labelledby="verification-editor-title" onCancel={dismiss} onClose={dismiss}><form onSubmit={async e => {
    e.preventDefault(); const before = useMeasurementWorkspace.getState();
    try {
      const profile: VerificationProfile = { name: name.trim(), familyConfidence: confidence, criteria };
      const proposed = allocate ? allocateProfile(before.metrics, profile) : before.metrics;
      validateProfile(profile, proposed.map(definition)); setBusy(true); setError('');
      const attempt = new AbortController(); controller.current = attempt;
      await calculationRequest('/api/runs/measurements/schema', { config, measurements: proposed.map(definition), verificationProfile: profile }, attempt.signal, setStatus, 10000);
      attempt.signal.throwIfAborted();
      const current = useMeasurementWorkspace.getState();
      if (current.metrics !== before.metrics || current.verificationProfile !== before.verificationProfile) throw new Error('The plan changed during validation. Review it before saving again.');
      useMeasurementWorkspace.setState({ metrics: proposed, verificationProfile: profile }); close();
    } catch (err) { if (!controller.current?.signal.aborted) { setError(err instanceof Error ? err.message : 'Invalid verification profile.'); setBusy(false); } }
  }}><header><div><span className="sim-eyebrow">PREDECLARE / NEXT RUN</span><h2 id="verification-editor-title">Verification profile</h2></div><button type="button" className="btn" aria-label="Close verification editor" onClick={dismiss}>✕</button></header>
    <p>Select the checks that must hold before launch. Each measurement keeps its authored observation point, filter, subject, reference and tolerance. A feature type can be its own filtered measurement. Mean checks use the overall matching population of that definition; cohort discovery cannot add post hoc criteria.</p>
    <fieldset className="measurement-form-grid" disabled={busy}><label>Profile name<input aria-label="Verification profile name" value={name} maxLength={80} required onChange={e => setName(e.target.value)} /></label><label>Family confidence<input aria-label="Verification family confidence" type="number" min="0.500001" max="0.999998" step="any" value={confidence} onChange={e => setConfidence(Number(e.target.value))} required /></label>
      <div className="measurement-wide">{criteria.map((c, i) => {
        const metric = metrics.find(m => m.id === c.measurementId); const choices = metric ? allowedProfileChecks(metric) : [];
        return <fieldset key={i} className="verification-criterion"><legend>Required check {i + 1}</legend><label>Measurement<select aria-label={`Required measurement ${i + 1}`} value={c.measurementId} onChange={e => update(i, { measurementId: e.target.value, check: 'observation-integrity' })}><option value="">Choose a measurement…</option>{metrics.map(m => <option value={m.id} key={m.id}>{m.name}</option>)}</select></label><label>Check<select aria-label={`Required check ${i + 1}`} value={c.check} onChange={e => update(i, { check: e.target.value as VerificationCriterion['check'] })}>{choices.map(check => <option key={check} value={check}>{checkLabels[check]}</option>)}</select></label><label>Minimum matching observations<input aria-label={`Required minimum ${i + 1}`} type="number" min="1" max="10000000" step="1" value={c.minimumCount} required onChange={e => update(i, { minimumCount: Number(e.target.value) })} /></label>
          {c.check === 'mean-equivalence' && <p>Reference {metric?.options?.referenceMean} ± {metric?.options?.tolerance} · {metric?.options?.referenceStatistic}. Configure these values and independence assumptions in the measurement editor.</p>}<button type="button" className="btn" onClick={() => setCriteria(c => c.filter((_, j) => i !== j))}>Remove required check {i + 1}</button></fieldset>;
      })}<button type="button" className="btn" disabled={criteria.length >= 32 || !metrics.length} onClick={() => setCriteria(c => [...c, { measurementId: metrics[0].id, check: 'observation-integrity', minimumCount: 1 }])}>Add required check</button></div>
      <label className="measurement-checkbox measurement-wide"><input aria-label="Allocate verification family budget" type="checkbox" checked={allocate} onChange={e => setAllocate(e.target.checked)} />Allocate the shared error budget to selected mean measurements</label>
      <p className="measurement-wide">{statistical} statistical checks share error probability ≤ {(1 - confidence).toPrecision(4)}. Allocation raises each selected metric's confidence or family size as needed and validates the complete updated collection plan atomically. It changes inference settings, while the game's authored math and random stream remain defined by the graph. Logical checks describe observed cases and use no statistical error allowance. Every interval keeps its own assumptions.</p>
      {!metrics.length && <p className="measurement-wide">Configure measurements first. References and tolerances belong to the authored measurement contract.</p>}
    </fieldset>{busy && <p role="status">Validating profile and complete plan · {status}</p>}{error && <p className="measurement-error" role="alert">{error}</p>}<footer><button type="button" className="btn" onClick={dismiss}>Cancel</button><button className="btn primary" type="submit" disabled={busy || !criteria.length}>{busy ? 'Validating…' : 'Save verification profile'}</button></footer>
  </form></dialog>;
}
