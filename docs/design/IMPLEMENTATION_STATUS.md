# Precision Workbench implementation checkpoint

Branch: `feat/precision-workbench-redesign`. Isolated worktree based on fetched `origin/main` (`b81e71c`). Existing task worktrees and uncommitted work are untouched. Authorization: `/home/terentiy/Downloads/CODEX_AUTORUN.md`; its continuous execution directive supersedes design-document review pauses.

## Baseline audit

- React 19 / Vite / Zustand / React Flow / .NET 10 retained. Existing routes `/build`, `/simulate`, `/results`, `/export`, feature-gated `/play`, games.html, project/share/run query links.
- Model: canonical GraphConfig in tables and nodes; nested changes serialized through graphTrail/peekRootProject. No second math model.
- Nine Lives: manifest 6 reels × 5 rows, cluster/cascade/free-spins/buy; actual local symbol assets available. Sample model uncalibrated.
- Defects: absent metrics default to Exact; absent response numerics become zero; obsolete results survive edits/errors; default selection wastes inspector space; top mechanic chips and toolbar crowd canvas; toolbar falsely advertises a 96% target.
- Capabilities: inline drawWeights exist in nested mechanics; generic state arrays and expression AST exist. No guaranteed universal reel-strip/paytable mapping. Support explicit source-backed adapters only.
- Design: retain cool charcoal/Plex; add provided semantic aliases; structured navigation, a model-derived board, compact mechanic tree, full-width canvas. Skill generator's documentation landing recommendation rejected as off-topic; keyboard/focus UX guidance applies.
- Dependencies installed using npm ci --legacy-peer-deps (0 vulnerabilities). Baseline checks underway. Rider cleanup for frontend requires discovery; CLI .NET solution excludes frontend.

| Slice | Status | Files / decisions | Evidence |
|---|---|---|---|
| 0 baseline | verified (Core/full critical still running) | required documents and actual editing/state/API code read | QA logs/screenshots pending |
| 1 trust and shell | implemented; critical sweep pending | suppress absent/stale proof; responsive semantic nav | build/lint/unit/critical/browser |
| 2 overview/tree | verified for primary workflows; extended tests pending | manifest-derived 6×5, source-linked mechanic hierarchy | round-trip/browser |
| 3 graph workspace | todo | contextual inspector, palette/library separation, camera/search | nested editing/browser |
| 4 data editing | todo | capability-detected weighted draws; transaction/undo | unit/export/reload |
| 5 features | todo | readable summaries; safe mapped parameters only | form/graph preservation |
| 6 evidence/debug | todo | existing evidence and actual traces only | no invented attribution |
| 7 hardening/report | todo | viewports/accessibility/regression/manual review | FINAL_QA_REPORT.md |

Next exact task: finish baseline build/lint/unit/critical + before screenshots; implement trust-state correction and responsive shell.

Slice 1: build/lint pass; 117 units pass; metric/feature-gate targeted browser checks 3/3 pass. Initial critical sweep exposed removed AI assist; fixed by wiring it to actual generation dialog and targeted control passed. Remaining sweep pending. Screenshot slice1-build-{1440,1280,1024,390}.png. Rider frontend passes unavailable (see qa/BASELINE.md); no cleanup success claimed. Next: Overview and searchable mechanic hierarchy using canonical projections.

Slice 2: new /overview (default entry), preserved project/share routes via shared initialization; source-derived searchable mechanic tree. Build/lint, 117 units, all-viewport Overview/browser, nested editing and real seeded replay pass. Screenshot review qa/SLICE_2_REVIEW.md. Next exact task: contextual graph inspector, separate library/palette, toolbar and camera controls, mobile read-only graph.
