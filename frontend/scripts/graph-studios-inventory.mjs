import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { rolldown } from 'rolldown';
const frontend = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const bundle = await rolldown({ input: path.join(frontend, 'unit/fixtures/graphStudiosCorpus.ts'), logLevel: 'silent' });
try {
  const { output } = await bundle.generate({ format: 'esm' });
  const { graphStudiosCorpus } = await import('data:text/javascript;base64,' + Buffer.from(output[0].code).toString('base64'));
  const { graphs, nonGraphs } = graphStudiosCorpus();
  const rows = graphs.map(({ caseId, graph }) => {
    const nodes = [...graph.nodes, ...Object.values(graph.mechanics ?? {}).flatMap(scope => scope.nodes)];
    return { caseId, authoredId: graph.id ?? null, name: graph.name, geometry: graph.boardConfig ?? null, definitions: Object.keys(graph.mechanics ?? {}).length,
      nodes: nodes.length, draws: nodes.filter(node => node.nodeType === 'draw').length, loops: nodes.filter(node => node.nodeType === 'loop').length,
      paytables: (graph.paytables ?? []).map(table => table.id), initialStateKeys: Object.keys(graph.initialState ?? {}) };
  });
  const file = process.argv[2]; if (!file) throw new Error('Provide an inventory output path.');
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, JSON.stringify({ kind: 'structural-inventory-only', cases: rows, nonGraphs }, null, 2) + '\n');
  console.log(`${rows.length} executable-shape cases, ${nonGraphs.length} non-graph fixtures. No model or independent oracle was changed.`);
} finally { await bundle.close(); }
