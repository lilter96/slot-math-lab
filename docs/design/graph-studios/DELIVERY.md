# Local graph studios delivery

Reel & Paytable Studio and State Machine Designer edit the selected private project's existing GraphConfig and use the existing compiler/execution APIs. No backend/API/schema changes, new graph atoms, game-name runtime dispatch, alternate RNG or mathematical runtime were introduced. Browser projects remain private and independent; server saving remains explicit.

Implementation commit: `b6dfd81` (Designer); Studio checkpoint: `6e98807`. Branch: `feat/precision-workbench-redesign`. Base audit/fetched main: `b81e71c82cb0314d066d7134278751b85676f356`; old work and the Simulate checkpoint were preserved. The user authorized publishing the audit corrections to [PR19](https://github.com/lilter96/slot-math-lab/pull/19) on 2026-10-13. No merge or deployment is authorized.

**Acceptance is incomplete; PR19 audit repair is active.** The prior M0–M5 completion claim was premature: CI run `38310620153` failed frontend units and backend formatting, and skipped the downstream Core/API/browser/container/smoke gates. The user's audit identifies F01–F06 in conditional source discovery, AST binding/dependencies, numeric input and parameter rewriting. F01 reproduces locally. See STATUS.md for current corrections and gates. The 2026-10-12 scoped Rider cleanup succeeded on 92 files but did not certify those missing requirements.

The [2026-10-13 publication checkpoint](PR19_PUBLICATION_CHECKPOINT.md) records the current corrections, affected checks and retained failures. It supersedes the earlier blanket completion claim; the original M5 integrated and visual acceptance gates remain open.

Live development: http://localhost:4187/model/studio and http://localhost:4187/model/machine. API: 5166; built preview: 4186. Polling HMR remains enabled. Open **My projects**, select/create/import a model, then use Studio or State machine in the navigation.

Studio delivers source, payout, payline and symbol editing, exact input and spreadsheet import, supported source/data copies, whole-graph validation/apply, complete undo and private browser recovery. Designer delivers existing graph projection, a diagram and equivalent transition table, source operations, typed actions/guards/effects, priority, states/start/terminal, variables, explicit ownership, connected new/reused definitions, nested independent copies, coupled respin rules, pan/layout, raw graph portability, seeded execution evidence and project backups.

## Acceptance evidence

The matrix below records earlier local scenarios, not final acceptance of the current PR. F01 reopens G04/R02/I03; F02/F03/F06 reopen S02/S04/I02; F04 reopens G04/P01/P02; F05 reopens P04/V02. Integrated M5 and V01–V03 remain unaccepted until the audit repair and required fresh checks complete. Full details and retained failures are in STATUS.md and the evidence directory.

| ID | Local result | Evidence |
|---|---|---|
| G01 | Pass: 65 corpus imports/roundtrips; all source nodes project unchanged | M1/M2 corpus journeys; `m5-final-authoring-units.log` |
| G02 | Pass: ordinary/bought and all 18 Le Militare mode/entry cases retained | Corpus inventory/roundtrip; M2 variant journeys; five M4 family journeys |
| G03 | Pass within compatible source patterns: separate reference/starters and opaque custom source retained | M1/M2 corpus, native opaque operations, wrapped nested-machine proofs |
| G04 | Pass: source fences, alpha-renamed helpers, private projects, definition/use copies | M1 bindings; M2 conditioned-copy; M3/M5 lifecycle/source checks |
| R01 | Pass: ordered stops/padding/domain and independent tiny-reel law | M2 reel/source lifecycle and API evidence |
| R02 | Pass: conditional WW copy/edit and impossible-condition rejection | M2 conditioned-copy and variant evidence |
| R03 | Pass: independent cell sources and linked board edits | M2 cell/source checks and TGSlots journeys |
| R04 | Pass: X7 cell/prize support, draw order and payload/stake retention | M2 prize/source checks; X7 edit/play journeys |
| R05 | Pass: selected Le Militare geometry/mode/entry retained | Corpus and siege/entry journeys; variant visual review |
| P01 | Pass: runtime/precision/wild rows and explicit cap policy | M2 payout/cap evidence |
| P02 | Pass for verified cluster/reference consumer contracts | M2 cluster/symbol/domain evidence; local unsupported states elsewhere |
| P03 | Pass: supported line geometry/count and explicit stake policy | M2 line lifecycle/spreadsheet evidence |
| P04 | Pass: exact tokens, formula refusal, XLSX/CSV/TSV and atomic staging | M2 spreadsheet/input checks |
| S01 | Pass: actual action/counter edit and execution for each family | `m4-designer-browser.log`; final X7 affected check |
| S02 | Pass: created connected graph, explicit weighted Draw and ordered guards/effects | M3 nine API proofs; M4 creation/weighted/priority journeys |
| S03 | Pass: terminal visit once, self visits, condition/modelLimit boundary | `m3-current-api.log` |
| S04 | Pass: nested/independent namespaces and opaque dependency closure | M5 lifecycle units and raw payout-36 API proof |
| S05 | Pass: opaque operation survives; direct Graph change blocks stale draft | Source units; M5 conflict/rejection checks |
| I01 | Pass: project/source transaction fences and whole-source undo | M1 transaction/ownership checks; M2/M4 apply/reload/undo |
| I02 | Pass: exported plain GraphConfig executes with no Designer runtime | M2 family export journeys; M3/M5 real API proofs; created machine reimport |
| I03 | Pass: pending inputs and immediate semantic commit recovery | M2 storage checks; Designer reload/rejection/cancel checks |
| V01 | Pass: complete Studio tabs/drawers/lower sections inspected | M2 visual manifests/reviews in STATUS.md |
| V02 | Pass: diagram/table/forms, side loops and all scroll segments inspected | M4/M5 manifests; `m5-capture-recovery-ui.log` |
| V03 | Pass: route-local styles and fresh hard navigation/reload | Studio direct-route evidence; hard-navigation Designer journeys |

## Checks and retained failures

- Final affected authoring units: **19 passed** across lowering, lifecycle, projection and view metadata. Source projection enumerates all 65 fixtures without regenerating them.
- Final failed/affected UI rerun: **2/2 passed** (`m5-capture-recovery-ui.log`): newly created feature writes its private counter to 7, preserves outer payout 111 and reimports as a raw graph; complete imported-family capture includes verified session recovery and settled camera geometry. The hidden file-input wait was corrected to wait for attachment; original failures remain retained.
- Fractional shared-state API rerun: **1/1 passed**, payout 111.5 with the fixture's explicit half-unit settlement quantum (`m5-fractional-final-api.log`). This retains the engine's declared-precision requirement.
- Initial machine API gate: **9 passed**. Additional nested opaque copy preserves payout 36; variable binding preserves 7; respin expectation 5/4 → 55/32 remains exact. Fractional shared-state proof uses an explicit half-unit settlement policy; the previous undeclared-precision rejection remains retained.
- Designer browser scenarios pass across focused runs, including creation, raw reimport, edit/reload/undo, private copies, weighted probabilities, guard priority, five families, rejection/cancel and direct-source conflicts. These are not reported as one fabricated full-suite pass. Final affected/capture outputs are named in STATUS.md.
- Production build and scoped lint pass. A full frontend lint pass also succeeded before the last scoped changes. No backend/full repository suites were run for individual edits.
- Preserve initial selector failures, parameter-ABI corrections, infrastructure connection failure, original precision rejection and auth quota timeout logs. Final checks rerun only failed, new or affected cases.

Commands used from `frontend` with Node 22 and the existing local preview/API:

```sh
npm run build
npx eslint <touched-source-and-test-files>
npx playwright test -c playwright.unit.config.ts \
  unit/authoring-machine-lowering.spec.ts unit/authoring-machine-lifecycle.spec.ts \
  unit/authoring-control-projection.spec.ts unit/authoring-view-metadata.spec.ts
npx playwright test -c .workbench-playwright.config.ts e2e/machine-lowering.spec.ts --grep <affected-cases>
npx playwright test -c .workbench-playwright.config.ts e2e/state-machine-designer.spec.ts --grep <affected-cases>
```

## Required cleanup — completed

[AGENTS.md](../../../AGENTS.md)'s two required passes now succeed. Rider 2026.1.2 opened a temporary `.slnx` containing an existing C# project for context and exactly 92 graph-studios source/test files as solution items. Only the **Studios** folder was selected; the backend project and unrelated source were excluded. **Built-in: Reformat Code** completed successfully at 23:33:26, followed by **Built-in: Full Cleanup** at 23:33:58. Owner settings and applicable EditorConfig were retained in an isolated IDE instance.

The [cleanup manifest](evidence/m5-rider-cleanup.json) records every path, before/after SHA-256, profiles and semantic diff review; the [Rider log excerpt](evidence/m5-rider-cleanup.log) contains both `Success` results and selected frontend files. Scope/profile screenshots remain local in `evidence/m5-rider-cleanup-visual/`. Earlier `m0`–`m5` CLI failure logs are historical evidence, superseded by these successful passes.

All 90 TypeScript/TSX/JavaScript files retain equivalent emitted executable AST after normalizing named imports and consolidation of duplicate imports without changing first module evaluation order. Both CSS files retain their rules, declarations and media queries; changes are formatting only. Post-cleanup checks: production build and scoped ESLint pass, **19/19** affected units pass, and **2/2** real-API browser journeys pass (Studio reel edit/reload/validate/undo and Designer action edit/play/export/reimport/undo). See `m5-cleanup-{build,lint,units,browser}.log`. No full suites were rerun.

The working procedure is documented in [RIDER_FRONTEND_CLEANUP.md](../../RIDER_FRONTEND_CLEANUP.md) and linked from AGENTS.md, so subsequent frontend work can use solution context directly instead of repeating the failing standalone formatter.

## Scope limits and review

Imported graphs retain their original control and unknown operations. Lossless recovery of arbitrary author intent is not claimed. New machine variables use primitive initial values; private numeric reset values are whole numbers, while existing shared fractional state is retained. Complex array/object game state can be read and manipulated through original graph operations. Library parameters remain native constants; explicit field binding specializes ordinary source expressions. Unsupported private-control ownership refuses copying rather than silently sharing it.

An imported definition copy retains its existing shared game fields; an authored machine copy has distinct private controls throughout its nested closure. Neither action grants another user access. Pending inputs live in browser storage and can hit its quota; local backups remain available. No cross-device autosave, sharing, certification, full animation debugger or production-scale capacity is claimed.

For review: select a private project; inspect every Studio tab and lower section; open State machine, choose a feature/region or transition, stage an edit and validate/apply. Test a seeded round, inspect returned state, back up/export raw graph and reimport it. Use the transition table and phone view; pan the diagram to inspect side branches. Direct Graph edits invalidate pending source-bound drafts. Creation adds a definition or inserts one explicit Library use; the existing settlement remains outside the machine.

Scope stops at these two studios. Overview, unrelated old roadmap items and hosted changes were not resumed.
