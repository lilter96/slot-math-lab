import { useMemo } from 'react';
import { useAppStore } from '../store';
import { peekRootProject } from '../lib/projectFiles';
import type { ModelGraph } from '../lib/modelProjection';
export function useModelProjection() {
  const nodes = useAppStore(s => s.nodes);
  const edges = useAppStore(s => s.edges);
  const tables = useAppStore(s => s.tables);
  const trail = useAppStore(s => s.graphTrail);
  const name = useAppStore(s => s.configName);
  return useMemo(() => {
    void nodes; void edges; void tables; void trail; void name;
    return peekRootProject() as ModelGraph | null;
  }, [nodes, edges, tables, trail, name]);
}
