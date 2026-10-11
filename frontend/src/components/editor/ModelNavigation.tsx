import { useState } from 'react';
import { useAppStore } from '../../store';
import { exportProject, loadProject, reuseMechanic, rootProject } from '../../lib/projectFiles';
import { extractSubgraph, type ComposableGraph } from '../../lib/subgraphComposition';
import ProjectDataEditor from './ProjectDataEditor';
import StarterLibrary from './StarterLibrary';
import MechanicTree from './MechanicTree';
import { closeMechanic } from '../../lib/projectFiles';
import { useModelProjection } from '../../hooks/useModelProjection';

export default function ModelNavigation() {
  const trail = useAppStore(state => state.graphTrail);
  const name = useAppStore(state => state.configName);
  const [showData, setShowData] = useState(false);
  const [subgraphName, setSubgraphName] = useState('');
  const [search, setSearch] = useState('');
  const [compositionError, setCompositionError] = useState<string | null>(null);
  const source = trail[0]?.graph;
  const graph = useModelProjection();
  const extract = () => {
    try {
      const current = useAppStore.getState();
      const ids = current.nodes.filter(node => node.selected).map(node => node.id);
      if (!ids.length && current.selectedNodeId) ids.push(current.selectedNodeId);
      const graph = exportProject();
      if (!graph) throw new Error('Add and select atoms first.');
      const updated = extractSubgraph(graph as ComposableGraph, subgraphName, ids, `subgraph-${crypto.randomUUID()}`);
      // Stay at the same nesting level. Full-game export saves this definition and its parent together.
      loadProject(updated);
      setSubgraphName('');
      setCompositionError(null);
    } catch (error) { setCompositionError(error instanceof Error ? error.message : String(error)); }
  };
  return <>
    <aside className="model-navigation mechanic-explorer" data-testid="model-navigation" aria-label="Mechanic explorer">
      <div className="model-navigation-heading"><b>{String(source?.name ?? name ?? 'Untitled')}</b><span>Mechanics</span></div>
      <div className="model-navigation-actions">
        <button className="btn" aria-label="Open full game graph" onClick={() => rootProject()}>Full game</button>
        <button className="btn" aria-label="Open model data tables" onClick={() => { rootProject(); setShowData(true); }}>Data tables</button>
      </div>
      {trail.length > 0 && <div className="mechanic-context"><p>{trail.map(item => item.mechanic).join(' / ')}</p><button className="btn" onClick={closeMechanic}>Save & return</button><button className="btn" title="Connect a new reference to the full game" onClick={() => reuseMechanic(trail.at(-1)!.mechanic)}>Reuse this subgraph</button></div>}
      <label className="mechanic-search">Find mechanic<input className="inp" type="search" aria-label="Find subgraph" placeholder="Name or description" value={search} onChange={event => setSearch(event.target.value)} /></label>
      {graph && <MechanicTree graph={graph} search={search} />}
      <StarterLibrary />
      <details className="extract-disclosure"><summary>Extract selected atoms</summary>
      <div className="model-navigation-actions model-navigation-extract">
        <input className="inp" aria-label="Name for extraction" placeholder="Name for selected atoms" value={subgraphName} onChange={event => setSubgraphName(event.target.value)} />
        <button className="btn" onClick={extract} disabled={!subgraphName.trim()} title="Move selected atoms into one editable definition. Connections, expressions and data are retained.">Extract selection</button>
        <span className="hint">Shift-select atoms, name their subgraph, then reuse its definition.</span>
      </div>
      </details>
      {compositionError && <p role="alert">{compositionError}</p>}
    </aside>
    {showData && <ProjectDataEditor onClose={() => setShowData(false)} />}
  </>;
}
