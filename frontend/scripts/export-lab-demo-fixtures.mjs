import fs from 'node:fs';
import path from 'node:path';
import ts from 'typescript';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const source = path.join(root, 'frontend/src/games/nine-lives');
const text = fs.readFileSync(path.join(source, 'graph.ts'), 'utf8').replace(
  "import model from './model.json' with { type: 'json' };",
  'const model = ' + fs.readFileSync(path.join(source, 'model.json'), 'utf8') + ';',
);
const module = ts.transpileModule(text, { compilerOptions: { module: ts.ModuleKind.ESNext } }).outputText;
const author = await import('data:text/javascript;base64,' + Buffer.from(module).toString('base64'));
const directory = path.join(root, 'backend/SlotMath.Core.Tests/TestData/LabDemo');
fs.mkdirSync(directory, { recursive: true });
const mini = structuredClone(author.createNineLivesGraph());
mini.id = 'lab-demo-mini';
mini.name = 'Nine Lives component enumeration';
mini.initialState.board = Array.from({ length: 30 }, (_, i) => i < 5 ? '1' : '7');
mini.nodes = [
  { nodeType: 'draw', id: 'choice', stateWriteKey: 'choice', inputs: { state: { name: 'state', type: 'State' } }, outputs: { state: { name: 'state', type: 'State' } }, drawWeights: [{ outcomeId: '1', weight: 1, value: 0 }, { outcomeId: '2', weight: 1, value: 0 }] },
  { nodeType: 'modifyState', id: 'board', outputKey: 'board', expressionId: 'board', inputs: { state: { name: 'state', type: 'State' } }, outputs: { state: { name: 'state', type: 'State' } } },
  { nodeType: 'library', id: 'evaluate', mechanicName: 'evaluate-components', inputs: { state: { name: 'state', type: 'State' } }, outputs: { state: { name: 'state', type: 'State' } } },
  { nodeType: 'modifyState', id: 'credits', outputKey: 'winCredits', expressionId: 'credits', inputs: { state: { name: 'state', type: 'State' } }, outputs: { state: { name: 'state', type: 'State' } } },
  { nodeType: 'metricsSink', id: 'round-metrics', winStateKey: 'winCredits', winCap: 9999, inputs: { state: { name: 'state', type: 'State' } }, outputs: {} },
];
mini.edges = mini.nodes.slice(1).map((node, i) => ({ id: 'mini-' + i, sourceNodeId: mini.nodes[i].id, sourcePort: 'state', targetNodeId: node.id, targetPort: 'state' }));
mini.expressions = {
  board: { exprType: 'map', stateKey: 'board', itemName: 'item', indexName: 'index', itemType: 'String', body: { exprType: 'if', condition: { exprType: 'compare', op: 'Eq', left: { exprType: 'fieldAccess', target: 'state', path: ['index'] }, right: { exprType: 'constant', kind: 'Integer', value: '0' } }, thenExpr: { exprType: 'fieldAccess', target: 'state', path: ['choice'] }, elseExpr: { exprType: 'fieldAccess', target: 'state', path: ['item'] } } },
  credits: { exprType: 'binary', op: 'Div', left: { exprType: 'fieldAccess', target: 'state', path: ['evalWin'] }, right: { exprType: 'constant', kind: 'Integer', value: '20' } },
};
const fixtures = { 'lab-demo.json': author.createNineLivesGraph(), 'lab-demo-buy.json': author.createNineLivesGraph('buy'), 'lab-demo-mini.json': mini };
for (const [name, graph] of Object.entries(fixtures)) {
  const file = path.join(directory, name);
  const json = JSON.stringify(graph, null, 2) + '\n';
  if (process.argv.includes('--check')) {
    if (fs.readFileSync(file, 'utf8') !== json) throw new Error('Graph fixture is stale: ' + name);
  } else fs.writeFileSync(file, json);
}
console.log('Nine Lives graph fixtures ' + (process.argv.includes('--check') ? 'verified' : 'exported') + '.');
