import { parseModelJson } from '../lib/modelJson';
import { useEffect, useRef, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useAppStore, withoutSelection, type GraphNode, type GraphEdge, type CustomMechanic, retainCurrentProject } from '../store';
import { rootProject, switchProject } from '../lib/projectFiles';
import { buildConfigPayload } from '../lib/configPayload';
import { createNineLivesGraph } from '../games/nine-lives/graph';

/** Initialize all workspace routes from one canonical draft; preserve model-switch recovery. */
export function useProjectInitialization() {
  const [params, setParams] = useSearchParams();
  const encoded = params.get('load');
  const decoded = useMemo(() => {
    if (!encoded) return { config: null, error: null };
    try { return { config: parseModelJson(decodeURIComponent(atob(encoded))), error: null }; }
    catch (error) { return { config: null, error: error instanceof Error ? error.message : 'Unable to import this model link.' }; }
  }, [encoded]);
  const handled = useRef<string | null>(null);
  useEffect(() => {
    const state = useAppStore.getState();
    const token = encoded ? `load:${encoded}` : params.get('project');
    if (token && handled.current === token) return;
    handled.current = token;
    if (encoded) {
      try {
        const config = decoded.config;
        if (decoded.error) throw new Error(decoded.error);
        if (!config || !Array.isArray(config.nodes)) throw new Error('Missing graph nodes');
        retainCurrentProject();
        if (state.nodes.length && !state.authoringDraft) {
          const { nodes, edges, tables, graphTrail, verificationSource, configName, selectedNodeId, projectId } = state;
          useAppStore.setState({ authoringDraft: { nodes, edges, tables, graphTrail, verificationSource, configName, selectedNodeId, projectId } });
        }
        if (config.nodes[0]?.data) {
          const custom = (Array.isArray(config.mechanics) ? config.mechanics : []) as CustomMechanic[];
          const definitions = Object.fromEntries(custom.map(mechanic => [mechanic.id, { ...mechanic, ...buildConfigPayload(mechanic.nodes, mechanic.edges, { name: mechanic.name, tables: { ...mechanic } }) }]));
          const importedTables = config.tables && typeof config.tables === 'object' ? config.tables as Record<string, unknown> : {};
          const tables: Record<string, unknown> = { ...config, ...importedTables, mechanics: { ...(importedTables.mechanics as Record<string, unknown> ?? {}), ...definitions } };
          for (const key of ['nodes', 'edges', 'tables', 'name']) delete tables[key];
          useAppStore.setState({ nodes: withoutSelection(config.nodes as GraphNode[]) ?? [], edges: (config.edges ?? []) as GraphEdge[], mechanics: custom, tables, configName: typeof config.name === 'string' ? config.name : 'Imported model', graphTrail: [], projectId: crypto.randomUUID(), selectedNodeId: null, inspectorOpen: state.inspectorPinned, liveRtp: null, liveProvenance: null });
        } else { switchProject(config); }
        setParams(previous => { const next = new URLSearchParams(previous); next.delete('load'); return next; }, { replace: true });
      } catch { /* Rejected links leave the current project unchanged. */ }
    } else if (params.get('project') === 'nine-lives') {
      const root = state.graphTrail[0]?.graph ?? state.tables;
      if (['nine-lives-base', 'nine-lives-buy'].includes(String(root.id))) rootProject();
      else {
        if (state.nodes.length && !state.authoringDraft) {
          const { nodes, edges, tables, graphTrail, verificationSource, configName, selectedNodeId, projectId } = state;
          useAppStore.setState({ authoringDraft: { nodes, edges, tables, graphTrail, verificationSource, configName, selectedNodeId, projectId } });
        }
        switchProject(createNineLivesGraph());
        useAppStore.setState({ graphTrail: [], verificationSource: null });
      }
      setParams(previous => { const next = new URLSearchParams(previous); next.delete('project'); return next; }, { replace: true });
    }
  }, [params, setParams, encoded, decoded]);
  return decoded.error;
}
