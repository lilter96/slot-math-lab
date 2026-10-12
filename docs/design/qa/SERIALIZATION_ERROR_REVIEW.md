# Recoverable model serialization errors — 2026-10-12

## Reproduced failure and implementation

Malformed legacy DSL such as `state-weights` previously threw “Select a state field” during useModelProjection → peekRootProject → buildConfigPayload → parseExpression and blanked the app. The prior DRAW_CARD_REVIEW diagnostic retains that reproduction.

Rendering now reads an explicit `{ config, error }` snapshot. A failed serialization supplies no executable projection. The shell retains the graph/editor, names the error, links to Build and downloads the current raw editable draft with its original expression text and unknown table fields. The backup is labelled `slotmath-editable-draft-v1`, not presented as executable GraphConfig; automatic import of that raw format is not added here. Existing browser autosave keeps the editable state, including incomplete text, across reload.

Live preview returns the actual local error and no metrics; it no longer posts a fabricated empty “Invalid expression” model to the API. Executable JSON download/copy/server save disable when there is no serialized projection. Toolbar export catches and displays the original error. Strict serialization remains strict for execution callers; simulation already catches a serialization failure before launching. Outgoing buildConfigPayload now rejects unsupported numeric magnitudes/nonfinite values before JSON.stringify can normalize them.

Data execution, weighted edits, shared validation and delayed toolbar imports use a context token that survives serialization failure. Successful configs keep canonical comparison; invalid drafts include raw mathematical editing state, so invalid-to-invalid changes remain distinguishable. Model identity/path remain scoped. The mechanic manager and measurement/accounting/profile constructors no longer throw during render. Invalid schema/plan requests are blocked, with an explicit local notice and no fictitious schema-loading state.

No kernel/compiler/API schema/PRNG or game-specific dispatch changes. A null executable projection is not a substitute mathematical model or proof.

## Verification

- New strict serialization units 2/2 (`serialization-unit.log`): malformed DSL stays unmodified, corrected field access serializes properly, unknown metadata stays intact and unsupported outgoing numbers fail before JSON conversion.
- Initial build/lint caught an obsolete toolbar import; final builds and exact-file lint pass (`serialization-build-final.log`, `serialization-submit-build.log`, `serialization-lint-final.log`, `serialization-submit-lint.log`). One existing WeightedDrawEditor cleanup-ref warning remains.
- Chromium first two new cases 2/2 (`serialization-chromium.log`): backup preserves original raw text/metadata, Export operations disable, correcting the expression enables an actual model download, incomplete/invalid edits persist after reload, mechanic/Data dialogs remain accessible and no pageerrors/substitute preview occur.
- New invalid-plan dialog case initially failed on an incorrect “Simulation” tab selector; only that case corrected to the actual “Simulate” label reran, 1/1 (`serialization-dialog-chromium-followup.log`). Three dialogs remain editable with disabled saves, no schema requests and no fake loading.
- Firefox/WebKit new cases 6/6 (`serialization-cross.log`).
- Existing affected Chromium cancellation cases 4/4 (`serialization-affected-chromium.log`): source-change-and-restore execution, mounted payout validation, weighted unmount/same-template isolation and profile cancellation retain their assertions and actual request evidence. No complete suite was run.
- Four-size WebKit captures `serialization-error-webkit-{1440,1280,1024,390}.png` show no document overflow. Desktop/phone reviewed: error, current project, recovery action and graph workbench remain available. Inner/outer scrolling on phone is retained; not every error/pane combination is certified.

Required scoped Rider attempts exit 3 with missing solution context (`serialization*-rider.log`). Neither mandatory cleanup pass is claimed complete; ESLint is separate validation.

## Limits and next work

Raw draft download is a recovery/inspection artifact; executable model import remains the normal JSON contract. Add an explicit restoration path before claiming raw-draft backup/import round trips. Native AST/reference compiler errors still belong to backend validation, and nested/other malformed import shapes need their own evidence. This slice proves the reproduced legacy render/authoring failure and covered scopes, not every malformed model path.

Finish AI-generated response ingestion (raw-number and source semantics), explicit outgoing-payload bypasses and unsupported typed-client/Results integration evidence. Audit remaining measurement persistence/execution-option inputs. Then command palette, actual compositor performance and practitioner/Core/Rider gates. No whole-goal completion or release-readiness claim.
