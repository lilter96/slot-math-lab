import { useState } from 'react';
import { useAppStore } from '../../store';
import { exportProject, loadProject, openMechanic, reuseMechanic, rootProject } from '../../lib/projectFiles';
import { extractSubgraph, type ComposableGraph } from '../../lib/subgraphComposition';
import ProjectDataEditor from './ProjectDataEditor';

export default function ModelNavigation() {
  const tables = useAppStore(state => state.tables);
  const trail = useAppStore(state => state.graphTrail);
  const name = useAppStore(state => state.configName);
  const [showData, setShowData] = useState(false);
  const [subgraphName, setSubgraphName] = useState('');
  const [search, setSearch] = useState('');
  const [compositionError, setCompositionError] = useState<string | null>(null);
  const source = trail[0]?.graph;
  const mechanics = (source?.mechanics ?? tables.mechanics) as Record<string, { nodes: unknown[]; description?: string }> | undefined;
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
    <div className="model-navigation" data-testid="model-navigation">
      <div className="model-navigation-heading"><b>{String(source?.name ?? name ?? 'Untitled')}</b><span>{trail.length ? trail.map(item => item.mechanic).join(' / ') : `${Object.keys(mechanics ?? {}).length} editable subgraphs`}</span></div>
      <div className="model-navigation-actions" aria-label="Game model graphs">
        <button className="btn" aria-label="Open full game graph" onClick={() => rootProject()}>Full game</button>
        {trail.length > 0 && <button className="btn" title="Adds another reference to this definition. Connect the new node where it belongs in the full game." onClick={() => reuseMechanic(trail.at(-1)!.mechanic)}>Reuse this subgraph</button>}
        <button className="btn" aria-label="Open model data tables" onClick={() => { rootProject(); setShowData(true); }}>Data tables</button>
        <input className="inp" type="search" aria-label="Find subgraph" placeholder="Find a subgraph" value={search} onChange={event => setSearch(event.target.value)} />
      </div>
      <div className="model-navigation-actions model-navigation-list">
        {Object.entries(mechanics ?? {}).filter(([key, mechanic]) => `${key} ${mechanic.description ?? ''}`.toLowerCase().includes(search.toLowerCase())).map(([key, mechanic]) => <button key={key} className="btn" title={mechanic.description} aria-label={`Open ${key} graph`} onClick={() => { rootProject(); openMechanic(key); }}>{key} · {mechanic.nodes.length} nodes</button>)}
      </div>
      <div className="model-navigation-actions model-navigation-extract">
        <input className="inp" aria-label="Name for extraction" placeholder="Name for selected atoms" value={subgraphName} onChange={event => setSubgraphName(event.target.value)} />
        <button className="btn" onClick={extract} disabled={!subgraphName.trim()} title="Move selected atoms into one editable definition. Connections, expressions and data are retained.">Extract selection</button>
        <span className="hint">Shift-select atoms, name their subgraph, then reuse its definition.</span>
      </div>
      {compositionError && <p role="alert">{compositionError}</p>}
    </div>
    {showData && <ProjectDataEditor onClose={() => setShowData(false)} />}
  </>;
}
