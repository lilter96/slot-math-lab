import { useEffect } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useAppStore } from '../store';
import { loadProject, rootProject } from '../lib/projectFiles';
import { createNineLivesGraph } from '../games/nine-lives/graph';

/** Initialize all workspace routes from one canonical draft; preserve model-switch recovery. */
export function useProjectInitialization() {
  const [params, setParams] = useSearchParams();
  useEffect(() => {
    const state = useAppStore.getState();
    if (params.get('project') === 'nine-lives') {
      const root = state.graphTrail[0]?.graph ?? state.tables;
      if (String(root.id ?? '').startsWith('nine-lives')) rootProject();
      else {
        if (state.nodes.length && !state.authoringDraft) {
          const { nodes, edges, tables, graphTrail, verificationSource, configName, selectedNodeId } = state;
          useAppStore.setState({ authoringDraft: { nodes, edges, tables, graphTrail, verificationSource, configName, selectedNodeId } });
        }
        loadProject(createNineLivesGraph());
        useAppStore.setState({ graphTrail: [], verificationSource: null });
      }
      setParams(previous => { const next = new URLSearchParams(previous); next.delete('project'); return next; }, { replace: true });
    } else if (!state.nodes.length && !state.graphTrail.length) {
      const encoded = params.get('load');
      if (encoded) {
        try {
          const config = JSON.parse(decodeURIComponent(atob(encoded)));
          if (config.nodes?.[0]?.data) {
            for (const node of config.nodes) state.addNode({ ...node, position: node.position ?? { x: 100, y: 100 } });
          } else loadProject(config);
        } catch { /* Existing share-link recovery: a bad link leaves the editor available. */ }
      } else loadProject(createNineLivesGraph());
    }
  }, [params, setParams]);
}
