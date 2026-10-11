import { test, expect } from '@playwright/test';
import { createNineLivesGraph } from '../src/games/nine-lives/graph';

test('Nine Lives composes subgraphs from the existing atomic vocabulary', () => {
  const graph = createNineLivesGraph();
  const mechanics = graph.mechanics as Record<string, { nodes: { nodeType: string; id: string; outputKey?: string }[]; expressions: Record<string, unknown> }>;
  const allowed = new Set(['draw', 'modifyState', 'getState', 'branch', 'loop', 'library', 'metricsSink']);
  for (const mechanic of Object.values(mechanics))
    for (const node of mechanic.nodes) expect(allowed.has(node.nodeType)).toBe(true);
  expect(mechanics.bonus.nodes.find(node => node.outputKey === 'bonusCompleted')).toBeDefined();
  expect(mechanics['restore-row-major'].expressions.restore).toMatchObject({ exprType: 'map', indexName: 'index', body: { exprType: 'call', function: 'index' } });
  expect(JSON.stringify(graph)).not.toMatch(/"transformId"|"setIndex"|"setindex"|"plugin"/);
});
