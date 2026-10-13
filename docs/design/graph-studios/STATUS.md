# Graph studios status — 2026-10-13

Current scope: the two graph-native studios plus the supplied PR19 audit's F01–F06 and actual M5 gates. [PR19](https://github.com/lilter96/slot-math-lab/pull/19) is open; the user authorized push/PR publication. Merge and deployment remain outside this task.

Source candidate: `5f193279f4db5aef0604f8f0be04db843dfda044`, branch `feat/precision-workbench-redesign`. Audit baseline: `3d8ce3695f21e25f5dea618f75a5d4caeac534e3`; fetched main/base: `6345b1bd0de39fade0432cdb005560628c2f8328`; original design audit: `b81e71c82cb0314d066d7134278751b85676f356`. The [original acceptance contract](04_EXECUTION_AND_ACCEPTANCE.md) remains unchanged (Git blob `83d3ad8b4f0dca80eebdd501a1fff6681f643fa7`).

M0–M4 functionality is implemented. **M5 remains open only for completed published-candidate CI.** Earlier blanket completion claims and historical failed CLI cleanup entries are superseded by the actual checks below. Detailed historical checkpoints remain in Git history and retained evidence, rather than an additional roadmap.

| Correction | Current evidence |
|---|---|
| F01 | Explicit condition + complete degenerate shape retains editable all-one sources; real API mixed → all-one → raw reimport → mixed proves independent exact `1/3`; impossible edits retain source. |
| F02/F03 | Shared lexical AST binding covers implicit collection names, shadowing, initializer/body scope and supported namespace aliases. Independent compiler/exact/reference/optimized programs agree; measurement namespaces remain distinct. |
| F04 | Active source inventory covers collection keys, state/board writes, inline ports, ExitReason and named expression closure. Additional readers/writers block unsafe domain edits; compatible custom and unused templates remain admitted. |
| F05 | Raw integer drafts reject fractional/empty/unsafe tokens before conversion; intentional zero compiles and pays 3. Browser draft/export/reload cases pass 5/5. Full-size review found cramped error text; final CSS correction gives it 246–290px at the four required viewports. |
| F06 | Original literal amount=7 and bound private counter=7 both compile and execute, including supported direct refs, inline and transitive uses. Scalar dynamic weight uses the existing reference fallback; no false native-plan claim. |
| Cross-view Undo | Latest edit survives Studio → Graph → Designer → Studio; session history is bounded to 32 owner/project entries and fenced by source/current edit identity. Three regressions plus affected units pass 62/62. Reload restores graph/drafts, not Undo history. |

Touched modules and hashes: [final source manifest](evidence/pr19-acceptance/integrated/final-source-manifest.json). No new kernel atoms, evaluator, RNG or game-specific runtime dispatch. The bounded C# changes are required formatter-compatible cleanup and independent regression tests.

Validation completed:

- 38 original fixtures unchanged; Release solution build and full `dotnet format` pass.
- Full .NET solution tests: **1,389 Core + 141 API passed**, 0 failed/skipped, normal four-processor budget. Backend source remains identical to that run at `930963b`.
- Full frontend units: **296/296** at `930963b`; CI **299/299** at `6ccb1c8`, including the three new Undo regressions. Full lint/build pass; one existing WeightedDrawEditor hook warning remains.
- Critical local browsers: first **34/35**, then the corrected private-project deletion/pinned replay case **1/1**.
- Full browsers: **315 passed / 30 failed** out of 345, no Playwright retries. Failed-only selection: **27/29 passed**; last two plus the separately selected nested Golden path **3/3 passed**. Preserve the first failures; these results are not a fabricated first-attempt green full run.
- Current Undo/source/storage/variants affected run: **72/73 passed**; the failed visual session wait subsequently passed **1/1**. Complete **25/25 intentional game variants**, plus separate reference, two starters and compatible custom graph; all 18 Le Militare combinations are exercised. Final CSS-only change: numeric **5/5**, visual **1/1**, build/scoped lint pass.
- Required Rider Reformat then Full Cleanup completed on all **39 frontend + 10 backend** touched source files. Latest frontend passes: 16:51:38/16:51:45 local time. Assembly-load warnings in backend CLI logs are retained; unsupported standalone frontend CLI failures are historical, not claimed successes.
- Both real Dockerfiles build. Isolated Compose API 5176 / frontend 4196 passes health/proxy/configs/SPA smoke and **17 independent compiler/round proofs**. PR CI excludes Docker/smoke by its existing main-push condition; local equivalents are recorded separately.

Fresh visual evidence: **446 matched before/after captures** at 1920×1080, 1440×900, 1280×800 and 390×844, zoom/DPR 1; same models and filenames, no document overflow or page errors. Covers every delivered tab, internal vertical/horizontal scroller, source drawer/JSON, transition table, create/reuse dialogs, dirty/invalid/applied state and varied route order. Additional final error-layout lower captures cover all four sizes. [Paired manifest](evidence/pr19-acceptance/paired-captures.json), contact sheets and final full-size review are the visual gate; test success alone is not its substitute. [Actual visual review](evidence/pr19-acceptance/visual-review.json) records the inspected regions and corrected error-layout defect.

CI `38405440415` validates head `6ccb1c88ac1a7e7641068654e752a006c86de623`, merge candidate `950a97b2142b1b0012a6f72d98fa08e85c039031`: frontend and gitleaks green; backend Core still running, downstream critical pending. Do not cancel/restart the job merely because it is long. Final source adds only numeric error layout CSS and two explicit 70-second visual readiness waits. After this run finishes, publish final source/evidence, inspect the new PR candidate CI and record its actual conclusion.

Live HMR: http://localhost:4187; current acceptance preview 4186 → isolated API 5167. Separate browser DB `slotmath_pr19_browser`, normal resource limits, one browser worker; production quotas unchanged. Other task checkouts/services remain untouched.

Next action: retain current CI counts/logs, push the prepared checkpoint, update the PR description, and close M5 only after exact published-candidate CI completes.
