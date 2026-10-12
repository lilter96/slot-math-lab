# Precision Workbench — local implementation and QA

Date: 2026-10-12. Branch `feat/precision-workbench-redesign`, isolated worktree based on `b81e71c`. Nothing pushed, merged or deployed. Final Chromium regression passed 108/108 cases on source `b9d92a1`; screenshots were refreshed from the same final production bundle. Further broad runs were stopped under the user’s test-selection policy now recorded in AGENTS.md.

## Delivered behavior

The workbench now starts with **My projects**. Each user can keep multiple private game/model drafts, create independent template copies, search, switch, duplicate, rename and import personal models. Switching retains edits, layout and open subgraphs. The catalog includes all five migrated games, a separate Nine Lives reference model, two starters and a blank model. New workspaces do not automatically open Nine Lives. The explicit `?project=nine-lives` route and legacy inbound import links still work. Export no longer generates sharing links.

Server configurations, runs, experiment manifests and SignalR subscriptions enforce the validated owner identity. Storage identities are separate from authored model IDs, so copies of one game cannot collide. Encrypted restart and explicit legacy ownership are covered. Production supports multiple configured PBKDF2 accounts; development token issuance remains development-only. Both browser entry points verify the session before hydrating user-scoped drafts, measurement workspaces, simulation history and demo sessions. An unavailable session read displays a retry screen; a required unsigned session displays sign-in first. See [PRIVATE_WORKSPACES.md](PRIVATE_WORKSPACES.md) for account configuration, migration rules and limits.

The other redesign slices remain available:

- Model-derived Overview reads selected-game geometry, symbols, data capabilities and definitions. Nine Lives reference remains 6×5; Ancient Dragon displays its actual 5×3 geometry. Coordinate diagrams are explicitly not spins; examples are uncalibrated.
- Searchable mechanic hierarchy, nested breadcrumbs, separate starter library/primitive palette, contextual inspector, node focus, fit/selection/100% controls, per-project/mechanic cameras and actual compiler validation. Phone graph movement/connections are read only; forms remain available.
- Weighted draws preserve IDs, values and unknown fields; validate integer/nonnegative/positive-total/Int32 constraints. CSV imports map every known ID and stage a preview before application. Guarded cluster payouts update actual coin inputs and matching metadata. Guarded free-spin changes update four executable sources together. Unsupported mappings remain explicit.
- Canonical edits survive navigation/export/reload. Staged form buffers and Debug receipts belong to their project within the session. Late validation cannot edit another identical project copy. Immediate Undo refuses newer changes. Root exports from a nested definition include all definitions, tables, AST and connections. The state inspector preserves null, nested objects and boolean arrays.
- Metrics withhold stale, absent, nonfinite and unsupported proof/numbers. Actual zero stays visible. Sampled proof requires real N; N=1 has no standard-error/CI claim. Interval evidence requires valid bounds and applies only to RTP. PAR exports preserve available evidence and absent-field semantics.
- Saved Results, measurements, accounting, comparison and reproducibility retain pinned source authority. Calculation requests retry only explicit quota rejection using existing bounded Retry-After behavior. Ambiguous accepted/network failures are not retried.
- Debug executes the current graph through the existing engine, renders compatible actual board/cascade histories, highlights returned positions and exports a configuration/seed/index/hash/response receipt. Replay compares the pinned response on the current server. Other models retain raw returned state when no supported visual adapter exists. `/play` is clearly labelled **Reference demo**; `/games.html` still runs all migrated games.

## Limits

Browser drafts autosave locally within browser storage capacity; server saving is explicit. Saved server versions can be listed/opened, but automatic cross-device synchronization is not implemented. Save failures display a backup action while in-memory edits remain available. Unapplied buffers/receipts are session-local; Undo is not a global durable history. Browser namespaces do not protect against access to the same OS account or developer tools.

No sharing, collaboration, public registration, password reset or OIDC provider was added. Owner isolation does not establish large-scale throughput: the archive remains a single-process encrypted snapshot store with bounded compute concurrency. Legacy ownerless server records map only to the configured legacy operator (or anonymous local mode); production without a legacy operator leaves them unassigned. Existing anonymous browser drafts are preserved, with explicit JSON transfer into signed-in workspaces. No production migration occurred.

Physical reel-strip editing, universal graph-to-form conversion, automatic calibration, Scenario Lab and invented feature RTP anatomy are unavailable. There are no fabricated event-to-node links, RNG stream records, timings or engine-version replay guarantees; the missing generic trace contract is in [DEBUG_TRACE_REQUIREMENTS.md](DEBUG_TRACE_REQUIREMENTS.md). No calibrated RTP, performance improvement or usability-study score is claimed.

## Validation evidence

