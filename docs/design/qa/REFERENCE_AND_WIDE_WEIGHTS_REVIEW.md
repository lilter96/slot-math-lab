# Reference controls and browser-safe long weights

Sources: `ac65826` (reference controls/readiness), `43f32ce` (wide weights), `626ad84` (capture settling). No kernel/API/PRNG or authored game-math changes.

## Reference demo

Replay inputs, small game buttons and the return link have at least 44px targets; captions have at least 12px text and a lighter readable token. Existing larger Spin/Buy targets remain intact. The project switcher is no longer hidden at intermediate widths. Rules dialogs use their actual heading as the accessible name, preserve native Escape/focus return and do not execute a round.

The first cross-browser rules check exposed a real readiness race: the button existed before its handler was attached. Sound/bet/rules/turbo remain disabled until binding finishes. Replay also waits for the mounted game controller; filling a hash during delayed asset loading cannot enable an inert action. A controlled real asset delay verifies both states.

Initial Chromium 3/3 passed; initial Firefox/WebKit 4/6 retained two rules-readiness failures. After correction/new readiness coverage: Chromium 4/4, including real play/replay/negative seed/unchanged replay balance; combined final cross selection 12/12 includes these cases and wide-weight cases. Only the changed size/capture scenario reran after preserving the larger primary buttons: Chromium 1/1, Firefox/WebKit 2/2. Capture-settling change alone: cross 2/2. Build/touched lint pass. See reference-legibility-*, reference-ready-*, reference-wide-final-cross.log, reference-primary-target-* and reference-capture-settle-* logs.

Four actual captures are `reference-legibility-{1440,1280,1024,390}.png`; desktop/phone inspected. They have no document overflow, readable captions, visible project entry and measured targets. These are initial-state captures, not fabricated spin evidence.

**Visual acceptance remains incomplete.** Current headless WebKit captures show incomplete/repeated initial reel sprites. A separate ready-state capture after another two seconds still shows the problem (`reference-stable-webkit-1440.png`; script log reference-stable-webkit.log). The four-frame capture wait did not resolve it. Inspect the actual reel/art renderer before calling the demo visually accepted; passing numerical replay does not prove sprite correctness. Updated agent score remains 13/16: T2/D1/P2/H1/I2/A1/V2/S2. Readability improved, but domain presentation now loses a point. Canvas accessibility is also not fully certified.

## Wide weighted tables

The authoritative DrawWeight contract has `long` Weight and Value (`backend/SlotMath.Core/Model/Graph.cs`). modelCommands had imposed an unrelated Int32 limit. It now accepts exact non-negative browser-safe integer weights and signed browser-safe integer payloads. Totals are checked with BigInt before Number-based preview/application. Both rows and total must remain at most Number.MAX_SAFE_INTEGER (9,007,199,254,740,991); unsafe values/totals are rejected with an explicit browser-editor limit, not a false backend restriction. Unsupported numeric-source wording is corrected.

Affected/new model/CSV units 6/6 pass (`wide-weights-unit.log`). Chromium wide cases 2/2 are included in reference-wide-chromium.log (4/4 overall); Firefox/WebKit wide cases 4/4 are in the combined 12/12 final cross log. A real compiler and exact evaluation prove weights 10,000,000,000 each give expectation 0.5, then 30,000,000,000 / 10,000,000,000 give 0.75. Mounted Undo restores the original weight; export preserves payloads, metadata and edges. CSV stages large weights; an unsafe total never applies. Unit checks also preserve signed payloads above Int32.

This does not claim full Int64 JSON support. Import/server-read/export paths for integers above JavaScript's safe range still need a lossless/rejection audit; filtering an unsupported editor source alone cannot certify their round trip.

No complete suites were launched. Required Rider attempts still exit 3 (`solution can't be null`) and Full Cleanup remains unavailable. Touched source/test inventory is in RIDER_CLEANUP_REQUIRED.md; lint is supplementary. Rendering, precision-boundary imports, command palette, performance, practitioner and Core/Rider acceptance gates remain open.
