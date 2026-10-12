# Raw numeric state and measurement AST — 2026-10-12

## Behavior

Project Data/AST scalar and numeric-array inputs now retain the user's text locally and validate it before changing canonical state. Empty text, an incomplete minus/exponent, invalid syntax, overflow and unsupported numeric magnitudes are visible unapplied drafts. They cannot become zero or a rounded integer in the model. Supported decimal/scientific syntax accepts `.05`, leading plus/zeroes, trailing decimal point and signed long values. Valid changes apply immediately; typing an exponent retains focus and text rather than remounting the field. Changing project/path/field resets that input context.

`ModelNumberInput` supplies a labelled field, aria-invalid, an associated error message and the shared `parseModelNumber` boundary. Boolean, string, structured read-only and AST string constants retain their prior paths. Ordinary fractional values retain existing IEEE-754 model semantics; arbitrary decimal precision is not claimed.

Measurement AST/visual JSON now uses original-token validation. Large Integer/Rational string constants remain unchanged. The text-expression parser remains unchanged. No mathematical engine, atom, compiler or API schema changes.

## Evidence

- New editable-number unit 1/1; measurement AST/new and affected DSL units 2/2 (`raw-number-unit.log`, `raw-number-measurement-unit.log`). Values include rejected original unsafe tokens, incomplete text, supported long/scientific syntax and retained `9223372036854775807`/`1/3` strings.
- Final build and exact-file ESLint pass (`raw-number-build-final.log`, `raw-number-lint*.log`).
- Initial new/affected Chromium 4/5. One new numeric-row test used a partial label matching the toolbar and dialog. The exact selector correction reran only that failed case, 1/1 (`raw-number-chromium-followup.log`). Original failure log/image retained. No dropped assertions.
- Firefox/WebKit new/changed cases 6/6 (`raw-number-cross.log`). Actual downloads prove invalid row input leaves the original model, supported scalar `-1e10` and row `.05` export correctly and unknown metadata remains intact. Existing raw-row deletion and actual obsolete-execution cancellation cases passed in the initial Chromium selection.
- Actual desktop review found the Add-row button stretched to the height of its neighbouring error. The grid now aligns items at the start. Build/touched lint pass; only the changed numeric-row capture/export case reran: Chromium 1/1, Firefox/WebKit 2/2 (`raw-number-grid-*.log`). Final desktop capture was reviewed.
- Four-size error-dialog captures in each engine show no document overflow. WebKit desktop/phone were visually reviewed; invalid text and its wrapped error remain visible. Images: `raw-number-{engine}-{1440,1280,1024,390}.png`. Inner dialog scrolling on phone is retained.
- Updated draft-recovery numeric-edit test now expects rejection before mutation, rather than allowing an invalid model and waiting for autosave to fail. Its unchanged last-safe draft and corrected valid autosave assertions pass in all three engines.

No full test suites. Required scoped Rider attempts exit 3 with missing solution context (`raw-number*-rider.log`); Reformat/Full Cleanup remain unavailable. ESLint is separate validation.

## Remaining work

The legacy node Inspector still uses Number/parseInt for caps, iterations and draw rows. Integrate validated numeric inputs there, including integer and source-domain rules. Finish outgoing model/server export and AI ingestion guards, and dedicated unsupported typed-client/Results evidence. Persisted measurement workspace/other execution-option inputs still need their own audit. Do not claim all numeric inputs or full-Int64/decimal authoring are complete. Command palette, actual compositor performance and practitioner/Core/Rider gates remain open.
