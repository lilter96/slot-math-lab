import { useEffect, useRef, useState } from 'react';
import { useModelProjection } from '../../hooks/useModelProjection';
import { canonicalJson } from '../../lib/hash';
import { graphRequest } from '../../lib/graphApi';
import { useAppStore } from '../../store';
import { openMechanic, rootProject } from '../../lib/projectFiles';
import { validationSource, type ValidationSource } from '../../lib/validationSource';
import type { ModelGraph } from '../../lib/modelProjection';

interface Validation { isValid: boolean; errors: { code: string; message: string; nodeId?: string; edgeId?: string }[] }

function ValidationPanel({ graph }: { graph: ModelGraph | null }) {
  const pending = useRef<AbortController | null>(null);
  const [busy, setBusy] = useState(false), [error, setError] = useState('');
  const [result, setResult] = useState<Validation | null>(null);
  useEffect(() => () => pending.current?.abort(), []);
  const focus = (source: ValidationSource, kind: 'node' | 'edge') => {
    rootProject();
    for (const mechanic of source.mechanics) openMechanic(mechanic);
    const state = useAppStore.getState();
    if (kind === 'node') state.selectNode(source.id);
    else {
      state.selectNode(null);
      useAppStore.setState({ edges: state.edges.map(edge => ({ ...edge, selected: edge.id === source.id })) });
    }
    window.dispatchEvent(new CustomEvent(`slotmath:focus-${kind}`, { detail: source.id }));
  };
  return <div className="graph-validation"><button className="btn" disabled={busy || !graph} onClick={async () => {
    const controller = new AbortController(); pending.current = controller;
    setBusy(true); setError(''); setResult(null);
    try {
      const checked = await graphRequest<Validation>('validate', { config: graph }, controller.signal);
      if (!Array.isArray(checked.errors) || typeof checked.isValid !== 'boolean') throw new Error('No compiler validation result returned. Retry when the API is available.');
      if (!controller.signal.aborted) setResult(checked);
    } catch (reason) {
      if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : String(reason));
    } finally { if (!controller.signal.aborted) setBusy(false); }
  }}>{busy ? 'Validating…' : 'Validate model'}</button>{error && <span role="alert">{error}</span>}{result?.isValid && <span role="status">Model passes compiler validation. This does not prove RTP.</span>}{result && !result.isValid && <details open><summary>{result.errors.length} compiler errors</summary><ul>{result.errors.map((item, index) => {
    const node = graph && item.nodeId ? validationSource(graph, item.nodeId, 'nodes') : null;
    const edge = graph && item.edgeId ? validationSource(graph, item.edgeId, 'edges') : null;
    return <li key={index}><span>{item.code}: {item.message}</span>
      {node && <button className="btn" onClick={() => focus(node, 'node')}>Focus {item.nodeId}</button>}
      {edge && <button className="btn" onClick={() => focus(edge, 'edge')}>Focus edge {item.edgeId}</button>}
      {!node && !edge && <small>{item.nodeId || item.edgeId ? `Source ${item.nodeId ?? item.edgeId}; no unambiguous authored location is available.` : 'No node location returned.'}</small>}
    </li>;
  })}</ul></details>}</div>;
}

export default function GraphValidation() {
  const graph = useModelProjection();
  const projectId = useAppStore(state => state.projectId);
  return <ValidationPanel key={`${projectId}:${canonicalJson(graph)}`} graph={graph} />;
}
