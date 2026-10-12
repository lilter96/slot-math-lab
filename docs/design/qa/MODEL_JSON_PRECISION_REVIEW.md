# Model JSON numeric ingestion — 2026-10-12

## Contract and change

The browser represents model numbers as IEEE-754 doubles. Core DrawWeight uses Int64; removing an Int32 editor limit does not make all Int64 values representable in the browser. JSON.parse can silently turn 9007199254740993 into 9007199254740992 before a validator inspects it.

`modelJson.ts` validates JSON syntax, scans original number tokens outside escaped string literals and rejects nonfinite values or magnitudes exceeding ±9007199254740991. A boundary-token comparison also rejects 9007199254740991.1, which would otherwise round onto the allowed maximum. Strings remain strings; large numeric-looking identifiers and metadata are preserved. Ordinary supported fractional model values retain existing IEEE-754 semantics; arbitrary decimal precision and lexical/byte-identical JSON exports are not claimed.

Raw checks now cover Projects file import/server opening, toolbar import, Export import, legacy inbound links, generated-client saved-config responses and Results pinned-config reads. Programmatic `loadProject`/`switchProject` reject unsupported numeric magnitudes before retaining or replacing a draft. Legacy link rejection is visible in the workbench. Export displays the specific import error instead of hiding it behind “Invalid JSON”. No kernel/API schema/math/PRNG changes.

Unsupported models are rejected for browser authoring; keep their original JSON. This does not implement lossless Int64 authoring/read-only viewing. Valid imports retain unknown fields and use the existing canonical serialization/export path.

## Evidence

- New units initially 2/3; the round-trip fixture incorrectly expected JSON.stringify to preserve the sign of -0. It now uses canonical zero; only the changed boundary test reran, passing both follow-up runs. Unsafe nested tokens, scientific notation, overflow, boundaries, strings/escaped quotes, malformed JSON and programmatic guards have passing scoped evidence.
- Initial build exposed unknown-valued legacy table/name assumptions; these now narrow types explicitly. Final build passes (`model-json-build-final.log`).
- Final exact-file ESLint has no errors and one existing Projects cleanup-ref warning (`model-json-lint-final.log`); final browser-file lint passes.
- Chromium initially 4/6, including both existing actual compiler/expectation/Undo/CSV long-weight scenarios. Two new tests navigated away before the initial imported project existed. Adding a visible project-title and processed-link wait corrected only those tests; failed-case rerun 2/2 (`model-json-chromium-followup.log`). No dropped assertions.
- Firefox/WebKit new browser checks 8/8 (`model-json-cross.log`): personal import, toolbar/Export rejection, legacy-link rejection, raw saved-response rejection. Each checks preserved model data by actual downloaded JSON. Safe import/export retains MAX_SAFE_INTEGER, 10000000000 and unknown string metadata.
- Real server-save/open compatibility passes 1/1 in Chromium (`model-json-server-save.log`), using the actual API and opening an independent draft.

No full test suites. Logs retain initial failures. Required scoped Rider attempts exit 3 with missing solution context; Reformat/Full Cleanup remain unavailable.

## Remaining precision work

The original raw JSON cannot recover digits already lost by earlier browser versions. Audit persisted browser drafts, raw graph/table numeric editing, AI-generated model ingestion and measurement AST inputs next, including outgoing/export guards. Typed-client and Results guards have source/units but their unsupported-response UI paths do not yet have dedicated browser integration evidence. Do not claim every numeric input boundary has been covered. Then continue command-palette, compositor-performance and practitioner/Core/Rider gates.
