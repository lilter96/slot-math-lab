---
name: slotmath-graph-studios
description: Implement or review Slot Math Lab's Reel & Paytable Studio and State Machine Designer as source-preserving views over existing GraphConfig. Use for multi-game mathematical authoring, graph bindings, state-machine lowering, and their UX/verification. Do not use as a request to redesign Overview or add a new game runtime.
---

# Slot Math Lab graph-native studios

This is a project-specific authoring/quality contract. Follow applicable repository AGENTS. Paths below are relative to the repository root.

## Read progressively

At task start read `docs/design/graph-studios/START_CODEX.md`, `00_MAIN_AUDIT.md`, `01_GRAPH_EDITING_CONTRACT.md`, and `04_EXECUTION_AND_ACCEPTANCE.md` from that directory. During Studio work read `02_REEL_PAYTABLE_STUDIO.md`; during machine work read `03_STATE_MACHINE_DESIGNER.md`. After context compaction read STATUS.md and relevant source changes, not all old design plans again.

Inspect the actual checkout and fetched main. Preserve existing work and data. The audit is pinned to a snapshot, not a promise the local branch matches it.

## Non-negotiable output

Deliver Studio first, State Machine second, for the multi-game product: Ancient Dragon, Woodland Whisper, X7 Club, Nine Lives TGSlots, Le Militare and relevant variants. Reference Nine Lives is separate. Existing custom graphs remain intact; unresolved operations are explicit and locally bounded.

The existing GraphConfig is the sole executable authority. Both views read source bindings and commit validated atomic graph changes. Never create a second math engine, add named game dispatch to Core/compiler/API, or add unauthorized atoms. State-machine cycles lower to existing Loop control, exclusive transitions to Branch chains—not ordinary graph fan-out.

Paytable metadata is not enough: synchronize actual runtime state/expressions and dependent precision tables, reel padding/stops/conditional distributions, payline parameters, wild/cluster-derived logic and applicable bounds. Preserve numeric exactness, IDs, random draw order for no-op edits, state ownership and unrelated custom content. UI layout metadata is non-executable and its persistence must be proven.

## Process and quality

Use installed frontend-design for visual execution; only claim ui-ux-pro-max use when it exists. Build the concrete workspaces in the specs, not generic dashboard cards. Complete meaningful feature slices, with graph tests at the lowest correct layer and full-page visual inspection after coherent surfaces. Keep controls styled independently of route order.

There are three independent gates: mathematical/source correctness, actual user workflows, and visual/interaction quality. A green render test or a screenshot command is not evidence for all three. Use real compiler/play/run calls for edit-and-execute proof. Never regenerate independent expected game data to hide a regression.

Maintain one STATUS.md and the acceptance matrix evidence. Report real tool/cleanup blockers, do not disable policy or retry unchanged broken tools forever. Preserve task isolation and do not auto-push/merge/deploy. Stop after these two workspaces or a specific genuine blocker; do not resume unrelated roadmap items.
