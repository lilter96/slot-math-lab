import { useState } from 'react';
import { useAppStore } from '../../store';
export interface EvidenceInput { kind: 'rule' | 'specification' | 'asset'; id: string; version: string; sha256: string; uri?: string | null; contentBase64?: string | null }
export function EvidenceInputsEditor() {
  const tables = useAppStore(s => s.tables), [error, setError] = useState('');
  const inputs = tables.evidenceInputs as EvidenceInput[] | undefined ?? [];
  const change = (values: EvidenceInput[]) => useAppStore.setState(s => ({ tables: { ...s.tables, evidenceInputs: values.length ? values : undefined } }));
  const patch = (i: number, value: Partial<EvidenceInput>) => change(inputs.map((x, j) => i === j ? { ...x, ...value } : x));
  return <section aria-label="External evidence inputs"><p>Pin rule, specification and asset versions to the graph. Embedded bytes are checked against SHA-256 by the compiler and retained with the graph. URI-only identities are authored assertions; the server never downloads them.</p>{inputs.map((input, i) => <fieldset key={i}><legend>Input {i + 1}</legend><div className="measurement-form-grid"><label>Kind<select value={input.kind} onChange={e => patch(i, { kind: e.target.value as EvidenceInput['kind'] })}>{['rule', 'specification', 'asset'].map(kind => <option key={kind}>{kind}</option>)}</select></label><label>ID<input value={input.id} maxLength={128} onChange={e => patch(i, { id: e.target.value })} /></label><label>Version<input value={input.version} maxLength={128} onChange={e => patch(i, { version: e.target.value })} /></label><label>SHA-256<input value={input.sha256} maxLength={64} onChange={e => patch(i, { sha256: e.target.value, contentBase64: null })} /></label><label>Source URI<input value={input.uri ?? ''} maxLength={2048} onChange={e => patch(i, { uri: e.target.value })} /></label><label>Hash and embed file · up to 64 KiB<input type="file" onChange={async e => {
    const file = e.target.files?.[0]; if (!file) return; setError('');
    try {
      if (file.size > 65536) throw new Error('Embedded evidence is limited to 64 KiB per file and 128 KiB in total. Larger files can use an external digest and version.');
      const bytes = await file.arrayBuffer(); const sha256 = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)), b => b.toString(16).padStart(2, '0')).join('');
      let binary = ''; for (const b of new Uint8Array(bytes)) binary += String.fromCharCode(b);
      useAppStore.setState(s => { const current = s.tables.evidenceInputs as EvidenceInput[] ?? []; return { tables: { ...s.tables, evidenceInputs: current.map((x, j) => j === i ? { ...x, sha256, contentBase64: btoa(binary) } : x) } }; });
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'File hashing failed.'); }
  }} /></label></div><small>{input.contentBase64 ? 'Embedded content · compiler verifies its digest' : 'External identity · content not retained'}</small><button type="button" className="btn" onClick={() => change(inputs.filter((_, j) => j !== i))}>Remove input</button></fieldset>)}<button type="button" className="btn" disabled={inputs.length >= 64} onClick={() => change([...inputs, { kind: 'specification', id: '', version: '', sha256: '' }])}>Add evidence input</button>{error && <p role="alert">{error}</p>}</section>;
}
