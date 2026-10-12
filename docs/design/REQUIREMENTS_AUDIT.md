# Requirement completion audit

2026-10-12. Audited against UX_MASTERPLAN v1.1, DESIGN_SYSTEM, CODEX_HANDOFF, private-workspace and migration contracts, repository rules and current source/evidence. This is a continuation checklist, not a completion certificate. Historical full-suite evidence belongs to its recorded source; later scoped checks do not imply every suite passed on the current tree.

Statuses: **supported** means inspected source and scoped evidence support the stated behavior; **partial** means the requirement's full scope is not yet proven; **conditional** means the plan explicitly depends on absent model/backend capability; **pending** means work or evidence remains. All release gates remain independently required.

## Product and discovery

| Requirement | Evidence / disposition |
|---|---|
| §0 many users, many private games/models/projects; empty personal entry | Supported: Projects, projectFiles, verified browser identity and owner-filtered API; private-projects browser tests and PrivateWorkspaceTests. No sharing; Nine Lives is a fixture. |
| §0 independent copies, canonical drafts/nested context, owner-only archives/realtime | Supported by the recorded private integration checks, including same-template copies, account changes, foreign IDs, SignalR denial and saved-version copies. No unlimited-capacity or cross-device autosync claim. |
| §1 route/component/state/feature inventory and before captures | Baseline and slice reviews exist; before-build captures cover four sizes. MODEL_CAPABILITY_INVENTORY.md and raw source capture now cover all eight bundled factories, including actual physical-strip state and coupled stop domains. |
| §1 replay abilities, persistence/migration hazards, mobile authoring | Supported documentation: roundTrace adapters, PRIVATE_WORKSPACES, MIGRATION_ROLLBACK; mobile movement/connections explicitly read-only. Cross-version engine replay certification is absent. |
| §1 ask three relevant practitioners | Pending actual feedback; PRACTITIONER_REVIEW_KIT.md now provides concrete tasks and scoring. Do not invent results or contact others without authorization. |
| §2 4/5 testers locate editing/evaluation in ≤30 seconds | Pending human evidence; local browser timing cannot prove this target. |
| §2 canvas ≥65%, Overview ≤2s, interaction ≥50fps | Local measured canvas 81.3%; Overview 217–309ms; input-associated RAF ~60Hz on root/84-node graph. Partial: RAF is not compositor frame certification across devices. |
| §2 model round trips and seeded parity | Scoped Core 191 passed; UI round-trip checks exist. Partial release gate: full Core run incomplete. |
| §2 viewport/AA-oriented accessibility | Four-size captures, token contrast and scoped AX/keyboard checks support audited views. Partial: not full WCAG certification, complete keyboard matrix or human acceptance. |

## Navigation and surfaces

