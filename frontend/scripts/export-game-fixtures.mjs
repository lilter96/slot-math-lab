// Writes every game graph authored in src/games as JSON for the backend tests; `--check` fails when one is stale.
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {rolldown} from 'rolldown';
import {sameJsonSource} from './json-source-equality.mjs';

const frontend = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const directory = path.resolve(frontend, '../backend/SlotMath.Core.Tests/TestData/Games');
const bundle = await rolldown({input: path.join(frontend, 'src/games/fixtures.ts'), logLevel: 'silent'});
const {output} = await bundle.generate({format: 'esm'});
const {gameFixtures} = await import('data:text/javascript;base64,' + Buffer.from(output[0].code).toString('base64'));
const check = process.argv.includes('--check');
const LINE = 140;

/** JSON laid out one member per line, except that a list of plain values and anything short stay on one line. */
function layout(value, indent = '') {
    const flat = JSON.stringify(value);
    if (typeof value !== 'object' || value === null || flat.length <= LINE) return flat;
    const list = Array.isArray(value), inner = indent + ' ';
    if (list && value.every(item => typeof item !== 'object' || item === null)) return flat;
    const members = list ? value.map(item => layout(item, inner)) : Object.entries(value).map(([key, item]) => JSON.stringify(key) + ': ' + layout(item, inner));
    return (list ? '[' : '{') + '\n' + members.map(member => inner + member).join(',\n') + '\n' + indent + (list ? ']' : '}');
}

fs.mkdirSync(directory, {recursive: true});
for (const [name, create] of Object.entries(gameFixtures)) {
    const file = path.join(directory, name), text = layout(create()) + '\n';
    if (!check) {
        let current = false;
        if (fs.existsSync(file)) {
            try {
                current = sameJsonSource(fs.readFileSync(file, 'utf8'), text);
            } catch {
                // Regeneration also repairs a truncated or malformed fixture.
            }
        }
        if (!current) fs.writeFileSync(file, text);
    } else if (!fs.existsSync(file) || !sameJsonSource(fs.readFileSync(file, 'utf8'), text)) throw new Error('Game graph fixture is stale: ' + name + ' (run node frontend/scripts/export-game-fixtures.mjs)');
}
console.log(`${Object.keys(gameFixtures).length} game graph fixture(s) ${check ? 'verified' : 'exported'}.`);
