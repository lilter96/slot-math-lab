import { useMemo } from 'react';
import { useAppStore } from '../store';
import { inspectRootProject } from '../lib/projectFiles';
import type { ModelGraph } from '../lib/modelProjection';
export function useModelSnapshot() {
  const nodes = useAppStore(s => s.nodes);
  const edges = useAppStore(s => s.edges);
  const tables = useAppStore(s => s.tables);
  const trail = useAppStore(s => s.graphTrail);
  const name = useAppStore(s => s.configName);
  return useMemo(() => {
    void nodes; void edges; void tables; void trail; void name;
    return inspectRootProject();
  }, [nodes, edges, tables, trail, name]);
}

export function useModelProjection() { return useModelSnapshot().config as ModelGraph | null; }
