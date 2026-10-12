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
| 0 baseline | baseline captured; full Core incomplete | required documents and actual editing/state/API code read | QA logs/screenshots pending |
| 1 trust and shell | verified targeted; final sweep underway | suppress absent/stale proof; responsive semantic nav | build/lint/unit/critical/browser |
| 2 overview/tree | verified after interaction test adaptations | manifest-derived 6×5, source-linked mechanic hierarchy | round-trip/browser |
| 3 graph workspace | implemented; visibility verified; legibility audit pending | contextual inspector, palette/library separation, camera/search | nested editing/browser |
| 4 data editing | verified weighted draws and guarded cluster payouts; CSV preview implemented; physical strips unsupported | capability-detected weighted draws; transaction/undo | unit/export/reload |
| 5 features | verified | readable summaries; safe mapped parameters only | form/graph preservation |
| 6 evidence/debug | verified supported trace | existing evidence and actual traces only | no invented attribution |
| 7 hardening/report | in progress; remaining requirements audited | viewports/accessibility/regression/manual review | FINAL_QA_REPORT.md |
| 8 private personal projects | implemented and verified; release gates documented | per-project drafts, verified browser namespaces, owner-scoped configs/runs/experiments/SignalR, configured accounts; no sharing | 140 API / 128 unit / 108 final Chromium cases |

Next exact task: perform the full requirement-by-requirement completion audit against UX_MASTERPLAN, DESIGN_SYSTEM, CODEX_HANDOFF, named artifacts and current source/QA evidence. Record exact satisfied, conditional, missing and externally blocked requirements without reducing the scope to implemented features. In particular inspect remaining keyboard/drag alternatives, status/accessibility coverage, design review rubric/practitioner targets, required Rider cleanup and full Core release verification. Legibility/no-board/dialog corrections are delivered in b752c03 with scoped tests and 20 rendered captures; no general test suite was repeated.

Final implementation checkpoint: the unchanged final production bundle (source b9d92a1) passed 108/108 Chromium cases in 18.5 minutes. Build/lint/128 units, API 140, scoped 18/18 and final personal-project 7/7 passed. Final capture has 64 four-size viewport checks with no overflow, five actual hit cells, zero offline proof badges and text-token contrast ≥5.0069:1, plus save/session-failure captures. Final reports record the evidence and limits. The previous Firefox/WebKit bundle passed 26/26; the final refresh was not repeated under the user’s updated test policy. Rider passes and the expensive full Core release gate remain explicitly blocked/incomplete. No publication or deployment occurred.

Slice 1: build/lint pass; 117 units pass; metric/feature-gate targeted browser checks 3/3 pass. Initial critical sweep exposed removed AI assist; fixed by wiring it to actual generation dialog and targeted control passed. Remaining sweep pending. Screenshot slice1-build-{1440,1280,1024,390}.png. Rider frontend passes unavailable (see qa/BASELINE.md); no cleanup success claimed. Next: Overview and searchable mechanic hierarchy using canonical projections.

Slice 2: new /overview (default entry), preserved project/share routes via shared initialization; source-derived searchable mechanic tree. Build/lint, 117 units, all-viewport Overview/browser, nested editing and real seeded replay pass. Screenshot review qa/SLICE_2_REVIEW.md. Next exact task: contextual graph inspector, separate library/palette, toolbar and camera controls, mobile read-only graph.

Extended slice-2 regression: 11/14 pass; three starter tests assumed all definitions/extraction fields always visible. Updated tests to use searchable explorer and extraction disclosure. No dropped assertions. Phase-3 gate includes those workflows.

Slice 3: build/lint, 117 units pass; feature gates/metric checks pass. Graph screenshots captured at all four requested sizes (slice3-graph-*), selected inspector at 1440. Extended tests found assumptions about example backup: toolbar now preserves the earliest draft, so tests now start with a real coin share-link instead of replacing an auto-created Nine Lives draft first. Inspector tabs remain accessible when drawer is open. A later API-only test received 502 because local API stopped gracefully; restarted isolated service and rerunning affected workflows. No regression is waived.

Phase 3 final: clean build/lint, 117 units, feature/metric tests and 9/9 graph/library/Overview interactions pass on restarted API. Source-preserving payout test passes through actual /play/round. Next exact task: weighted-data transactions, canonical round-trip tests, export/reload/browser proof.

Slice 4: 119 units and 3 workbench browser tests pass; actual API independently proves changed coin expectation 1. Undo bug from JSON key ordering fixed using canonical comparison. Screenshots slice4-data-*.png; qa/SLICE_4_REVIEW.md. Next: safe feature form with complete known source mapping, then trace/evidence and final comprehensive regression.

