import fs from 'node:fs';
import path from 'node:path';
import ts from 'typescript';
import { fileURLToPath } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const src = path.join(root, 'frontend/src/games/doghouse');
const load = async text => import('data:text/javascript;base64,' + Buffer.from(ts.transpileModule(text, { compilerOptions: { module: ts.ModuleKind.ESNext } }).outputText).toString('base64'));
const author = await load(fs.readFileSync(path.join(src, 'graph.ts'), 'utf8').replace("import model from './model.json';", 'const model=' + fs.readFileSync(path.join(src, 'model.json'), 'utf8') + ';'));
const full = author.createDogHouseGraph();
const reference = await load(fs.readFileSync(path.join(src, 'expectationGraph.ts'), 'utf8').replace("import { createDogHouseGraph } from './graph';", 'const createDogHouseGraph=()=>(' + JSON.stringify(full) + ');'));
const mini = structuredClone(full);
const strips = [[3,13],[2,3],[3,13],[2,13],[3,13]];
strips.forEach((strip, col) => { mini.initialState['baseReel' + col] = [...strip, ...strip].map(String); mini.mechanics['dog-base-spin'].nodes.find(n => n.id === 'stop-' + col).drawWeights = strip.map((_, i) => ({ outcomeId: String(i), weight: 1, value: 0 })); });
const fixtures = { 'dog-house-ui.json': full, 'dog-house-expectation-ui.json': reference.createExpectationGraph(full), 'dog-house-mini-ui.json': mini, 'dog-house-mini-expectation-ui.json': reference.createExpectationGraph(mini) };
const directory = path.join(root, 'backend/SlotMath.Core.Tests/TestData/DogHouse');
fs.mkdirSync(directory, { recursive: true });
for (const [name, value] of Object.entries(fixtures)) {
  const file = path.join(directory, name), text = JSON.stringify(value, null, 2) + '\n';
  if (process.argv.includes('--check')) { if (fs.readFileSync(file, 'utf8') !== text) throw new Error('UI graph fixture is stale: ' + name); }
  else fs.writeFileSync(file, text);
}
console.log('Dog House UI graph fixtures ' + (process.argv.includes('--check') ? 'verified' : 'exported') + '.');
