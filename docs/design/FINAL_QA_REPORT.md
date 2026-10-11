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
4. Advanced raw graph/state edits still require math review. Compiler success proves structural validity, not certified RTP. Complete accessibility/practitioner acceptance is owner-side work.

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