Slice 5: build/lint, 121 units, 4/4 workbench browser tests pass. Guarded free-spin form updates all executable count sources together; actual purchased round with one life returns one board. Screenshots slice5-features-{1440,1280,1024,390}.png. No universal form conversion or bonus-price inference. Next exact task: current-model Debug round, source-backed board/cascade trace and replay verification, then final evidence and viewport audit.

Slice 6: build/lint, 123 units, 7 browser checks pass. Debug executes the canonical current graph, renders returned boards/cascade hit positions, downloads pinned receipts and verifies replay against full actual response even after draft edits. Play/Debug remain fail-closed behind play flag. No node IDs or version pinning exist in API; no fabricated mapping or cross-version replay assurance. Actual screenshots slice6-debug-*.png. Existing saved-run evidence remains authoritative. Next: guarded cluster payout mapping (metadata alone is not executable), final export/root/provenance audit and comprehensive checks.

Slice 4b: executable cluster payout editor added after proving its flat coin array, both authored lookup expressions and metadata unit mapping. Build/lint, 125 units, 6/6 workbench tests pass. Seed 1 independently demonstrates the same initial board and five-cell symbol-1 hit paying 1 → 2 coins after edit; metadata changes 0.05 → 0.1 model units. Undo/inverse preserves all other graph data. Screenshots slice4b-payouts-*.png. Next: export the root rather than open subgraph, per-field PAR evidence, keyboard/appearance polish and full browser regressions.

Hardening: fixed full-root JSON/server exports from nested context and absent-field PAR proof; CSV preview validates IDs/weights before staging; free-spin drafts persist across modes; compiler validation and keyboard nav added; Appearance moved out of the content overlay. First broad critical sweep encountered actual 429 quota rejection in new edits and feature fetch (captured hardening-first-critical.log); first sweep completed with 20 pass / 3 quota failures before the latest rebuild. Reuse existing bounded Retry-After helper for explicit rejected calculations and feature GET. No quota bypass, no ambiguous mutation retry. Dedicated Core full run stopped after over 40 minutes without completion; 191 focused compiler/serialization/RNG/Nine Lives checks pass in 72 seconds. API 135 pass. Full Core remains unverified.

Final hardening checkpoint: latest build/lint/128 units and eight corrected Chromium checks pass. 48 fresh viewport captures have zero document overflow. Supporting text contrast minimum is now 5.0069:1; actual offline proof count is zero. Keyboard skip-to-main and Appearance Escape/focus return pass. First cross-browser run: 23 pass / 3 Firefox timing failures; all nine new workbench workflows pass in both browsers. Tests now control pending responses explicitly; final rerun pending. Source review also corrected unsupported sink numeric placeholders and proof refresh dependencies; final build/tests must cover this last change. Rider remains unsupported; a 180-second Full Cleanup attempt timed out with composition errors. FINAL_QA_REPORT.md is drafted with pending final browser results and explicit release gates.

Stable final build checkpoint: build/lint/128 units pass after the sink correction. Firefox/WebKit 26/26 pass in 1.3 minutes, including deterministic pending evidence checks and all real edit/round/replay/round-trip workflows. 52 refreshed captures have zero document overflow and five actual Debug hit cells; contrast minimum 5.0069:1. Full Chromium now runs on unchanged dist; no rebuild while it runs. Post-cross Reformat exited 3; three-file Full Cleanup timed out at 60 seconds with the same composition errors. Cleanup is still a release blocker.

Final source-preservation correction: raw state editor now keeps null fields and boolean/null/nested array values read only; numeric-array row additions retain numeric type. A browser round-trip test checks all preserved state plus numeric append. Interrupted the initial stable-build sweep deliberately after 27 passes so the complete rerun covers this last correction; no interruption is treated as a product failure or full pass. Next: scoped cleanup attempts, build/lint/units, this new test, 28 cross-browser cases, full 101-case Chromium suite.

## User steering — private multi-project workspaces

The user clarified that each user may own many games/models and personal materials. Sharing and collaborative access are explicitly out of scope now. Implement a personal project list, preserve edits when switching, create personal copies of templates, and enforce ownership of configurations, runs, experiments and realtime subscriptions. Do not assume Nine Lives is the user's only model. Remove share-link creation; preserve legacy inbound import links and personal JSON export. Namespace browser drafts only after verifying the server identity.

Previous Chromium full run was deliberately interrupted after 27 passes to include the state-type guard and this scope. It is not a complete passing gate. Next: implement ownership and personal projects, validate two-user isolation, then run the complete stable build suite and update final visual/QA evidence.

