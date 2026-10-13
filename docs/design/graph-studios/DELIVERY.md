# Graph studios delivery and PR19 audit correction

Reel & Paytable Studio and State Machine Designer edit the selected private project's canonical GraphConfig through the existing compiler/play APIs. Users can own many private projects; games derive their geometry and capabilities from their own source. Sharing remains deferred. Drafts recover in the browser; saving a server version is explicit.

Implementation/audit source: `5f193279f4db5aef0604f8f0be04db843dfda044`, branch `feat/precision-workbench-redesign`. Baseline: `3d8ce3695f21e25f5dea618f75a5d4caeac534e3`; PR base: `6345b1bd0de39fade0432cdb005560628c2f8328`. [PR19](https://github.com/lilter96/slot-math-lab/pull/19) is authorized for publication. **M5 remains pending completed published-candidate CI.** No merge/deployment occurred.

Studio supplies Sources/Paytables/Paylines/Symbols, ordered physical strips, independent cell distributions, exact matrices/imports, domain reconciliation, source copies and atomic validate/apply. Designer supplies existing lifecycle projection, readable states and actual guards, equivalent transition table, ordered actions/effects, explicit private/shared variables, connected/reusable definitions, source conflicts and graph-only portability. Latest source edits can be undone across the two authoring views within the same owner/project browser session; reload restores graph and drafts rather than session Undo history.

## Original acceptance matrix

The original [G01–V03 contract](04_EXECUTION_AND_ACCEPTANCE.md) is unchanged. Rows below record local workflow proofs, separate from the remaining CI gate. New correction tests supplement original journeys; fixture no-op counts do not replace intentional edit evidence. Source manifests and complete outputs are in [final evidence](evidence/pr19-acceptance/integrated/final-source-manifest.json).

| ID | Local result and scope | Current proof |
|---|---|---|
| G01 | Pass: all 65 bundled source projections/no-op raw roundtrips, unchanged executable source | `graph-studios-foundation.spec.ts`, affected browser log; 38 original backend fixtures unchanged |
| G02 | Pass: 25 intentional game variants, all 18 Le Militare mode/entry combinations, selected initial state/stake retained | [Intentional variants](evidence/pr19-acceptance/integrated/intentional-variants.json), `studio-integration.spec.ts` in `undo-affected-browser.log` |
| G03 | Pass: separate reference, cluster/cascade starter weight edits, compatible custom payout 7 → 21, opaque extension retained | `pr19-acceptance.spec.ts` intentional reference/starter/custom tests |
| G04 | Pass: alpha-renamed definitions and lexical dependencies; owner/project/use identity fences and independent source copies | `pr19-authoring-audit.spec.ts`, source-index/transactions/lifecycle units, private-project/browser journeys |
| R01 | Pass: physical order/resize/padding/support and independent tiny-reel law | reel-conditioning/reel-bindings units, Studio source lifecycle + AD integrated journey |
| R02 | Pass: Woodland ordinary/bought copy/reorder; degenerate condition retained; impossible change atomic | conditioned-copy journeys + new real API exact 1/3 F01 roundtrip |
| R03 | Pass: paid/free/refill independent cell sources and linked edits | cell-binding/source units, TGSlots ordinary/bought integrated tests |
| R04 | Pass: X7 landing/cell/prize weights, payload/outcome/stake and draw order retained | X7 ordinary/bought integration and source/prize lifecycle |
| R05 | Pass: Le mode/entry paid/free strips and multipliers retain held/weighted state | all 18 Le integration tests; no base-variant substitution |
| P01 | Pass: line/scatter precision and authored/derived wild rows; cap policy explicit | pay-binding/domain units, line eligibility and integrated line edits |
| P02 | Pass for verified contracts: cluster table/minimum/eligibility/pruning/bound reconcile; additional reader/writer rejected | cluster/domain tests and actual ordinary/weighted payout journeys |
| P03 | Pass where supported: add/remove/duplicate paths update graph and explicit stake policy | Studio line lifecycle and spreadsheet tests |
| P04 | Pass: raw integer strings, zero, exact CSV/TSV/XLSX, formula/unsafe-token refusal, atomic preview | `studio-spreadsheets.spec.ts`, `pr19-machine-inputs.spec.ts` (5/5 latest), exact amount units |
| S01 | Pass: actual designer action/counter edit and execution in each of five lifecycle families | `state-machine-designer.spec.ts` five existing-family edit/Graph/round/export tests |
| S02 | Pass: connected new machine, first-true guards, effects ordered, weighted exact 5/2 and self transition | creation/weighted/priority journeys, independent `machine-lowering.spec.ts` exact/runtime proofs |
| S03 | Pass: terminal once; visit budget/condition/modelLimit distinct and boundary cases explicit | graph-only sequence 111, self/initially-terminal tests |
| S04 | Pass: two nested instances isolate controls and preserve intended shared game fields | nested/opaque copy exact payout 36; lifecycle/binding units; 17 independent audit programs |
| S05 | Pass: opaque operations retained; stale designer draft cannot overwrite direct Graph edit | custom-source/conflict journeys and active/inactive dependency units |
| I01 | Pass: owner/project/source/obsolete-object fences; cross-view Undo and project drafts retained | new transactions regressions (affected units 62/62), custom 7 → 21 → Undo browser journey |
| I02 | Pass: exported raw GraphConfig executes without Designer metadata/runtime | intentional 25-family + reference/starter/custom reimport, 17 compiler/exact/reference/optimized programs |
| I03 | Pass: immediate semantic commit and confirmed autosave reload, invalid draft recovery, explicit server versions | `graph-studios-storage.spec.ts`, `pr19-machine-inputs.spec.ts`, private-project save tests |
| V01 | Pass: final whole-page/contact and full-size review complete | all Studio tabs/source/dialog/bottom/internal scrollers at four sizes; paired manifests/contact sheets |
| V02 | Pass: final whole-page/contact and full-size review complete | diagram/table/guards/actions/variables/budget/source/create/reuse/dirty/error/applied, error-layout lower captures |
| V03 | Pass locally: fresh studio routes and varied route order retain styling and source | visual browser test 1/1 latest, direct-route and reload scenarios |

## Validation and failures retained

Backend: fixture check (38 unchanged), Release build and full formatting pass. Full solution test run: **1,389 Core + 141 API passed**, 0 failed/skipped, normal four-processor budget. Backend source is byte-identical since `930963b`. Audit regression inputs have independent expected values: **17** compiled/exact/reference/optimized cases and **17** actual API/container cases. The two scalar dynamic-weight cases deliberately use the existing reference fallback; invalid array-valued weight attempts remain failures in the evidence.

Frontend: full units **296/296** before three new Undo regressions; CI **299/299** on `6ccb1c8`. Full lint/build pass, then affected Undo units **62/62**. Latest CSS/readiness-only revision passed build, scoped lint, all **5** raw-input browser tests and the complete visual capture test. Local latest Node is 20.20.2; CI installs Node 22.

Browser reporting preserves attempts: critical first **34 pass/1 fail**, corrected private-project/pinned-replay case **1/1**. Full **345** suite first **315 pass/30 fail**, zero Playwright retries; failed-only selection **27 pass/2 fail** out of 29, then remaining two plus separately selected Golden path **3/3**. Shared Undo/source/storage/variant run subsequently **72 pass/1 fail** out of 73; visual session readiness correction **1/1**, final CSS visual recapture **1/1**. All original failures have successful affected reruns. This is not reported as one first-attempt green full run. Explicit POST quota rejections honor Retry-After and are logged; accepted requests are never replayed and production limits are unchanged.

Actual Dockerfiles build in isolated Compose project `slotmath-pr19-acceptance` (ports 5676/6476/5176/4196). Health/proxy/configs/Studio HTML smoke and 17 independent compiler/round cases pass. CI's minimal empty config is only a transport smoke: HTTP 200 with `isValid:false`, not a mathematically valid model. PR Docker/smoke jobs are excluded by the unchanged main-push condition, so local equivalents are recorded separately.

CI run `38405440415` uses source `6ccb1c88ac1a7e7641068654e752a006c86de623`, merge candidate `950a97b2142b1b0012a6f72d98fa08e85c039031`. Frontend and gitleaks passed; backend Core/downstream critical still pending at this checkpoint. Final source adds only error layout CSS and explicit 70-second visual readiness waits. Published-candidate CI must be recorded before closing M5.

Retained original failures include F01 binding loss, scalar/array weight distinctions, formatter diagnostics, undeclared fractional settlement precision, stale selectors, actual auth/computation 429s and cross-view Undo. See integrated logs and the historical [publication checkpoint](PR19_PUBLICATION_CHECKPOINT.md); earlier milestone/cleanup outputs remain historical rather than substitutes for final validation.

## Visual evidence and cleanup

Fresh baseline and final source have **446 matching capture records**, identical model/view/viewport, zoom/DPR 1: 1920×1080, 1440×900, 1280×800, 390×844. [Paired capture manifest](evidence/pr19-acceptance/paired-captures.json) records hashes. Contact sheets cover the entire captured surface, including lower/internal scroll regions. Four extra final lower-error screenshots verify message width 246–290px, height 54px, no document overflow and disabled Apply. Full PNGs remain local; selected screenshots/contact JPEGs are published for review.

Required scoped Rider Reformat then Full Cleanup completed, including subsequent changes: 39 frontend and 10 backend source files. Latest frontend profiles completed at 16:51:38/16:51:45 local time. [Source manifest](evidence/pr19-acceptance/integrated/final-source-manifest.json) records exact hashes. Existing standalone CLI failures and backend assembly-load warnings remain disclosed. No repository-wide cleanup/config/hook bypass occurred.

## Supported boundaries

Editing is admitted by source contracts, not game names. Arbitrary unresolved custom regions remain executable with local unsupported edits; this does not exclude bundled factories. Existing complex state remains native graph operations. New private-control initial values require supported primitive/whole-number syntax; fractional shared values retain their explicit settlement quantum. Dynamic weight fields accept the backend's scalar contract.

Undo is a latest-edit browser-session action bounded by owner/project/source; it is not durable multi-step history. Browser draft recovery has storage limits; server version saving is explicit. There is no sharing, cross-device autosave, capacity certification or new mathematical runtime.

Live preview: http://localhost:4187/model/studio and http://localhost:4187/model/machine, via My projects. HMR remains running. Scope ends at these studios and the audit corrections; unrelated roadmap pages are deferred.
