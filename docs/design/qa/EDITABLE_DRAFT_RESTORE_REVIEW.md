# Editable-draft backup restoration

The personal JSON import in **My projects** now accepts `slotmath-editable-draft-v1`, the format downloaded when an expression cannot be serialized. It validates the envelope, editor node IDs/data/coordinates, edge identities/endpoints and nested context envelope before mutating the workspace. Original number-token checks reject unsupported browser magnitudes. Unknown node/model data and incomplete DSL are preserved for repair; this path deliberately does not require an executable expression.

Restoration creates a fresh private project ID in the verified browser workspace and retains the current project. Imported verification source, results draft and authoring-project references are cleared; a backup cannot supply current mathematical proof or switch back to a foreign project ID. The import description explains these semantics. A malformed file leaves the active project untouched and displays an error.

## Verification

- `editable-restore-build.log`: production build passes.
- `editable-restore-unit-final.log`: 2/2 pass. Invalid DSL/unknown fields and nested parent data survive; evidence/references are cleared. Invalid format, duplicate nodes, missing coordinates, dangling edges, malformed nested envelope and an original unsafe numeric token are rejected.
- `editable-restore-chromium-final.log`: 1/1 pass. Actual invalid-expression download → another coin project → malformed-file rejection without replacing that game → valid backup import → original text visible → repair → three independent projects → original coin open/reload.
- `editable-restore-cross.log`: WebKit passes 1/1; initial Firefox failed after immediate reload without first verifying the debounced storage write. The strengthened scenario checks actual persisted current name and three project records before reload; `editable-restore-firefox-final.log` passes 1/1. The initial failure is retained; no universal immediate-unload flush guarantee is inferred from this rerun.
- Scoped lint has zero errors. The existing Projects effect-cleanup ref warning remains; test files have no warnings. No complete suite ran.

The first browser run failed because its locator included both personal and server rows with the same name. It was scoped to Personal projects without weakening the assertion; failure evidence remains in `editable-restore-chromium.log`.

Rider standalone attempts (`editable-restore-rider.log`, `editable-restore-tests-rider.log`, `editable-restore-persist-rider.log`) exited 3 with `solution can't be null`. Required Reformat/Full Cleanup is unavailable and remains outstanding for all four source/test files listed in RIDER_CLEANUP_REQUIRED.md.

This imports a single editable project backup, not a quarantined full workspace archive. Full workspace recovery still offers exact original download; unsupported Int64 numbers remain rejected, and a general archive importer is not provided. The nested envelope has unit preservation evidence, not a dedicated browser restoration scenario. Remaining malformed import/nested paths, typed-client/Results evidence, measurement/options and release gates still require work.