Private-workspace first checks: 15/15 Chromium browser tests passed (three new project/account isolation scenarios plus ten workbench and two provenance cases); .NET ownership checks initially 2/2, then expanded to 4/4 including real SignalR access rejection, experiment children and encrypted restart/legacy ownership. Final guards/session namespaces were added afterward and require a fresh stable build. Rider backend Reformat/Full Cleanup again could not complete (missing Roslyn System.Composition.AttributedModel 10.0.0.12; bounded pass timed out). No cleanup success is claimed.

The first final API suite encountered the host's 128-instance inotify limit while failed Rider CLI children remained alive. Only cleanup processes belonging to this worktree were terminated. Preserve that failed log as environmental evidence; rerun with configuration reload disabled and polling file watchers. No math/security quota was relaxed. Node 22.23.3 is now available in npm's temporary cache and the final frontend build/lint/128 unit checks passed on Node 22.

A 30-project browser capacity probe restored all 30 Ancient Dragon copies (3,697,587 serialized characters). This is a local observation, not an unlimited-capacity claim. Added explicit save-failure feedback with a JSON/server backup path; this last low-impact slice needs its final frontend build and browser coverage.

Current source is now frozen for final verification after the save-failure banner and empty-project header. The first comprehensive run is still on the preceding built dist (source edits have not been rebuilt during it). Once it ends, rebuild once and validate the complete final bundle; screenshots must come from that bundle.

Final-regression follow-up: first 105-case run exposed two integration issues: opening a pinned graph after clearing browser data did not retain the source run for collection-plan reuse; bootstrap treated explicit HTTP 429 as an unavailable identity. Fixed pinned-graph opening as a separate private project with run context (preserving any other active monitoring), and shared identity bootstrap now honors bounded Retry-After without reading stores until identity is verified. Added a real 429 timing browser check. Final bundle now has 107 browser cases. Required Rider standalone attempt still exits 3; CLI Full Cleanup remains blocked as documented. Rerun final build/lint/units and scoped regressions after the old run ends, then all 107 cases on an unchanged production bundle.

Final personal-project integration: shared config query invalidation now refreshes server saves immediately; a seventh private browser case saves a real model then opens it as a distinct copy. 7/7 pass. Private run links are labelled as owner-only bookmarks. Actual mobile review found long authored symbol labels colliding; scoped wrapping corrected them without renaming symbols. Final build passes after this stylesheet change. Captures use a real verified QA owner; a 100-round coin calculation supplies actual saved evidence. Inspector overlays at tablet/phone sizes are captured as overlays; canvas fitting is performed when controls are exposed. No forced pointer clicks or fake evidence are used.


## User steering — test frequency

AGENTS.md now prohibits complete suites on every iteration. Ordinary changes run only affected or new tests; full suites run after an integrated substantial feature or an explicit user request. After a failed full run, fixes receive failed/affected checks rather than an automatic full rerun. Documentation-only changes require no tests. No additional broad suite was launched after this instruction.


## Documentation alignment and renewed completion audit

Documentation follow-up (2026-10-12): the workspace-level copies of UX_MASTERPLAN, DESIGN_SYSTEM and CODEX_HANDOFF are synchronized with this worktree's private multi-project scope; their index points to the maintained repository documents. AGENTS.md now also makes this product scope mandatory, alongside the existing affected/new-test policy. The frontend README describes the actual project library, identity boundaries and development workflow instead of the Vite starter template. This follow-up changes documentation only; no tests were launched and no new implementation verification is claimed.

The 2026-10-12 user instruction requires the documentation to reflect many private projects per user. Updated UX_MASTERPLAN, DESIGN_SYSTEM, CODEX_HANDOFF, PRD, README and TESTING; added the named `design-system/MASTER.md` artifact and a migration/rollback plan. Removed single-game entry assumptions and current sharing requirements. Historical QA/slice observations remain historical evidence; they are not rewritten as present-day product requirements. No source changes or tests were made for this documentation slice.

This is progress toward the active goal, not full completion. Current audit findings:

| Requirement | Current evidence / next work |
|---|---|
| Many projects per verified user, no sharing | Projects/store/browser identity and owner-scoped API; 7 private browser workflows and 5 private API test cases in the recorded 108/140 passing suites |
| Model-derived facts, safe editing, actual evidence and Debug | Existing source adapters and recorded unit/API/browser/viewport evidence; no new pass implied by documentation edits |
| Design reference artifact | `design-system/MASTER.md` now exists and describes actual CSS tokens, current scope and pending design acceptance |
| Migration/rollback plan | `MIGRATION_ROLLBACK.md` now exists, grounded in encrypted snapshot and browser storage code; hosted rehearsal/deployment not performed |
| Inspector pin with persisted preference | Implemented in e6c404c; three new scenarios cover pin/reload/projects, user isolation and legacy import; affected Chromium/Firefox/WebKit checks pass; see qa/INSPECTOR_PIN_REVIEW.md |
| Graph interaction performance target and Overview readiness | Real pointer/wheel samples recorded on root and 84-node definition; ~60 RAF Hz and Overview 217–309 ms locally. This is active-input evidence, not compositor FPS certification; see qa/interaction-performance-audit.json |
| Keyboard/reduced-motion/accessibility acceptance | Six targeted final checks and token contrast verified; further inspector/tree/graph and reduced-motion interactions need scoped evidence |
| Current Firefox/WebKit private-project behavior | Pin preference/project-switch/import workflows now have scoped cross-browser evidence. Other private workflows retain Chromium evidence; no new complete cross-browser claim |
| Rider cleanup | Required passes remain blocked by tooling; source list and actual failure evidence retained |
| Full Core release verification | Earlier full run incomplete; focused 191 passed. This remains a release gate, not a reason to run unrelated suites after docs changes |
| Practitioner/user acceptance and staged release | Not proven; local work must not invent tester feedback or deploy without authorization |

Trace event-to-node attribution and per-feature RTP anatomy remain conditional on a generic source contract; the required separate specification is `DEBUG_TRACE_REQUIREMENTS.md`. Physical strips are conditional on an actual selected-model schema; unsupported states do not invent one. Scenario Lab is explicitly later in the roadmap, sharing explicitly deferred by the user. These are not implemented-feature claims.


Inspector follow-up: source e6c404c implements user-scoped pinning and predictable focus. Initial Chromium 4/4 and Firefox/WebKit 6/6 passed; an additional legacy import guard and unpin focus correction received only affected checks (final Chromium 2/2, Firefox/WebKit 4/4). Existing legacy edge/definition regression passed. Build and touched ESLint passed; no general unit/API/Core/browser suite was repeated. Four pin viewports plus keyboard capture have no overflow and visible Close. Desktop/phone images were inspected. Real pointer/wheel performance is recorded on this source. Required Rider Reformat still exits 3 and Full Cleanup remains unavailable; this does not establish release readiness.

Source inspection found remaining requirements: Overview always renders a general no-evidence message even when a matching owned saved run exists; implement matching verified evidence next. React Flow currently does not enable onlyRenderVisibleElements; address this explicit performance requirement with graph interaction coverage. The new empty pinned inspector still uses the earlier 11px add-node hint; include it in the legibility/accessibility follow-up rather than claiming every design requirement is complete.


Overview evidence follow-up: source 51fe9ad adds an owner-scoped, read-only matching-model lookup and a source-verified saved-run panel. Existing canonical serializer/fingerprint semantics and mathematical execution are unchanged. Three API, three new unit, three Chromium and four Firefox/WebKit checks pass; no general suite was repeated. OpenAPI/types were regenerated. Eight four-size saved/error captures use an actual owned 100-round result, no overflow and zero failed-state proof badges. Desktop/phone review led to clearer network/timeout recovery in 81af758, validated by rebuilt rendering rather than repeated suites. See qa/OVERVIEW_EVIDENCE_REVIEW.md. The previous absence of Overview saved results is resolved; visible-node rendering and legibility remain the next concrete work.


Visible graph follow-up: source 1973c4d enables native viewport filtering. Four initial affected Chromium cases passed; a deletion test incorrectly stopped at the last visible DOM node and was corrected to enumerate actual model IDs without weakening empty-model assertions. Only that failed case was rerun and passed. Firefox/WebKit 4/4 scoped cases pass. Actual full-fit versus focus observation is 84→5 rendered nodes with all 84 still available in the model; exported JSON equality, offscreen focus, Fit selection/Reset view and nested camera restoration are checked. Five images/four focused viewport observations have no overflow. No mathematical source or broad test suite changed/ran. Legibility and accessibility remain the next work.


Legibility/access follow-up: b752c03 raises audited working labels/hints/tags to 12px, labels primitive inspector controls, makes the no-board Overview compact and gives the charcoal data dialog explicit focus restoration and wrapping controls. Chromium 6/6 and Firefox/WebKit 4/4 pass. Final 20 captures have zero document and modal-tab overflow, zero unnamed audited exposed AX controls and no audited CSS sizes below 12px; new link contrast 8.9245:1. Desktop/mobile review corrected actual link/tab issues without rerunning suites for CSS-only fixes. These are scoped observations, not complete WCAG or human acceptance. Required cleanup remains unresolved. The next task is the comprehensive requirement audit, rather than assuming all remaining gaps are only external.
