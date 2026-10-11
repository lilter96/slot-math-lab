// Runs one reference entry of this directory against a checkout of the original tgslots games.
//
//   TGSLOTS_DIR=/path/to/tgslots node scripts/verification/tgslots/run.mjs ancient-dragon.ts
//   TGSLOTS_DIR=/path/to/tgslots node scripts/verification/tgslots/run.mjs ancient-dragon.ts client
//
// Each entry plays the original TypeScript game and writes the golden file the backend tests replay through
// the migrated graph. With `client` it instead drives the original game server through a few rounds and writes
// what the server answered the game client, which the game server of this repository must answer too. tgslots is not a dependency of this repository, so the golden files are committed and
// this runner is only needed to regenerate them.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { Worker } from 'node:worker_threads';

const here = path.dirname(fileURLToPath(import.meta.url));
const repository = path.resolve(here, '../../..');
const tgslots = process.env.TGSLOTS_DIR;
if (!tgslots || !fs.existsSync(path.join(tgslots, 'packages/games'))) throw new Error('Set TGSLOTS_DIR to a tgslots checkout.');
const { rolldown } = createRequire(path.join(repository, 'frontend/package.json'))('rolldown');

const sourceFile = file => [file, file + '.ts', path.join(file, 'index.ts')].find(candidate => fs.existsSync(candidate) && fs.statSync(candidate).isFile());
// The tgslots workspace links its packages by name; resolve those names, and its `.js` import suffixes, to the sources.
const workspace = {
  name: 'tgslots-workspace',
  resolveId(source, importer) {
    // The modules of the original game server, which the entries drive to record what it answered the client.
    if (source.startsWith('@tgslots-api/')) {
      const file = sourceFile(path.join(tgslots, 'apps/api/src', source.slice('@tgslots-api/'.length)));
      if (!file) throw new Error('Cannot resolve ' + source);
      return file;
    }
    if (source.startsWith('@tgslots/')) {
      const [, name, ...rest] = source.split('/');
      const root = [path.join(tgslots, 'packages', name), path.join(tgslots, 'packages/games', name)].find(directory => fs.existsSync(directory));
      const exported = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8')).exports ?? {};
      const target = exported[rest.length ? './' + rest.join('/') : '.'] ?? (exported['./*'] ?? './src/*.ts').replace('*', rest.join('/'));
      const file = sourceFile(path.resolve(root, target)) ?? sourceFile(path.resolve(root, target.replace(/\.ts$/, '')));
      if (!file) throw new Error('Cannot resolve ' + source);
      return file;
    }
    if (source.startsWith('.') && importer) {
      const file = path.resolve(path.dirname(importer), source);
      return sourceFile(file) ?? sourceFile(file.replace(/\.js$/, '')) ?? null;
    }
    return null;
  },
};

const [entry, ...args] = process.argv.slice(2);
const bundle = await rolldown({ input: path.resolve(here, entry), plugins: [workspace], logLevel: 'silent' });
const { output } = await bundle.generate({ format: 'esm' });
const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'tgslots-reference-'));
fs.writeFileSync(path.join(directory, 'entry.mjs'), output[0].code);
// The original games recurse once per sampled value (Woodland Whisper's pick bonus thousands deep for a rare
// award), which overflows the default stack; the entry runs on a thread with a deep one.
fs.writeFileSync(path.join(directory, 'run.mjs'),
  "import { workerData } from 'node:worker_threads';\nimport main from './entry.mjs';\nawait main(workerData);\n");
const worker = new Worker(path.join(directory, 'run.mjs'), {
  workerData: { golden: path.join(repository, 'backend/SlotMath.Core.Tests/TestData/Games'), args },
  resourceLimits: { stackSizeMb: 512 },
});
worker.on('error', error => { console.error(error); process.exitCode = 1; });