| Check | Result |
|---|---|
| .NET solution build | Pass; existing analyzer/pruning warnings remain |
| Final API suite | 140/140 passed; includes five new private-workspace tests |
| Node version for final frontend gates | 22.23.3; .NET SDK 10.0.112 |
| Final TypeScript/build/lint/unit gates | Pass on Node 22; 128/128 unit cases passed |
| Scoped integration regressions | 18/18 pass; latest private-project scenarios 7/7 pass |
| Full Chromium suite | 108/108 passed in 18.5 minutes on the final unchanged bundle (source b9d92a1) |
| Firefox/WebKit selected workflows | Prior bundle: 26/26 passed; final refresh not run under the user’s test-selection policy |
| Focused Core compiler/serialization/RNG/Nine Lives parity | 191/191 passed in 72 seconds earlier in this branch |
| Full Core suite | Unverified: earlier run interrupted after more than 40 minutes; partial passes are not a complete gate |
| Personal collection capacity probe | 30 Ancient Dragon copies restored after reload; 3,697,587 serialized characters, local Chromium observation |
| Final viewports/accessibility | 64 final captures with no document overflow; two additional failure-state captures; minimum audited text-token contrast 5.0069:1 |
| Keyboard access | Six final checks pass: skip/focus, appearance/Escape/focus return, navigation |
| Required Rider Reformat / Full Cleanup | Blocked; no required-pass success claimed |

The final API suite checks two real JWT identities; independent same-template copies; config CRUD/version denial; foreign run launch/read/evidence/cancel/witness/verification denial; real SignalR subscription/snapshot rejection; owned worker completion; private experiment children; encrypted restart; legacy ownership; and multiple production credential accounts. Browser coverage includes different games, nested drafts, independent copies, staged-buffer isolation, account changes, unsigned/unavailable boot, delayed validation and save rejection with an actual backup export.

Independent math checks include `(2×3 + 3×1 + 4×0)/9 = 1` after a weight edit, legacy imported expectation `(3×2 + 1×0)/4 = 1.5`, and seed-1 payout edits preserving the initial board while a five-cell symbol-1 hit changes 1→2 authored coins and metadata 0.05→0.1 units. A one-life purchased graph returns one board and the demo displays the returned count. Kernel/compiler/AST/PRNG/evaluation semantics were not changed; API changes implement ownership/account/storage identity only.

Earlier failed/interrupted runs remain in ignored local logs. A full API attempt hit the host's 128-instance inotify limit while failed cleanup children remained alive. Only this worktree's orphaned cleanup processes were stopped; rerunning with polling/configuration reload disabled passed 140 cases. No security/compute quota was relaxed. Earlier browser failures were corrected without deleting mathematical assertions. Final raw browser logs/JSON are ignored; concise evidence is versioned.

## Visual and interaction review

The final unchanged production bundle has 64 four-size screenshots plus save/session-failure captures. [FINAL_VISUAL_REVIEW.md](qa/FINAL_VISUAL_REVIEW.md), [viewport audit](qa/final-viewport-audit.json) and [contrast audit](qa/final-accessibility-audit.json) record actual observations. Screenshots remain under `docs/design/qa/` and are excluded from Git. The review checks all four requested sizes, project collection/empty states, game-specific geometry, graph framing, saved evidence on mobile, actual Debug hits, save errors and offline/session failure behavior. Contrast audits are targeted token checks, not complete WCAG certification.

## Remaining release gates

1. [AGENTS.md](../../AGENTS.md) requires Rider Reformat then Built-in: Full Cleanup on touched source. Standalone Rider exits 3 with `solution can't be null`; frontend files are outside the .NET solution. The pinned solution CLI encounters missing `System.Composition.AttributedModel, Version=10.0.0.12` and bounded passes time out. [RIDER_CLEANUP_REQUIRED.md](qa/RIDER_CLEANUP_REQUIRED.md) lists all affected source. Scoped supplementary whitespace/ESLint checks do not replace these passes. Run them in the owner's Rider workspace, inspect changes and repeat relevant validation before release.
2. Complete the expensive full Core suite on a suitable test host before release. Focused math/API evidence is not a substitute for that additional gate.
3. Take encrypted-storage/key and important draft backups before any hosted upgrade; preserve the legacy account mapping. Capacity, provider/provisioning and multi-instance persistence need separate deployment work. No production deployment was authorized or performed.
4. The follow-up source e6c404c adds verified-user inspector pinning and actual local pointer/wheel measurements with scoped checks in [INSPECTOR_PIN_REVIEW.md](qa/INSPECTOR_PIN_REVIEW.md). Overview’s last matching verified result is now delivered in 51fe9ad/81af758 with [scoped evidence](qa/OVERVIEW_EVIDENCE_REVIEW.md); visible-node rendering and further accessibility requirements remain implementation/acceptance work in [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md). The [design reference](../../design-system/MASTER.md) and [migration/rollback plan](MIGRATION_ROLLBACK.md) now exist; no hosted rehearsal is claimed.
5. Advanced raw graph/state edits still require math review. Compiler success proves structural validity, not certified RTP. Complete accessibility/practitioner acceptance is owner-side work.