| Requirement | Evidence / disposition |
|---|---|
| §§3–6 charcoal/Plex instrument identity, dense working surfaces, responsive shell | Existing tokens, Layout and actual visual reviews. No wholesale framework replacement or invented artwork. Partial: record explicit DESIGN_SYSTEM §I rubric scores for each major view. |
| §4 project switcher/status/actions/account/help, collapsible navigation, old routes/query links | Source Layout/App/bootstrap; private, feature flag and legacy route checks. Preserve owner-only run deep links. |
| §4 command palette OR searchable tree | Searchable MechanicTree and node picker implemented. DESIGN_SYSTEM §F names command palette explicitly: audit intended command coverage/keyboard alternative before calling all keyboard requirements complete. |
| §5 wireframes/shared inventory | Behavioral requirements, not a request for duplicate components or physical reels on every graph. Existing components may fulfill semantics. Resizability is qualified 'if practical'; record decision and evidence. |
| §7.1 model-derived Overview facts, summaries, useful actions, matching last run | modelProjection and OverviewEvidence; own canonical-fingerprint lookup, source verification, actual seed/N/date/original ID. Scoped API/unit/browser evidence and saved/error captures. |
| §7.2 hierarchy, separate primitive palette/templates, pinning, fit/100%, breadcrumb/camera | Tree/palette/CanvasToolbar/SlotCanvas; pin and graph-visibility reviews, offscreen navigation/export equality. |
| §7.2 node/edge compiler focus, source preservation | 286c257; source resolver units, real root/nested compiler diagnostics and four-size focus captures. Aborts obsolete validation on project/source changes. |
| §7.3 weighted table/probability preview/CSV transaction/Undo | WeightedDrawEditor/modelCommands/weightCsv, corresponding units and workbench checks. Supported guards preserve unspecified fields and project-scoped buffers. |
| §7.3 physical strips add/remove/reorder/index/probabilities | **Partial:** 87c95d8 implements real columns/index/symbol/count/probability/keyboard/CSV/transaction/Undo mappings for all three ordinary template families and both Le Militare sets. Scoped units/API/browser/captures prove the stated scope. c4d4c9e now supplies coupled showing-pattern/conditional-stop updates, preserved typed source-rule constants, compiler/analytical/seeded/Undo/reimport evidence. Unknown/ambiguous/customized mappings remain explicit; broader table/keyboard review is still open. |
| §7.4 paytable/cluster mapping, precision/warnings/diff before apply | ClusterPayoutEditor and guarded payoutCommands, actual paid-round regression. No universal arbitrary payline editor or full RTP calculation each keystroke. Audit sticky labels/raw-value copying/light-evaluation states across supported tables. |
| §7.5 feature-readable summary, bounded mapped editor, graph escape | Features and featureCommands, free-spin graph/form tests. Unknown mappings remain graph-editable. |
| §7.6 methods, N/CI/budgets, target vs actual, max-win provenance, comparison | Existing evidence validators/Results/measurement source, metric-integrity and results tests. No invented per-feature attribution. |
| §7.7 Stage A current-model Debug, actual events/state, pinned receipt replay | roundTrace, Debug, slice-6 review and seeded checks. Unsupported board history shows raw returned state. Reference demo is labelled. |
| §7.7 Stage B source trace/RNG/engine pinning | Conditional, separate generic RFC DEBUG_TRACE_REQUIREMENTS exists. Current API has no source-node event contract; no fabricated bridge or cross-version replay assurance. |
| §7.8 backup/PAR/version/provenance | Existing Export/API and round-trip/evidence tests; no share-link generation. |

## Architecture, phases and deliverables

| Requirement | Evidence / disposition |
|---|---|
| §8 one canonical source, IDs/AST/unknown data/edge ports/plugins/mechanics | Existing serializers, model projection/commands, nested composition tests. No new atoms, game-specific kernel dispatch or math/PRNG changes in presentation slices. |
| §8 obsolete validation cancellation/debounce and project identity | Guarded source editors and 286c257 compiler request abort; changing source/project clears feedback. f4cce7f also guards file hashing by surviving input/project/context/latest task. 87c95d8 guards strip CSV/validation/focus with direct delayed-work/abort evidence. Audit remaining asynchronous surfaces individually. |
| §8 visible-node rendering, avoid chart recomputation/mega-components | Native ReactFlow visibility enabled; 84→5 rendered while full model/export preserved. Targeted modules extracted. Broad performance budget remains partial as above. |
| Phase 0 named design-system/MASTER.md | Exists with token aliases/current scope; baseline frontend/API results recorded, full Core unresolved. All-template source inventory now exists; its physical-strip findings add concrete unfinished editor work. |
| Phases 1–5 preserved routes/state/flags, graph/form/data changes | Slice reviews and source-specific tests support implemented surfaces; confirm all drag alternatives and destructive interactions before acceptance. |
| Phase 6 anatomy/overlap methodology; separate trace RFC; Scenario Lab later | Existing measurement/accounting conventions and diagnostics retained; no data supports universal RTP anatomy. RFC delivered, Scenario Lab explicitly deferred. |
| Phase 7 cross-browser, accessibility, performance, flags, compatibility, migration/rollback | Scoped Chromium/Firefox/WebKit checks and plans exist. Partial: required cleanup, full Core, practitioner acceptance, explicit rubric and further interaction audit. No hosted rollout or rollback rehearsal performed. |
| Coherent local commits, no push/deploy; persistent status/final QA | Commit history, IMPLEMENTATION_STATUS and FINAL_QA_REPORT exist. Historical phase approval pauses superseded by autorun; release safeguards remain. |
| Commands/tool installation | Existing stack/skills retained; commands are available entry points, not per-iteration obligations. Affected/new testing policy overrides broader old instructions. |

