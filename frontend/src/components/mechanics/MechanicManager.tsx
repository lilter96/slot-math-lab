import { useModelProjection } from '../../hooks/useModelProjection';
import { useState } from 'react';
import { useAppStore } from '../../store';
import { openMechanic, reuseMechanic, rootProject } from '../../lib/projectFiles';

export default function MechanicManager() {
  // Subscribe to edits and navigation; the root projection retains unsaved nested changes.
  useAppStore(state => state.tables);
  useAppStore(state => state.nodes);
  useAppStore(state => state.graphTrail);
  const [search, setSearch] = useState('');
  const [error, setError] = useState<string | null>(null);
  const graph = useModelProjection();
  const definitions = (graph?.mechanics ?? {}) as Record<string, { nodes: unknown[]; description?: string }>;
  const act = (name: string, reuse: boolean) => {
    try {
      if (reuse) reuseMechanic(name);
      else { rootProject(); openMechanic(name); }
      setError(null);
    } catch (error) { setError(error instanceof Error ? error.message : String(error)); }
  };

  return <div data-testid="mechanic-manager">
    <div className="panel-h"><span className="t">Project subgraphs</span><span className="s">{Object.keys(definitions).length} definitions</span></div>
    <div className="panel-body">
      <p className="hint">Shift-select atoms on the canvas and use Extract selection above it. Expressions, connections and shared data stay in your model.</p>
      <label className="field">Find a definition<input className="inp" type="search" value={search} onChange={event => setSearch(event.target.value)} /></label>
      {error && <p role="alert">{error}</p>}
      {!Object.keys(definitions).length && <p className="hint">Open an example from Ready subgraphs, or extract your own atoms.</p>}
      {Object.entries(definitions).filter(([name, definition]) => `${name} ${definition.description ?? ''}`.toLowerCase().includes(search.toLowerCase())).map(([name, definition]) => <div key={name} className="field">
        <b>{name}</b>
        <p className="hint">{definition.description ?? `${definition.nodes.length} atoms`}</p>
        <div style={{ display: 'flex', gap: 8 }}>
          <button className="btn sm" onClick={() => act(name, false)} aria-label={`Edit definition ${name}`}>Open</button>
          <button className="btn primary sm" onClick={() => act(name, true)} aria-label={`Reuse definition ${name}`} title="Adds a reference to this shared definition; connect its ports on the canvas.">Use</button>
        </div>
      </div>)}
    </div>
  </div>;
}
