# Inspector integer transactions — 2026-10-12

## Delivered behavior

Both legacy primitives and source-backed imported nodes use original-text numeric validation for WinCap, iteration cap, draw weights and values. Empty/incomplete text, fractions in integer fields, overflow and unsupported magnitudes remain unapplied. WinCap is positive; loop limits follow the existing shared 1–100000 contract. Legacy steppers stop at both boundaries. Native loop edits update the canonical maxIterations and the graph-card iterations alias together.

Zero weights are permitted when the remaining total is positive. Weight edits validate the exact whole-table total before mutation. Empty/all-zero/unsafe-total changes, including legacy Add/remove operations, do not silently change the distribution. Native rows now include an editable signed integer value. Unknown model/row properties retain the existing spread/canonical mapping.

Static table totals and means use BigInt products, sum and rational reduction. The label explicitly identifies authored static rows; native expression overrides are stated separately. This is a source-table fact, not an execution/provenance claim. Individual values and weight-edit totals retain the documented browser-safe budget; full Int64 authoring is not implemented.

Per-project/path/node keys reset numeric drafts when context changes. Missing native numeric source properties stay empty rather than being coerced to zero. Legacy effective defaults remain consistent with its existing serializer.

## Evidence

- Exact row-summary and admission units 2/2 (`inspector-number-unit.log`), including products beyond safe double precision, exact totals, signed fractions, zero weights and unsafe-total rejection.
- Initial build/lint exposed a missing native summary render; corrected before browser validation. Final builds/scoped lint pass (`inspector-number-alias-*`, `inspector-number-density-*`).
- Initial Chromium 0/4: the tests exported through a canvas toolbar behind the deliberately overlaid 1280px Inspector. They now perform simultaneous Inspector/toolbar actions at 1440px, where the panel is docked. All four failed/new cases pass 4/4 (`inspector-number-chromium-followup.log`). Initial logs/image retained. Narrow overlay workflows require closing the drawer or using the separate Export surface; no simultaneous background interaction is claimed.
- Firefox/WebKit new cases 8/8 (`inspector-number-cross.log`). Each authoring format has an independent actual compiler/light-evaluation result of Exact RTP 0.75 after 3:1 weight editing, plus actual downloaded JSON for retained safe data, unsupported edits and signed values.
- WinCap and loop fraction/range violations retain previous exported values. Corrected caps export as 10000000000 and 100000. The actual graph-card iteration caption updates to 100000; the legacy increment control disables at the limit.
- Four-size captures for both authoring formats in each engine have no document overflow. Desktop/phone review found error text squeezing neighbouring weight columns. Fixed table sizing and inner horizontal scrolling preserve usable numeric widths; error text wraps. Only affected draw/export/capture and shared raw-field/pin workflows reran: Chromium 4/4 (`inspector-number-density-chromium.log`). Firefox/WebKit affected draw/export/capture 4/4 also pass (`inspector-number-density-cross.log`). A strengthened final capture assertion scrolls to the edited field and checks its full bounds within the table viewport at every size; Chromium 2/2 passes, Firefox/WebKit 4/4 pass (`inspector-number-scroll-cross.log`). Final phone capture was inspected after scrolling; long input text retains normal native caret scrolling, while the complete field/error region is reachable.

No full suites, backend/kernel/schema/PRNG changes or deployment. Required scoped Rider attempts still exit 3 with missing solution context (`inspector-number*-rider.log`); Reformat/Full Cleanup remain unavailable. ESLint is separate validation.

## Limits and next work

Caps/browser budgets reflect existing source contracts; compiler validation still determines whole-model executability. Narrow drawers use inner table scrolling. The generic canvas Draw card still uses a floating-point two-decimal static mean and does not clearly account for expression overrides; align it with the new exact source summary next. Complete outgoing model/server export and AI response guards, plus unsupported typed-client/Results integration evidence and remaining measurement/option boundaries. Command palette, compositor performance and practitioner/Core/Rider gates remain open. This slice does not complete the design goal.
