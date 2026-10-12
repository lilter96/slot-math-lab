# Execution order and acceptance gates

## 1. Scope authority and working rules

The user's current task is to deliver Reel & Paytable Studio FIRST and State Machine Designer SECOND, both on existing graphs and across the multi-game product. Stop after these two workspaces are implemented and verified, or report a genuine blocking dependency without pretending completion.

This task supersedes earlier conversational instructions to redesign the whole application, focus only on Nine Lives, stop after its Overview, or continuously fix every newly discovered issue. It does NOT override repository AGENTS, data safety, mathematical contracts, auth boundaries or required approvals for new atoms. Other old design documents are context, not an additional execution queue.

Do not add RTP Anatomy, Scenario Lab, general AI improvements, a time-travel debugger, a new game runtime or unrelated dashboard pages. Repair an existing issue only when it blocks the two studios or risks their users' data/math. Put other findings in a short deferred list.

## 2. Reconcile the actual checkout once

Before editing, inspect the repository root, current branch/worktree, Git status, applicable AGENTS and fetched main. Record current HEAD, actual main SHA and the audited base `b81e71c82cb0314d066d7134278751b85676f356` in STATUS.md. Review changes since that snapshot in the relevant paths. Do not assume a local redesign report describes published main.

Preserve useful current work. If already in the user's assigned feature worktree, reconcile/reuse its current safe components and project storage rather than overwriting them from main. If on main, follow AGENTS to create an isolated task worktree/branch. Do not modify another agent's checkout, reset, force-checkout, stage all unknown files, clean unrelated files, auto-merge, push or deploy. Use isolated ports and writable test data.

Run a baseline build and the relevant existing authoring/graph checks. Record failures with source evidence. A pre-existing unrelated failure is not proof the new feature failed, and it is not permission to hide a regression.

## 3. Finite delivery plan

| Milestone | Coherent deliverable | Gate to proceed |
|---|---|---|
| M0: scope and baseline | Actual main/worktree reconciliation, game/variant inventory, existing command/test discovery, approved-by-task visual composition | One compact STATUS entry; known blockers isolated; no additional endless planning |
| M1: source-preserving editing | Source index, reusable verified bindings, exact-value handling, candidate transactions, undo/conflict handling and view metadata boundary | No-op round-trip corpus; at least one physical and one cell-source edit runs correctly; original data retained |
| M2: Reel & Paytable Studio | Complete Sources/Paytables/Paylines/Symbols workspace, source grouping, actual data editing, imports and apply workflow | All Studio cases below, full-page visual review, direct-route loading, real compiler/play evidence; not only one game's demo |
| M3: State-machine projection/lowering | Existing structured graph projection plus new-machine emitter using only current atoms | Small independent semantic proofs, shared-state isolation, imported no-op compatibility and real exported graph execution |
| M4: State Machine Designer | Complete diagram + state/action/guard editor + equivalent transition table; nested/reusable operations; graph reconciliation | All machine cases below, usable desktop interactions, real edit/run/export journeys and visual review |
| M5: integrated acceptance | Both workspaces interoperate with Graph/Play/Simulate/Results and current project storage | Candidate-commit full shared-runtime regression, corpus evidence, screenshots and honest final report |

Work autonomously between ordinary milestones. Complete one milestone's actual functionality rather than creating placeholders for all milestones. Do not declare completion when only M1 plumbing exists. A missing optional design skill is not a reason to stop; a mathematical capability that needs a new atom is a reason to report a specific boundary and request separate authorization.

## 4. Skill usage and visual ownership

Use the project skill `$slotmath-graph-studios` as the domain/task contract. Read the installed `frontend-design` skill before page implementation. Use `ui-ux-pro-max` only if actually present; find its real path rather than repeatedly assuming a worktree-local installation. Existing design skills advise visual execution; they do not decide graph semantics or acceptance.

Do not write “used both skills” when one was unavailable. Do not spend repeated iterations re-searching palettes. The concrete compositions in the two feature specs are the initial direction. Refine them while preserving the domain UI and readability. Compare the finished screens with their actual before screenshots at identical viewport/zoom settings.