## Explicit acceptance scenarios (§10)

| # | Scope / evidence |
|---|---|
| 1 | Personal entry, selected-model geometry/no false proof: private-projects, workbench, metric-integrity, Overview review. |
| 2 | Build/simulate/results/export/gated play and legacy links: existing route/golden/feature tests; historical full 108 applies to b9d92a1. |
| 3 | Search/nested open/back/edit: workbench and subgraph-library; canonical export checks. |
| 4 | Hidden/selected/pinned inspector and usable fit: context-inspector and graph-visibility. |
| 5 | Weight edit/validation/export/reload/unknown preservation: model-command units and real workbench flows. |
| 6 | Pending/sampled/exact/bounded/failure provenance: metric-integrity/Results and Overview evidence; exact labels require payload. |
| 7 | Feature flags/auth gates: feature-flags and owner isolation tests. |
| 8 | Different games/same-template projects, import/reload/nested edits: private-projects and context-inspector. |
| 9 | Focus/keyboard tree/ESC/dialog restoration/reduced motion: scoped workbench/accessibility checks; full graph/data-table command matrix remains partial. |
| 10 | Four specified sizes and explicit phone fallback: visual/interaction audits; continue review for newly changed surfaces. |
| 11 | Two-user CRUD/run/experiment/realtime/browser isolation: PrivateWorkspaceTests and private-projects evidence. |
| 12 | Failed autosave backup/explicit owner save/separate draft/no sharing: private integration and failure captures. |

## Design system A–I

A/B/C: selected-model identity, token aliases, contrast and audited working text ≥12px supported by actual captures. D: shell/drawer geometry and controls supported; the new strip table has direct sticky-header/index, keyboard and scoped paging evidence; compact options and remaining table/graph keyboard coverage still need audit. E: badges/metric/source rules supported; strip symbol IDs have copy controls and a selectable raw fallback. Other raw numeric copy actions need audit. F: reduced motion/dialog focus supported; graph/primitive and data-row/evidence-input destructive confirmations now have scoped evidence. f4cce7f removes pretend plugin deletion; actual API has no removal capability, so registry controls are explicitly read-only (DATA_INPUT_SAFETY_REVIEW). G: four-size evidence and explicit limited mobile graph supported. H: event-to-source bridge deferred only where backend lacks mapping, as explicitly allowed. I: minimum 14/16 scored visual acceptance and practitioner review **not yet proven**.

## Next work, in order

1. Finish asynchronous scope gaps: 026e7e0 adds keyboard connection authoring, canonical typed handles, scoped dialog/import protection and cancellable project/path/selection-guarded focus. Scoped root/nested navigation and import tests pass; a controlled delayed-frame race check still needs direct evidence. 7191f0c guards generic data execution with actual delayed source-change-and-restore, same-model/different-project and unmount checks; nested-path race still needs direct evidence. Remaining weighted editor/file requests need source/identity guards. See qa/KEYBOARD_CONNECTION_REVIEW.md. Ordinary/shared-refill/conditional reel mappings have scoped evidence.
2. Complete remaining table contract review (sticky/compact/raw-value/keyboard behavior), then produce scored agent visual notes and gather actual practitioner acceptance. Preserve source-specific verification and no invented human feedback.
3. Produce a scored visual review and concrete practitioner review kit. Gather human acceptance without fabricated feedback.
4. Resolve required Rider passes and the full Core release gate when integration is stable; keep failure/affected reruns scoped. No release readiness claim until every required gate passes.
