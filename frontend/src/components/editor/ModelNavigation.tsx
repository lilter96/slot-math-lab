import { useState } from 'react';
import { useAppStore } from '../../store';
import { openMechanic, rootProject } from '../../lib/projectFiles';
import ProjectDataEditor from './ProjectDataEditor';

export default function ModelNavigation() {
  const tables = useAppStore(state => state.tables);
  const trail = useAppStore(state => state.graphTrail);
  const name = useAppStore(state => state.configName);
  const [showData, setShowData] = useState(false);
  const source = trail[0]?.graph;
  const mechanics = (source?.mechanics ?? tables.mechanics) as Record<string, { nodes: unknown[]; description?: string }> | undefined;
  if (!mechanics || !Object.keys(mechanics).length) return null;
  return <>
    <div className="model-navigation" data-testid="model-navigation">
      <div className="model-navigation-heading"><b>{String(source?.name ?? name)}</b><span>{trail.length ? trail.map(item => item.mechanic).join(' / ') : `${Object.keys(mechanics).length} editable subgraphs`}</span></div>
      <div className="model-navigation-actions" aria-label="Game model graphs">
        <button className="btn" aria-label="Open full game graph" onClick={() => rootProject()}>Full game</button>
        {Object.entries(mechanics).map(([key, mechanic]) => <button key={key} className="btn" title={mechanic.description} aria-label={`Open ${key} graph`} onClick={() => { rootProject(); openMechanic(key); }}>{key} · {mechanic.nodes.length} nodes</button>)}
        <button className="btn" aria-label="Open model data tables" onClick={() => { rootProject(); setShowData(true); }}>Data tables</button>
      </div>
    </div>
    {showData && <ProjectDataEditor onClose={() => setShowData(false)} />}
  </>;
}