Component style ownership is mandatory: each reusable control brings its own styling or depends only on explicitly loaded shared tokens. Directly loading a studio URL in a fresh browser must look correct without first visiting Simulate. Avoid selector leaks and separate page copies of the same Input/Select/Tabs behavior. No default white fieldsets inside dark panels or unreadable inherited dialog headings.

## 5. Acceptance matrix: fill with evidence, not intentions

For each row, store the actual graph/variant identity, test names, screenshots where relevant, candidate commit and pass/fail/blocked state. Invented evidence and copied old pass counts fail the gate.

| ID | Required case | Required proof |
|---|---|---|
| G01 | All bundled ordinary game graphs | Project → either studio → Graph → export/reimport no-op retains complete executable model and baseline results |
| G02 | Bought variants and Le Militare mode/entry cross-product | Correct selected identity, sources, initial conditions and payout denominator; no silent base-model substitution |
| G03 | Reference Nine Lives, cluster/cascade starters and compatible catalog/custom graphs | Preserve source; distinguish reference from TGSlots; unresolved custom regions remain executable |
| G04 | Renamed/duplicated definitions, expressions and projects | No game-name-based behavior; source bindings revalidate; shared versus private scope correct |
| R01 | Ancient Dragon ordered reel replacement/reorder/resize | Wrap tail, stop support and window encoding correct; independent tiny-reel expected outputs |
| R02 | Woodland bought conditional reel edit | Conditional stop probability law recomputed; impossible condition rejected atomically |
| R03 | Nine Lives paid/free/refill cell weights | Correct linked sources, independent stages, exact ratios, no fake physical reels |
| R04 | X7 cell and coin-prize edits | Payload/outcome/stake preserved; all declared draw positions remain in order |
| R05 | Le Militare paid/free strips and multipliers | Correct mode/entry/refill/held data; weighted-cell counting preserved |
| P01 | Line and scatter payouts | Runtime tables + declared precision/units agree; explicit/derived wild behavior preserved |
| P02 | Cluster payout/minimum/eligible-symbol changes | Runtime table, minimum, eligibility and baked pruning/bounds synchronized; no stale generated fragments |
| P03 | Payline geometry/add/remove where supported | Metadata and executable `pN` agree, encoding correct, stake policy explicit |
| P04 | Exact values, zero/invalid inputs, CSV/TSV/XLSX | No rounding/corrupt coercion; import preview and apply atomic; formula policy explicit |
| S01 | Each of the five lifecycle projections | At least one guard/action/counter edited through the designer; actual graph/result shows the intended change |
| S02 | New machine with conditional/weighted/self transitions | Existing primitives only; independent exact tiny fixture; actions and transition guards execute in the specified order |
| S03 | Terminal state and limit exhaustion | Terminal actions once; cap/condition/modelLimit kept distinct; off-by-one cases |
| S04 | Two instances of reusable/nested machine | Control variables isolated, intended shared game state unchanged, no accidental multiple entry roots |
| S05 | Custom graph operation and stale projection | Opaque source retained; direct graph edits not overwritten; conflict shown locally |
| I01 | Cross-view and cross-project edits | Draft/undo/apply/reload/versioning consistent; stale async result cannot apply to a new project |
| I02 | Graph-only execution portability | Exported graph runs without authoring metadata or designer runtime |
| I03 | Pending save and reload recovery | Confirmed-save reload AND immediate reload during debounce tested against explicit recovery contract |
| V01 | Whole Studio page and all subviews | Desktop readable; all drawers, empty/error/dirty/applied states inspected; no hidden unstyled sections |
| V02 | Whole State Machine page and editor states | Full diagram and transition table usable, labels/guards legible; source inspector not required for normal edits |
| V03 | Fresh direct URLs and route order | Correct styles/behavior before and after visiting other routes; no accidental CSS dependency |

A known bundled factory case cannot be marked “custom unsupported” to shortcut required coverage. For arbitrary imported custom patterns, narrowly scoped unsupported editing is acceptable if the source remains unchanged/executable and the limitation is explained. Distinguish that from unsupported rendering or whole-game exclusion.

## 6. Test strategy without the previous micro-fix loop

During transformation development, run the lowest-level tests that independently prove the affected rule. During layout work, inspect the actual page and run focused interactions. Batch browser/viewport sweeps after a coherent surface is ready; do not rebuild all browsers after every text-wrap adjustment.

