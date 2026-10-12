# Local graph studios delivery

Reel & Paytable Studio and State Machine Designer edit the selected private project's existing GraphConfig and use the existing compiler/execution APIs. No backend/API/schema changes, new graph atoms, game-name runtime dispatch, alternate RNG or mathematical runtime were introduced. Browser projects remain private and independent; server saving remains explicit.

Implementation commit: `b6dfd81` (Designer); Studio checkpoint: `6e98807`. Branch: `feat/precision-workbench-redesign`. Base audit/fetched main: `b81e71c82cb0314d066d7134278751b85676f356`; old work and the Simulate checkpoint were preserved. No push/merge/deployment.

Live development: http://localhost:4187/model/studio and http://localhost:4187/model/machine. API: 5166; built preview: 4186. Polling HMR remains enabled. Open **My projects**, select/create/import a model, then use Studio or State machine in the navigation.

Studio delivers source, payout, payline and symbol editing, exact input and spreadsheet import, supported source/data copies, whole-graph validation/apply, complete undo and private browser recovery. Designer delivers existing graph projection, a diagram and equivalent transition table, source operations, typed actions/guards/effects, priority, states/start/terminal, variables, explicit ownership, connected new/reused definitions, nested independent copies, coupled respin rules, pan/layout, raw graph portability, seeded execution evidence and project backups.

## Acceptance evidence

“Pass” denotes the evidenced local behavior below, not certification or an exhaustive proof for arbitrary imported graphs. Full details and retained failures are in STATUS.md and the evidence directory.

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

## Required cleanup limitation

[AGENTS.md](../../../AGENTS.md) requires scoped Rider Reformat Code and Built-in Full Cleanup. The available Rider formatter repeatedly fails while opening this frontend context: `solution can't be null` (exit 3; one earlier control-edits attempt exited 6). Reformat and Full Cleanup are **not certified**. Build/lint/tests do not replace those passes. Attempts, including final changed-file attempts, remain in `m3-*-rider.log`, `m4-*-rider.log` and `m5-*-rider.log`.

Uncleaned M3/M4/M5 source files:

- `frontend/src/lib/authoring/{machineIR,machineLowering,machineSource,machineLifecycle,controlProjection,controlEdits,respinContract,viewMetadata}.ts`
- `frontend/src/pages/StateMachineDesigner.tsx`, `frontend/src/pages/state-machine-designer.css`
- `frontend/src/components/authoring/MachineForms.tsx`, `frontend/src/components/Layout.tsx`, `frontend/src/App.tsx`, `frontend/src/store/index.ts`
- `frontend/unit/{authoring-machine-lowering,authoring-machine-lifecycle,authoring-control-projection}.spec.ts`, `frontend/unit/fixtures/machineProofs.ts`
- `frontend/e2e/{machine-lowering,state-machine-designer}.spec.ts`

M2's earlier cleanup limitation and exact source attempts remain in its STATUS checkpoint. No successful cleanup is claimed for either workspace.

## Scope limits and review

Imported graphs retain their original control and unknown operations. Lossless recovery of arbitrary author intent is not claimed. New machine variables use primitive initial values; private numeric reset values are whole numbers, while existing shared fractional state is retained. Complex array/object game state can be read and manipulated through original graph operations. Library parameters remain native constants; explicit field binding specializes ordinary source expressions. Unsupported private-control ownership refuses copying rather than silently sharing it.

An imported definition copy retains its existing shared game fields; an authored machine copy has distinct private controls throughout its nested closure. Neither action grants another user access. Pending inputs live in browser storage and can hit its quota; local backups remain available. No cross-device autosave, sharing, certification, full animation debugger or production-scale capacity is claimed.

For review: select a private project; inspect every Studio tab and lower section; open State machine, choose a feature/region or transition, stage an edit and validate/apply. Test a seeded round, inspect returned state, back up/export raw graph and reimport it. Use the transition table and phone view; pan the diagram to inspect side branches. Direct Graph edits invalidate pending source-bound drafts. Creation adds a definition or inserts one explicit Library use; the existing settlement remains outside the machine.

Scope stops at these two studios. Overview, unrelated old roadmap items and hosted changes were not resumed.
