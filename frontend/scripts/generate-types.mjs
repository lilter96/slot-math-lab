import { compile } from "json-schema-to-typescript";
import { jsonSchemaToZod } from "json-schema-to-zod";
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const schemaPath = resolve(__dirname, "..", "schemas", "graph-schema.json");
const outDir = resolve(__dirname, "..", "src", "generated");

const schema = JSON.parse(readFileSync(schemaPath, "utf-8"));

// Generate TypeScript types
const tsTypes = await compile(schema, "GraphConfig", {
  bannerComment:
    "// Auto-generated from JSON Schema. DO NOT EDIT.\n// Run `npm run generate-types` to regenerate.",
  style: { singleQuote: true, semi: true },
});

// Generate Zod schema
const rawZod = jsonSchemaToZod(schema, { module: "esm" });

// json-schema-to-zod returns: "import { z } from \"zod\"\n\nexport default z.object({...})"
// We need to extract the z.object(...) part and create a named export.
const zodObjectMatch = rawZod.match(/export default (z\.object\([\s\S]*\))\s*;?\s*$/);
const zodObjectExpr = zodObjectMatch ? zodObjectMatch[1] : rawZod;

const zodOutput = `// Auto-generated from JSON Schema. DO NOT EDIT.
// Run \`npm run generate-types\` to regenerate.

import { z } from "zod";

export const GraphConfigSchema = ${zodObjectExpr};

export type GraphConfig = z.infer<typeof GraphConfigSchema>;
`;

mkdirSync(outDir, { recursive: true });

writeFileSync(resolve(outDir, "graph-types.ts"), tsTypes);
writeFileSync(resolve(outDir, "graph-schema.zod.ts"), zodOutput);

console.log(`Generated types → ${outDir}/graph-types.ts`);
console.log(`Generated Zod   → ${outDir}/graph-schema.zod.ts`);