At milestone boundaries, run build/lint and affected integration cases. Changes to shared model serialization, project transactions or runtime integration require the broader regression described by AGENTS and TESTING. Before final delivery, run the complete applicable regression on the actual candidate commit; scoped passes do not certify the whole product. Preserve genuine baseline failures separately and disclose anything not run.

Verified commands in reviewed main (run from repository root unless noted):

```sh
dotnet build backend/SlotMathLab.slnx
dotnet test backend/SlotMathLab.slnx
dotnet format backend/SlotMathLab.slnx --verify-no-changes
node frontend/scripts/export-game-fixtures.mjs --check
# From frontend:
npm run build
npm run lint
npm run test:unit
npm run test:e2e:critical
npm run test:e2e
```

Install/run dependencies according to current AGENTS and CI. Preserve existing API retry/resource limits. Browser tests sharing an API should not be parallelized beyond their existing isolation contract. Do not use a larger worker count to produce quota-related flakes.

Use existing independent original-game fixtures and the small cases in `frontend/src/games/fixtures.ts`. Do not regenerate original expected outputs from the same new adapter/emitter being tested. For no-op transformations, compare canonical source and seeded draws/results. For intentional source edits, use independently specified small exact expectations or deterministic assertions; same-seed equality of intentionally different mathematical programs is not required.

Mocks are allowed to exercise network error/cancel/late reply UI. They are not proof that the graph change affects a real payout. At least the representative workflows in the acceptance matrix must traverse the real compiler/play/run path.

Follow the repository's required scoped Rider passes. If a known environment failure prevents them, report the exact files and blocker. Do not disable hooks, weaken the policy, claim success, or loop forever on an unchanged failing command. ESLint/build is additional evidence, not a substitute for the required pass.

## 7. Visual and interaction gate

Desktop is the main authoring environment. Required screenshots at 1920×1080, 1440×900 and 1280×800 with CSS zoom 100% and recorded device scale; 390×844 is a recovery/navigation/responsiveness check, not a reason to reduce the desktop workspace to tiny text.

Capture before and after at the SAME viewport and model state. A fullPage screenshot alone is insufficient for virtualized grids, internal scrollers, closed drawers and tabs. Inspect all meaningful regions, all delivered tabs, expanded advanced/source panels, dropdowns, dialogs and both dirty/invalid states. Save screenshots with model/variant/view/state in the filename. Verify keyboard focus, escape/cancel, and that source links return without losing edits.

Pass criteria:

- The Studio visibly contains domain-specific strip lanes or source matrices, payout matrices and usable source navigation, not the old form with a new color.
- The State Machine screen centers on readable states, actual guard relationships and actions, not on hidden low-level atoms or generic workflow boxes.
- Primary action and current source/variant are unambiguous.
- No control looks unstyled; ordinary text is readable; numeric columns align; state/error labels are not color-only.
- No useless permanent empty inspector or screen-sized empty board placeholder.
- No unsupported “Exact”, “Saved”, “Applied” or fake RTP badge.
- Every required region is inspected, including the bottom of long pages and internal scroll containers.

Do not self-certify “9/10 premium SaaS” as evidence. Report observable checks and remaining defects. Technical tests, workflow acceptance and visual review are THREE separate gates; none substitutes for another.

## 8. Reporting and stopping rules

Maintain only one concise `docs/design/graph-studios/STATUS.md` during work. Include current milestone, actual touched modules, acceptance rows completed, remaining blockers and exact next action. Keep detailed logs/screenshots in an evidence directory, not in dozens of repetitive narrative reports. Report completion by working workflows and graph evidence, not number of commands or CSS lines.

Checkpoint coherent work on the assigned feature branch. Commit only reviewed task files; inspect the diff before staging. Never commit secrets, real user data or another agent's edits. Do not automatically push, merge main or deploy.

At completion produce a `DELIVERY.md` with the actual commit, supported models/variants, acceptance matrix results, screenshot paths, test commands/results, retained failures, source/API changes and truthful unimplemented items. Mark the task incomplete if a required editor or a bundled model case is missing. Then stop. Do not resume unrelated old roadmap items automatically.
