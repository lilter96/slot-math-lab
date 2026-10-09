/**
 * Generates typed TypeScript definitions from the OpenAPI spec.
 *
 * Usage:
 *   node scripts/generate-api.mjs                     # from local schemas/openapi.json
 *   node scripts/generate-api.mjs --url <backend-url>  # fetch from running backend
 *   npm run generate-api                                # via package.json script
 *
 * Output:
 *   src/api/generated-types.ts   — TypeScript types for openapi-fetch typed client
 *
 * The generated types are consumed by openapi-fetch to produce a fully typed client.
 * No hand-written request types — everything derives from the OpenAPI contract.
 */

import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import openapiTS, { astToString } from 'openapi-typescript';

const __dirname = dirname(fileURLToPath(import.meta.url));

const OUT_DIR = resolve(__dirname, '..', 'src', 'api');
const OUT_FILE = resolve(OUT_DIR, 'generated-types.ts');
const LOCAL_SPEC = resolve(__dirname, '..', 'schemas', 'openapi.json');

// ── Parse arguments ────────────────────────────────────────────────────
const args = process.argv.slice(2);
const urlIndex = args.indexOf('--url');
const backendUrl = urlIndex >= 0 ? args[urlIndex + 1] : null;

// ── Helpers ─────────────────────────────────────────────────────────
async function getSpec() {
  if (backendUrl) {
    console.log(`Fetching OpenAPI spec from ${backendUrl} …`);
    const res = await fetch(backendUrl, { signal: AbortSignal.timeout(15000) });
    if (!res.ok) {
      throw new Error(`HTTP ${res.status}: ${res.statusText}`);
    }
    return res.json();
  }

  console.log(`Using local spec: ${LOCAL_SPEC}`);
  return JSON.parse(readFileSync(LOCAL_SPEC, 'utf-8'));
}

async function generate(spec) {
  console.log('Generating TypeScript types …');
  const ast = await openapiTS(spec, { alphabetize: true, exportType: true });
  mkdirSync(OUT_DIR, { recursive: true });
  writeFileSync(OUT_FILE, astToString(ast), 'utf-8');
  console.log(`Done — ${OUT_FILE}`);
}

// ── Main ────────────────────────────────────────────────────────────
async function main() {
  // An explicitly requested backend is authoritative. A failed fetch must not
  // make stale seed types appear to be a successful contract regeneration.
  const spec = await getSpec();
  await generate(spec);
  if (backendUrl) writeFileSync(LOCAL_SPEC, JSON.stringify(spec, null, 2) + '\n', 'utf-8');
}

main().catch((err) => {
  console.error('Generation failed:', err.message);
  process.exit(1);
});
