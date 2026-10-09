// Validates sample JSON files against the generated Zod schema.
// Used by the drift-guard test to ensure .NET and Zod validation agree.
// Usage: node scripts/validate-samples.mjs <samples-dir-path>
// Outputs JSON: { "results": { "fileName": { "valid": true|false, "error": "..." } } }

import { readFileSync, readdirSync } from "node:fs";
import { resolve } from "node:path";
import ts from "typescript";
const schemaSource = readFileSync(new URL("../src/generated/graph-schema.zod.ts", import.meta.url), "utf8");
const schemaJs = ts.transpile(schemaSource, { module: ts.ModuleKind.ESNext });
// Resolve zod explicitly: a data URL has no relative module base.
const schemaModule = schemaJs.replace('from "zod"', `from "${import.meta.resolve("zod")}"`);
const { GraphConfigSchema } = await import(`data:text/javascript;base64,${Buffer.from(schemaModule).toString("base64")}`);

const samplesDir = process.argv[2];
if (!samplesDir) {
  console.error("Usage: node validate-samples.mjs <samples-dir>");
  process.exit(1);
}

const results = {};
const files = readdirSync(samplesDir).filter(f => f.endsWith(".json")).sort();

for (const file of files) {
  const path = resolve(samplesDir, file);
  try {
    const data = JSON.parse(readFileSync(path, "utf-8"));
    const parsed = GraphConfigSchema.safeParse(data);
    results[file] = {
      valid: parsed.success,
      error: parsed.success ? null : parsed.error.issues.map(i => `${i.path.join(".")}: ${i.message}`).join("; "),
    };
  } catch (err) {
    results[file] = { valid: false, error: err.message };
  }
}

console.log(JSON.stringify({ results }));
