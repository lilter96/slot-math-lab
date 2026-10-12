# Destructive graph actions

Source: e991928, with the following test-only readiness correction. Presentation boundary only; model commands/compiler/math remain unchanged.

Inspector node deletion (native and legacy), ReactFlow keyboard node/edge deletion and legacy primitive outcome removal require a native browser confirmation. Cancel leaves source intact; confirmed node deletion removes its incident edges and retains the other node. Model-store operations remain usable independently of browser prompts.

## Scoped evidence

- Production build and touched ESLint passed; test changes afterward received scoped ESLint.
- Initial Chromium 3/4 passed; the new legacy test used the native inspector button name. Corrected its locator to the actual legacy `Delete` button. Only the failed case and a new keyboard-node case ran next: 2/2 passed.
- Firefox/WebKit 7/8 passed. The WebKit keyboard-node case focused before initial viewport readiness. Added visible sink/focus assertions without weakening deletion checks; only failed WebKit case reran and passed (1/1).
- Existing emptied-subgraph scenario accepts the deliberate confirmations and still proves every model node is removed and remains absent on reopening.
- Retained logs: `destructive-actions-build.log`, `destructive-actions-browser.log`, `destructive-actions-followup.log`, `destructive-actions-cross.log`, `destructive-actions-webkit.log`. No full suite was repeated.

This closes graph/primitive destructive confirmation, not every delete/reset surface. ProjectDataEditor data-row deletion, EvidenceInputsEditor input removal and PluginManager removal still need their own confirmation/transaction review and scoped checks. Staged table Reset is distinct from immediate canonical deletion; do not conflate them.

Native confirmation dialogs are browser/system UI; no app screenshot is claimed to capture their contents. Browser tests inspect their actual messages and accept/cancel them. Earlier actual graph captures remain source-context evidence, not dialog images.

Required Rider Reformat again exits 3; Full Cleanup remains unavailable for frontend files outside the solution. Logs `destructive-actions-*-rider.log` and `destructive-actions-rider.log` preserve attempts. This remains a release gate.
