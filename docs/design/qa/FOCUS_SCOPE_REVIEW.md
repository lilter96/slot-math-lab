# Controlled focus frames and nested execution

These tests verify the existing production behavior without changing application source. The unchanged bundle includes source 482c548 and earlier toolbar/data-execution guards.

The focus harness holds only the animation frame scheduled synchronously by the actual toolbar focus-node listener. Other animation frames and native ReactFlow camera methods continue normally. It records cancellation and deliberately invokes the cancelled callback afterward, so the callback's generation/context guard must also work. An active control proves that a surviving delayed command moves the camera. Independent-project and nested-mechanic cases reuse the same authored node IDs and selection; obsolete focus leaves the new camera unchanged. A newer node-picker selection also cancels and supersedes the old command.

The nested data-execution case holds a real successful /api/play/round response for the current complete Nine Lives graph. It invokes the real Save subgraph and return toolbar handler through the DOM while the native editor modal remains mounted; this is controlled external-context instrumentation, not a claim that users click through a modal. The real fetch AbortSignal is observed cancelled. Releasing the old response produces no state evidence; a fresh execution succeeds in the new context.

Scoped ESLint passes (`focus-scope-lint.log`). Chromium 5/5 and Firefox/WebKit 10/10 pass (`focus-scope-chromium.log`, `focus-scope-cross.log`). No application changes, rebuild, full suites or new visual screenshot claims. Required Rider attempts on both test files exit 3 (`solution can't be null`), logs focus-scope-rider.log and nested-execution-rider.log; Full Cleanup remains unavailable.

Existing real compiler root/nested edge-focus checks remain evidence for edge navigation. These controlled-frame cases specifically exercise node focus; they do not claim a separate held-edge-frame test, compositor performance certification or whole-goal completion. Table/rubric/practitioner/Core/Rider gates remain open.
