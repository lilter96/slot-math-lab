# Start the two graph-native studios

Use `$slotmath-graph-studios`.

Implement these features for the multi-game Slot Math Lab, in this order:
1. Reel & Paytable Studio, including physical reels, independent weighted-cell/outcome sources, payout matrices and applicable paylines.
2. State Machine Designer, including safe projection of existing gameplay logic and creation/editing of machines lowered into the existing graph primitives.

This is an implementation task, not another general redesign proposal. Both editors must work on the current project's existing executable graph. Cover Ancient Dragon, Woodland Whisper, X7 Club, Nine Lives TGSlots and Le Militare, relevant paid/bought/mode variants, the independent reference model and compatible custom graphs. Do not center the product on Nine Lives or write game-specific runtime branches.

Read applicable AGENTS and:
- `docs/design/graph-studios/00_MAIN_AUDIT.md`
- `docs/design/graph-studios/01_GRAPH_EDITING_CONTRACT.md`
- `docs/design/graph-studios/04_EXECUTION_AND_ACCEPTANCE.md`
Then read the relevant feature spec for the current milestone. Use installed frontend-design; use ui-ux-pro-max only if found. Do not repeatedly load all historical design documents.

Reconcile the current worktree/HEAD with fetched main. The reviewed source was main at `b81e71c82cb0314d066d7134278751b85676f356`; inspect any relevant drift. Preserve existing useful/uncommitted work and project data. Follow the repository worktree and scoped-cleanup rules. Do not reset/overwrite another checkout or weaken tests/hooks.

Execute M0–M5 autonomously. Deliver complete working surfaces, not placeholder screens or endless micro-style patches. The new task replaces older scope instructions to redesign everything or to stop at the Nine Lives Overview. Existing repository safety and atomic-graph rules still apply.

First prove source bindings and atomic edits, then complete the Studio. Next prove state-machine lowering with existing Loop/Branch/Draw/state/library nodes and complete its UI. Reuse the current compiler/APIs; do not implement a second mathematical engine or add unauthorized atoms. Preserve exact values, derived dependencies, seeded no-op behavior and private project scope.

Use the acceptance matrix and separate graph-correctness, workflow and full-page visual gates. Inspect actual browser screenshots, all tabs/drawers/internal scrollers and fresh direct-route loading. Run focused checks while developing and the required broader regression at milestone/final boundaries. Keep evidence and one concise STATUS.md.

Do not start new Overview, Results, AI, Scenario Lab, RTP Anatomy or debugger projects. Fix only directly blocking issues and record unrelated ones. Stop when both requested editors pass their gates, or report a specific genuine blocker without fabricating completion. Do not push, merge main or deploy without a separate explicit request.
