# Draw canvas source summaries — 2026-10-12

## Implemented behavior

The generic Draw card uses the same BigInt/reduced-fraction static-row summary as Inspector. It no longer rounds to two decimals or styles an authored table mean as calculation proof. Source-backed weight-expression IDs and legacy expression text declare **Dynamic weights**, even when static rows are present or absent. Empty rows are Unconfigured, incompatible rows Unsupported, and a zero total has an undefined mean rather than 0.00.

Source resolution follows configPayload priorities: a native expression reference is authoritative; edited draw-row aliases override the stored native rows. No graph/model fields are mutated by this presentation. The complete mean has a title and wraps; a break opportunity after the fraction slash keeps a fitting denominator together. Signed values and large products use exact integer arithmetic. These are authored static-table facts, not executable-model validation, RTP or sampling evidence.

## Verification

- New summary unit 1/1 (`draw-card-unit.log`) checks source priority, dynamic/native-only references, incompatible rows, unconfigured/zero-total cases. Earlier exact row arithmetic units remain the documented source for BigInt products/signed fractions; not rerun.
- Build/touched lint pass, including final fraction-break source (`draw-card-build.log`, `draw-card-wrap-build.log`, `draw-card-lint.log`).
- Initial Chromium 1/2. The legacy dynamic fixture incorrectly supplied a native-style reference ID to the legacy field that contains DSL text. It now uses `state.weights`; only failed case rerun 1/1 (`draw-card-chromium-followup.log`). Failure log/image and actual parser diagnostic retained.
- Firefox/WebKit static/export and declared-mode cases 4/4 (`draw-card-cross.log`): exact 1/2 for equally weighted `(a+1)` and `-a`, no Draw calculation badge, unchanged actual JSON download, native/legacy dynamic references, dynamic without rows, zero-total/unconfigured states.
- Added independent long-fraction case: 9007199254740990/9007199254740991 stays exact. Four-size bounding/wrapping assertions pass Chromium 1/1 and Firefox/WebKit 2/2 (`draw-card-long-*.log`). Actual desktop/phone review found a poor mid-denominator break, corrected with a slash break opportunity. Only this affected case reran: Chromium 1/1, Firefox/WebKit 2/2 (`draw-card-wrap-*.log`).
- Screenshots `draw-card-{engine}-{width}.png` and `draw-card-long-{engine}-{width}.png` cover 1440×900, 1280×800, 1024×768 and 390×844. No document overflow. WebKit desktop/phone were reviewed; final fraction-break captures supersede initial line wrapping. This does not certify every graph/zoom or compositor performance.

No full suites or kernel/compiler/API/schema changes. Required scoped Rider attempts exit 3 with missing solution context (`draw-card*-rider.log`); no Reformat/Full Cleanup success is claimed.

## Newly reproduced defect and next task

Malformed legacy DSL can still blank the whole app. A fixture `weightExpressionId: "state-weights"` in a legacy frontend node triggers **Select a state field** while useModelProjection → peekRootProject → buildConfigPayload → parseExpression runs during render. `draw-card-legacy-diagnostic.log` independently reproduces the stack; `draw-card-legacy-first-failure.png` preserves the blank screen. Correcting the fixture verifies the supported card scenario; it does not fix or waive the parser failure.

Next: contain invalid/incomplete legacy expressions without losing their editable drafts, show a specific serialization error and prevent invalid execution/export. Audit useModelProjection, useLiveMetrics, useSimulation and callers deriving scopes via peekRootProject. Then finish outgoing model/server export, AI ingestion, unsupported typed-client/Results evidence and remaining measurement/option boundaries. Command palette, compositor performance and practitioner/Core/Rider gates remain open. No goal-completion or release-readiness claim.