## Local review

Use the dedicated development PostgreSQL instance on localhost:5546 (`slotmath` database/user/password, local-only). From repository root:

```sh
ASPNETCORE_ENVIRONMENT=CI ConnectionStrings__DefaultConnection='Host=localhost;Port=5546;Database=slotmath;Username=slotmath;Password=slotmath' dotnet run --project backend/SlotMath.Api --no-build --no-launch-profile --urls http://localhost:5166
```

From `frontend/`, build with Node 22 and run:

```sh
SLOT_MATH_API_URL=http://localhost:5166 npm run preview -- --port 4186 --host 127.0.0.1
```

Open `http://127.0.0.1:4186/projects`. Create Ancient Dragon and Nine Lives reference copies; edit free spins/weights, open a nested mechanic, switch projects and return. Duplicate a template and verify independent parameters. Reload/export/import a model. Review Results with actual pinned hashes/seeds/N; Debug seed 1 highlights five BONE cells and pinned replay survives later edits. Sign into different development identities in separate contexts (production must use provisioned credentials), verify empty foreign archives and denied foreign IDs. Test save failures and session/API outages without proof placeholders.

The local browser config extends the repository config with `webServer: undefined` and baseURL4186. CI commands remain `npm run test:e2e` / `npm run test:e2e:critical`. Local iterations run only new or affected tests; complete suites are reserved for integrated substantial features or explicit user requests, as required by AGENTS.md. Cross-browser checks use official Playwright Firefox/WebKit builds. This host's WebKit required temporary extracted native libraries and a launcher; repository/system dependencies were not changed. Provision standard browser dependencies on CI.


## Scoped follow-up after the comprehensive regression

Source e6c404c adds persisted inspector pinning without changing mathematical inputs. Only new/affected tests were run: initial Chromium 4/4 and Firefox/WebKit 6/6; after legacy-import and focus corrections, Chromium 2/2 and Firefox/WebKit 4/4. Existing legacy round-trip coverage also passed. These counts describe separate scoped runs, not one new complete suite. Build and scoped ESLint pass; required cleanup remains blocked. The earlier 108/128/140 comprehensive counts apply to the recorded b9d92a1 source, not a new full run on e6c404c. See [follow-up behavior, visual checks and measurements](qa/INSPECTOR_PIN_REVIEW.md).


Overview source 51fe9ad/81af758 now shows actual owner-scoped matching saved-run evidence, with failed/pending refreshes withholding stale proof. Scoped checks passed (API 3, new units 3, Chromium 3, Firefox/WebKit 4); eight real viewport captures have no overflow. No full suite was rerun. [OVERVIEW_EVIDENCE_REVIEW.md](qa/OVERVIEW_EVIDENCE_REVIEW.md) records fingerprints, uncertainty rules, failures, source scope and remaining gates.


Visible graph source 1973c4d enables native viewport filtering, with complete model/edge/expression export equality and retained offscreen search/cameras. Four initial affected Chromium checks and the corrected failed deletion scenario pass; Firefox/WebKit 4/4 pass. The actual 84-node model renders five nodes at the focused desktop viewport; no mathematical/FPS improvement is inferred. [GRAPH_VISIBILITY_REVIEW.md](qa/GRAPH_VISIBILITY_REVIEW.md) records the scope and remaining cleanup/legibility gates. No complete suite was rerun.


Source b752c03 improves working-text/field legibility, compact unsupported-board states and modal keyboard/focus behavior. Scoped Chromium 6/6 and Firefox/WebKit 4/4 pass; 20 actual viewport/AX observations have no overflow/unnamed audited controls, and the new link has 8.9245:1 contrast. [WORKBENCH_ACCESSIBILITY_REVIEW.md](qa/WORKBENCH_ACCESSIBILITY_REVIEW.md) states precise coverage and remaining acceptance limits. No complete suite was repeated or WCAG/practitioner acceptance claimed.


## Compiler navigation and destructive graph follow-up

286c257 adds authored root/nested node/edge focus and cancels obsolete compiler feedback by independent project identity/source. Scoped units 2/2, Chromium 3/3 plus added nested 1/1, Firefox/WebKit 6/6 pass; four actual viewport captures have no document overflow. See [validation review](qa/VALIDATION_NAVIGATION_REVIEW.md).

e991928 adds explicit graph/inspector/outcome deletion confirmation. Chromium initial 3/4 and failed/new follow-up 2/2; Firefox/WebKit 7/8 and only failed WebKit rerun 1/1 pass after correcting test button naming/readiness. Build/touched lint pass. See [destructive graph review](qa/DESTRUCTIVE_GRAPH_REVIEW.md). No complete suite or math-backend test was run for these frontend changes; historical full counts above remain source-specific.

