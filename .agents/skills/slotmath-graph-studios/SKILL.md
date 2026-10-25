---
name: slotmath-graph-studios
description: Implement or review Slot Math Lab's Reel & Paytable Studio and State Machine Designer as source-preserving views over existing GraphConfig. Use for multi-game mathematical authoring, graph bindings, state-machine lowering, and their UX/verification. Do not use as a request to redesign Overview or add a new game runtime.
---

# Slot Math Lab graph-native studios

This is a project-specific authoring/quality contract. Follow applicable repository AGENTS. Paths below are relative to the repository root.

## Read progressively

Read the relevant sections of `docs/SYSTEM.ru.md`: private projects (`#projects`),
editors (`#editors`), graph/expressions (`#graph`, `#expressions`) and verification (`#testing`).
Inspect the corresponding source bindings, lifecycle transformations and tests before editing.
After context compaction, review the current task state and source diff.

Inspect the actual checkout and fetched main. Preserve existing work and data. The system description does not replace inspecting the actual local implementation.

## Non-negotiable output

Work within the requested Studio or State Machine scope for the multi-game product: Ancient Dragon, Woodland Whisper, X7 Club, Nine Lives TGSlots, Le Militare and relevant variants. Reference Nine Lives is separate. Existing custom graphs remain intact; unresolved operations are explicit and locally bounded.

The existing GraphConfig is the sole executable authority. Both views read source bindings and commit validated atomic graph changes. Never create a second math engine, add named game dispatch to Core/compiler/API, or add unauthorized atoms. State-machine cycles lower to existing Loop control, exclusive transitions to Branch chains—not ordinary graph fan-out.

Paytable metadata is not enough: synchronize actual runtime state/expressions and dependent precision tables, reel padding/stops/conditional distributions, payline parameters, wild/cluster-derived logic and applicable bounds. Preserve numeric exactness, IDs, random draw order for no-op edits, state ownership and unrelated custom content. UI layout metadata is non-executable and its persistence must be proven.

## Process and quality

Use installed frontend-design for visual execution; only claim ui-ux-pro-max use when it exists. Use the maintained system description and concrete source bindings to guide the requested workspace changes. Complete meaningful feature slices, with graph tests at the lowest correct layer and full-page visual inspection after coherent surfaces. Keep controls styled independently of route order.

There are three independent gates: mathematical/source correctness, actual user workflows, and visual/interaction quality. A green render test or a screenshot command is not evidence for all three. Use real compiler/play/run calls for edit-and-execute proof. Never regenerate independent expected game data to hide a regression.

Keep acceptance evidence in ignored artifacts and summarize results in the task or PR notes. Report real tooling blockers; do not retry unchanged broken tools indefinitely. Preserve task isolation and do not auto-push/merge/deploy. Stop after these two workspaces or a specific genuine blocker; do not resume unrelated roadmap items.
