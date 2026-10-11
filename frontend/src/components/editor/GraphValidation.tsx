import { useState } from 'react';
import { useModelProjection } from '../../hooks/useModelProjection';
import { canonicalJson } from '../../lib/hash';
import { graphRequest } from '../../lib/graphApi';
import { useAppStore } from '../../store';
interface Validation { isValid: boolean; errors: { code: string; message: string; nodeId?: string; edgeId?: string }[] }
export default function GraphValidation() {
  const graph = useModelProjection();
  const nodes = useAppStore(state => state.nodes);
  const [busy, setBusy] = useState(false), [error, setError] = useState('');
  const [result, setResult] = useState<{ graph: string; validation: Validation } | null>(null);
  const current = result?.graph === canonicalJson(graph) ? result.validation : null;
  return <div className="graph-validation"><button className="btn" disabled={busy || !graph} onClick={async () => {
    setBusy(true); setError(''); setResult(null);
    try {
      const checked = await graphRequest<Validation>('validate', { config: graph });
      if (!Array.isArray(checked.errors) || typeof checked.isValid !== 'boolean') throw new Error('No compiler validation result returned. Retry when the API is available.');
      setResult({ graph: canonicalJson(graph), validation: checked });
    } catch (reason) { setError(reason instanceof Error ? reason.message : String(reason)); } finally { setBusy(false); }
  }}>{busy ? 'Validating…' : 'Validate model'}</button>{error && <span role="alert">{error}</span>}{current?.isValid && <span role="status">Model passes compiler validation. This does not prove RTP.</span>}{current && !current.isValid && <details open><summary>{current.errors.length} compiler errors</summary><ul>{current.errors.map((item, index) => <li key={index}><span>{item.code}: {item.message}</span>{item.nodeId && nodes.some(node => node.id === item.nodeId) ? <button className="btn" onClick={() => { useAppStore.getState().selectNode(item.nodeId!); window.dispatchEvent(new CustomEvent('slotmath:focus-node', { detail: item.nodeId })); }}>Focus {item.nodeId}</button> : <small>{item.nodeId ? `Source ${item.nodeId}; open the matching mechanic.` : item.edgeId ? `Edge ${item.edgeId}` : 'No node location returned.'}</small>}</li>)}</ul></details>}</div>;
}