The [requirement audit](REQUIREMENTS_AUDIT.md) records remaining table/keyboard/discovery/destructive-action work and release gates. The [practitioner review kit](PRACTITIONER_REVIEW_KIT.md) contains concrete tasks and scoring but no fabricated feedback. Required cleanup/full Core/human acceptance remain incomplete. This report does not claim the whole goal or release readiness is achieved.


## Data/evidence safety and model-inventory follow-up

f4cce7f adds raw-row/evidence deletion confirmation, prevents silent duplicate-field/expression overwrite and protects delayed file hashing from removed/reindexed inputs, another project, editor closure and newer uploads. Actual per-file/aggregate byte limits are enforced. Plugin registry operations now match the available API; unsupported registration/removal and missing evaluation compatibility are explicit.

Scoped Chromium 11/11 and Firefox/WebKit 18/18 passed; afterward only the strengthened embedded-byte/export test reran (Chromium 1/1, Firefox/WebKit 2/2). Build/touched ESLint pass. Sixteen four-size captures have no document/tab overflow and no plugin-panel proof badges; [review](qa/DATA_INPUT_SAFETY_REVIEW.md) records real hashing, actual empty registry, fault injection, source scope and cleanup limits. No full suites or backend/math tests ran for this frontend slice.

The [complete model inventory](MODEL_CAPABILITY_INVENTORY.md) found physical strips in Ancient Dragon, Woodland Whisper and Le Militare's initialState. The top-level-only capability detector missed them. A guarded physical reel editor is still required; raw-array editing or an 'unsupported' message does not complete the known-schema roadmap requirement. Its wrap suffix, stop distributions and conditioned/refill dependencies must round-trip together. Goal completion and release readiness remain unproven.


## Source-backed ordinary reel editor

87c95d8 delivers circular strip editing for ordinary Ancient Dragon/Woodland Whisper and both Le Militare sets, including shared refill stops. Full-period data, repeated tails and every mapped stop domain update together. Dense columns, keyboard manipulation, counts/raw probabilities, paged rows, staged validated CSV and guarded Undo are real working controls. Overview links to proven sets; mobile shows the current project.

Scoped mapping units 5/5 and CSV units 4/4 pass. Initial Chromium 8/8; post-density checks 6/7 exposed a genuine focus race, then only failed/new/affected checks 4/4 pass. Final Firefox/WebKit 12/12 pass. Actual compiler/evaluation/round endpoints prove a small independent expectation 0.5 → 1 and exact seeded restoration; all ordinary game sets Apply/Undo compile. Sixteen final captures have no document overflow and 44px phone controls. Final caption copy received build/lint/render review, no suite repeat. [Reel review](qa/REEL_EDITOR_REVIEW.md) retains scope, failures and cleanup blockers.

The conditioned bought-feature mapping is still required. Its materialized patterns/weights/stop subsets are preserved read-only until a complete guarded update exists. This does not complete that known-schema requirement. No kernel/PRNG/backend changes, full suites, publication or deployment occurred.


## Coupled conditioned reels

c4d4c9e closes the known bought-feature strip mapping: showing patterns, their exact weights, conditional subsets and ordinary/tail data rebuild together. Original recovered constraints persist as existing typed AST constants, validated against complete source, so ambiguous all-symbol outputs still export/import correctly. Unknown/customized coupling, metadata loss and unsupported integer/combination budgets fail closed; no kernel/PRNG/new-atom changes.

Affected/new units initially 8/8; changed guard/budget checks 3/3, final recovery rejection 1/1. Chromium 7/7 and only strengthened degeneration 1/1; Firefox/WebKit changed/new conditional 4/4. Actual API oracle confirms 2/3 → 1/2, raw share 1/3, all-symbol result 1, exact seeded/authored Undo and real bought-game compiler/reimport. Twelve final real before/applied/impossible captures have no document overflow/editor proof badges. [Conditional review](qa/CONDITIONED_REEL_REVIEW.md) documents AST contract, exact scope, recovery, guard limits and required cleanup. No full suite was repeated.

Remaining keyboard/async/table/rubric/human and Core/Rider gates are tracked in the requirement audit; this feature does not prove whole-goal completion or release readiness.

## Keyboard connection authoring

026e7e0 adds canonical typed handles and shared pointer/keyboard checks, independent IDs for distinct ports, a labelled keyboard dialog and project-scoped import/dialog/focus protection. Scoped units, Chromium 8+2 and Firefox/WebKit 14+4 pass; build/touched lint and four viewport captures pass. See [review and remaining race/cleanup limits](qa/KEYBOARD_CONNECTION_REVIEW.md). Whole-goal and release gates remain incomplete; no full suite was repeated.
