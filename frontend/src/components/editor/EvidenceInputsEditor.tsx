import { useEffect, useRef, useState } from 'react';
import { useAppStore } from '../../store';
export interface EvidenceInput { kind: 'rule' | 'specification' | 'asset'; id: string; version: string; sha256: string; uri?: string | null; contentBase64?: string | null }

export function EvidenceInputsEditor() {
  const tables = useAppStore(s => s.tables), [error, setError] = useState('');
  const inputs = tables.evidenceInputs as EvidenceInput[] | undefined ?? [];
  const alive = useRef(true), jobs = useRef(new WeakMap<EvidenceInput, symbol>());
  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; jobs.current = new WeakMap(); };
  }, []);
  const change = (values: EvidenceInput[]) => useAppStore.setState(s => ({ tables: { ...s.tables, evidenceInputs: values.length ? values : undefined } }));
  const patch = (i: number, value: Partial<EvidenceInput>) => change(inputs.map((x, j) => i === j ? { ...x, ...value } : x));
  const embed = async (file: File, input: EvidenceInput, index: number) => {
    const origin = useAppStore.getState(), token = Symbol();
    jobs.current.set(input, token);
    const current = () => {
      const state = useAppStore.getState();
      return alive.current && jobs.current.get(input) === token && state.projectId === origin.projectId
        && state.graphTrail === origin.graphTrail && (state.tables.evidenceInputs as EvidenceInput[] | undefined)?.[index] === input;
    };
    setError('');
    try {
      if (file.size > 65536) throw new Error('Embedded evidence is limited to 64 KiB per file and 128 KiB in total. Larger files can use an external digest and version.');
      const bytes = await file.arrayBuffer();
      const sha256 = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)), b => b.toString(16).padStart(2, '0')).join('');
      if (!current()) return;
      const retainedInputs = useAppStore.getState().tables.evidenceInputs as EvidenceInput[];
      const otherBytes = retainedInputs.reduce((total, item, i) => total + (i !== index && item.contentBase64 ? atob(item.contentBase64).length : 0), 0);
      if (otherBytes + bytes.byteLength > 131072) throw new Error('Embedded evidence exceeds 128 KiB in total. Remove an embedded file or use an external digest and version.');
      let binary = ''; for (const byte of new Uint8Array(bytes)) binary += String.fromCharCode(byte);
      useAppStore.setState(state => {
        if (!current()) return state;
        const retained = state.tables.evidenceInputs as EvidenceInput[];
        return { tables: { ...state.tables, evidenceInputs: retained.map((item, i) => i === index ? { ...item, sha256, contentBase64: btoa(binary) } : item) } };
      });
    } catch (failure) { if (current()) setError(failure instanceof Error ? failure.message : 'File hashing failed.'); }
  };
  return <section aria-label="External evidence inputs">
    <p>Pin rule, specification and asset versions to the graph. Embedded bytes are checked against SHA-256 by the compiler and retained with the graph. URI-only identities are authored assertions; the server never downloads them.</p>
    {inputs.map((input, i) => <fieldset key={i}><legend>Input {i + 1}</legend>
      <div className="measurement-form-grid">
        <label>Kind<select value={input.kind} onChange={event => patch(i, { kind: event.target.value as EvidenceInput['kind'] })}>{['rule', 'specification', 'asset'].map(kind => <option key={kind}>{kind}</option>)}</select></label>
        <label>ID<input value={input.id} maxLength={128} onChange={event => patch(i, { id: event.target.value })} /></label>
        <label>Version<input value={input.version} maxLength={128} onChange={event => patch(i, { version: event.target.value })} /></label>
        <label>SHA-256<input value={input.sha256} maxLength={64} onChange={event => patch(i, { sha256: event.target.value, contentBase64: null })} /></label>
        <label>Source URI<input value={input.uri ?? ''} maxLength={2048} onChange={event => patch(i, { uri: event.target.value })} /></label>
        <label>Hash and embed file · up to 64 KiB<input type="file" onChange={event => { const file = event.target.files?.[0]; if (file) void embed(file, input, i); event.target.value = ''; }} /></label>
      </div>
      <small>{input.contentBase64 ? 'Embedded content · compiler verifies its digest' : 'External identity · content not retained'}</small>
      <button type="button" className="btn" aria-label={`Remove evidence input ${input.id || i + 1}`} onClick={() => {
        if (window.confirm(`Remove evidence input ${input.id || i + 1}? Its pinned identity and embedded content will be removed from this model.`)) change(inputs.filter((_, j) => j !== i));
      }}>Remove input</button>
    </fieldset>)}
    <button type="button" className="btn" disabled={inputs.length >= 64} onClick={() => change([...inputs, { kind: 'specification', id: '', version: '', sha256: '' }])}>Add evidence input</button>
    {error && <p role="alert">{error}</p>}
  </section>;
}
