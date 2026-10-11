# Inspector pinning and interaction performance follow-up

Source `e6c404c`, 2026-10-12. This is a scoped follow-up to the comprehensive `b9d92a1` regression, not a new complete suite pass.

## Delivered behavior

Build opens a contextual inspector on node selection; it can also be opened explicitly without a selection. Pin keeps the panel visible after deselection, model import, nested navigation and project switching. The preference is stored under a verified-user browser namespace, separate from canonical model JSON and project drafts. Another account does not inherit it. Switching clears the prior node's identity and displays the new project's selection or the empty inspector.

Unpin with a selected node retains the contextual panel; unpin without a node dismisses it and returns focus to Show inspector. Close and Escape clear the persisted pin and dismiss the panel. Focus returns to the current node when available, otherwise Show inspector. Reload keeps an explicitly pinned empty inspector. Preference storage is best-effort when browser storage is unavailable; mathematical drafts still use their existing save-failure recovery.

## Targeted verification

| Check | Evidence |
|---|---|
| Production build and touched-file ESLint | Passed on Node 22; no full lint/unit/API/Core/browser suites repeated |
| Initial affected Chromium workflows | 4/4 passed: two new pin behaviors, existing contextual inspector and multi-project draft preservation |
| Initial affected Firefox/WebKit workflows | 6/6 passed: two new pin behaviors and existing contextual inspector in both browsers |
| Existing legacy inbound compatibility | Passed with preserved edges/definitions/export assertions |
| Final pin focus + legacy import Chromium | 2/2 passed after the compatibility/focus follow-up |
| Final pin focus + legacy import Firefox/WebKit | 4/4 passed after the same follow-up |
| Four pinned-inspector viewports | All four pass no document overflow, accessible on-screen Close button and current empty selection; reduced motion enabled |
| Required Rider passes | Incomplete; standalone Reformat exits 3 with `solution can't be null`; frontend Full Cleanup remains unsupported outside the .NET solution |

The first added legacy-pin check encoded the whole large Nine Lives browser draft into a URL and received a blank preview page before the application mounted. The fixture was replaced with a small valid legacy frontend model to exercise the intended compatibility contract; its pin/selection assertions were preserved. Only that failed/new affected scenario was rerun, followed by the affected focus/import scenarios across browsers. The earlier failure log remains locally available. No URL/server limit was relaxed.

Actual images are `inspector-pinned-empty-{1440,1280,1024,390}.png` and `inspector-pin-keyboard-390.png`, excluded from Git. Desktop and phone empty pinned panels were visually inspected. The inspector intentionally occupies desktop width and overlays the tablet/phone graph; no-selection space is allocated only after explicit opening/pinning. [Viewport evidence](inspector-pin-viewport-audit.json) records the four measurements.

## Actual interaction measurement

[Performance evidence](interaction-performance-audit.json) uses the same production source with a real validated owner, 1440×900 headless Chromium and reduced motion. Three fresh contexts rendered the 30-cell reference Overview in 217–309 ms from navigation start, measured at its geometry DOM plus two animation frames. This is a local readiness observation, not field Web Vitals or a user study.

Real pointer drag and wheel input were recorded on the 4-node root and largest 84-node free-spin definition. Both samples observed about 60 RAF callbacks/second, p95 intervals 16.7 ms, zero sampled intervals over 20 ms and 120 frames with changed viewport transforms. Opening the largest definition produced 84 nodes in 132 ms, including browser automation overhead. With inspector closed, the measured canvas occupied 81.3% of content width, exceeding the plan's 65% geometry target in this fixture.

These are measured active-input observations, replacing the earlier idle-only sample as interaction evidence. RAF cadence is not compositor frame rate and does not prove a universal ≥50 FPS guarantee. Mathematical evaluation endpoints were aborted to isolate UI work; this is not multi-user server throughput, a performance improvement claim or a guarantee under concurrent heavy calculations. The authored graphs, math kernel and PRNG were unchanged.

## Remaining work

Overview still needs the last verified result matching the selected model's exact source, rather than only a general Results link. Visible-node rendering and a broader keyboard/accessibility requirement audit remain pending. Required Rider cleanup, full Core release verification, practitioner acceptance and an authorized staged release are not complete.
