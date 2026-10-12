# Authored mathematical tables

Sources: `65113ba` (numeric tables/copy/preferences) and `92af329` (reel density). This is feature evidence, not whole-goal acceptance.

Model data exposes Regular/Compact using the existing appearance preference. Density persists under the verified browser user namespace; separate users do not inherit it. Weighted, payout and reel rows now respond to the setting. Headers and the primary column remain fixed within bounded scrolling. Weighted/payout fields support Enter/Shift+Enter and Alt+vertical arrows without intercepting typing or clipboard shortcuts; changed inputs have a visible border and existing unapplied-change summary. Validating and applying still use the canonical source contract.

Raw copy passes authored payloads and exact ratios such as `1/1830` and `1/20`, rather than rounded display percentages/decimals. Clipboard failure reveals a selectable read-only value with an accessible label. Async status cannot populate an unmounted/replaced value. Native text-input selection remains available for authored weights/coins. Clipboard transport is intercepted in the browser tests to verify exact strings and denied-write recovery; this does not certify OS clipboard permission behavior.

## Geometry and actual visual review

The initial 38px normal / 32px compact design target conflicts with the same specification's minimum 36px desktop editing target. Authoring rows therefore use 44px Regular / 38px Compact on desktop; Compact inputs stay 36px. Phone inputs/copy targets stay at least 44px; measured weighted rows are 45px. This is an explicit geometry deviation, not a claim of literal 32px compliance. No controls overlap to force a smaller row.

Eight numeric/payout captures plus four reel captures cover 1440×900, 1280×800, 1024×768 and 390×844: `table-contract-{width}.png`, `table-contract-payout-{width}.png`, `table-contract-reel-{width}.png`. Capture assertions show no document overflow and preserve desktop/mobile target sizes. `table-contract-captures.json` records actual weighted row/input measurements. Desktop/phone numeric and payout images were inspected; reel phone was inspected. The final artifacts were refreshed by WebKit. This is viewport evidence; full-page capture does not unroll every inner scroll region.

Visual review caught WebKit's native light select surface with pale inherited text despite passing interaction tests. Scoped native-control styling fixes the dark surface and caret without replacing semantic selects. Final desktop/phone payout captures show readable selected values. No computed-metric badge or simulated result is added to authored tables.

## Validation and retained failures

Final production build and touched ESLint pass. ESLint retains one pre-existing WeightedDrawEditor cleanup-ref warning; no lint errors remain. Logs are `table-contract-*-build.log` and `table-contract-*-lint.log`.

- Initial Chromium: 3 affected existing cases pass; 4 new cases fail because the test fixture used an unsupported fractional Draw payload. Replaced it with legal `2147483647`, preserving the actual integer contract. Only the new cases reran: 3/4 pass, revealing actual 51px rows in both density modes.
- Moved percentage/fraction into one copy control. Only density/raw-copy/capture checks reran: 2/3 pass, exposing absent density persistence. Added an owner-scoped preference; only failed/new preference cases reran, 2/2 pass. Keyboard/source preservation and exact raw-copy checks retain their passing evidence.
- Initial Firefox/WebKit: 15/16 pass. Firefox reloaded before the model's debounced autosave, although density itself had persisted. Test now waits for the actual saved model; only affected reload plus new payout capture cases reran, 4/4. Chromium new payout case 1/1.
- After the actual WebKit select-color correction: only affected capture/reel view checks reran, Chromium 2/2 and Firefox/WebKit 6/6.
- New reel-density case initially failed in Chromium and both other browsers because the large test graph exceeded the preview server's URL/header limit. Uses actual file import now; only that case reran, Chromium 1/1 and Firefox/WebKit 2/2. No server limit was increased.

All initial logs remain in QA. Existing weighted/payout Apply/Undo/export/independent API evidence and all real reel sets remain covered by the scoped affected selections. No full suites, backend/kernel/PRNG changes or deployment.

## Agent rubric — Model data only

This is an agent assessment of the inspected weighted/payout views, not practitioner feedback or acceptance of every major view.

| Criterion | Score | Observation |
|---|---:|---|
| Task clarity | 1 | Editing fields and Validate/Apply are clear; commit actions require scrolling past the table. |
| Domain fidelity | 2 | Actual outcome IDs, weights, exact raw ratios and payout unit conversion are labelled; no universal reel schema. |
| Trust | 2 | Authored parameters are explicitly separate from measured RTP; rounded values have exact raw counterparts. |
| Information hierarchy | 1 | Dense table is useful on desktop; introductory controls/explanations consume much of the phone viewport. |
| Interaction | 2 | Typed entry, keyboard movement, density, copy/failure recovery and guarded Apply/Undo have scoped evidence. |
| Accessibility | 2 | Labelled native inputs, visible focus, sufficient control sizes, bounded scrolling; corrected WebKit select readability. |
| Design identity | 2 | Charcoal/Plex mathematical workbench, tabular numerals and source rows; no decorative card grid. |
| Integration | 2 | Canonical export/mode switching and user-scoped preference tests preserve independent workspace behavior. |
| **Total** | **14/16** | Agent threshold reached for these inspected views; actual human review and other views remain pending. |

Required Rider attempts still exit 3 (`solution can't be null`); Full Cleanup is unavailable. Exact touched files are in RIDER_CLEANUP_REQUIRED.md. Scored reviews of remaining views, practitioner acceptance, performance and required Core/Rider gates remain open.
