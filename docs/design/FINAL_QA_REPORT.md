# Precision Workbench QA summary

The workbench supports many private projects per owner, source-derived editing and explicit server versions. [Implementation status](IMPLEMENTATION_STATUS.md) summarizes delivered areas; [private workspaces](PRIVATE_WORKSPACES.md) defines ownership/persistence boundaries. Graph-studios F01–F06 and M0–M5 acceptance are complete for the supported scope in the [delivery matrix](graph-studios/DELIVERY.md).

## Verified implementation

[CI run 38425695744](https://github.com/lilter96/slot-math-lab/actions/runs/38425695744) passed publication head `d29a1427377d2b8db58fd89d6371d99d3b921f92`, application source `4f18f1ffa771187c39647861d2a66286913426e4`: **1,389 Core, 142 API, 299 frontend unit and 35 critical browser tests**, zero failed/skipped/retried test cases. Build, lint, formatting, unchanged original-fixture checks and gitleaks passed.

Actual Dockerfiles built locally; isolated API/proxy/configs/SPA smoke and 17 independent compiler/round cases passed. Docker/smoke are excluded from PR CI by the existing main-push condition. Local full backend passed 1,389 Core + 141 API; CI's API count is 142. Counts from different runs are not interchangeable.

Local full browser verification first passed **315/345**, with 30 failures and zero Playwright retries. All 30 subsequently had successful failed/affected reruns. The later shared Undo/source/storage/variant run passed 72/73, followed by its corrected visual readiness case 1/1. Final canvas visibility changes passed **25/25 affected scenarios**. These attempts are recorded separately; the full local run is not reported as first-attempt green. Earlier [CI run 38413845577](https://github.com/lilter96/slot-math-lab/actions/runs/38413845577) used one critical-test retry; the final run above used none.

Intentional edit/execute/export/reimport checks cover **25 game variants**, including all **18 Le Militare** mode/entry combinations, plus a separate reference, cluster/cascade starters and a compatible custom 7 → 21 → Undo graph. Original 38 backend fixtures remain unchanged. Independent audit programs exercise compiler/exact/reference/optimized agreement; scalar dynamic weights retain the existing reference fallback.

## Visual and source review

Whole-page review inspected **446 matched before/after captures** at 1920×1080, 1440×900, 1280×800 and 390×844, zoom/DPR 1. Coverage includes delivered Studio tabs, lower/internal scrolls, drawers, source JSON, transition tables, dialogs and dirty/error/applied states. Final corrections made numeric errors readable and prevented states disappearing after Apply/Undo. No document overflow or page errors occurred in the recorded captures.

Earlier page-specific checks also covered My projects, expanded Results verification forms, numeric controls, recovery/error states and selected Firefox/WebKit workflows. They do not establish comprehensive accessibility or whole-roadmap acceptance. Required scoped Rider Reformat followed by Full Cleanup succeeded for the graph-studios/audit source scope; [the reproducible workflow](../RIDER_FRONTEND_CLEANUP.md) records the solution-context procedure and earlier unsupported standalone attempts.

## Retained output and limits

Generated screenshots, contact sheets, logs, audit JSON and per-iteration notes are retained locally in ignored `docs/design/qa/` and `docs/design/graph-studios/evidence/`. Original failures remain available there; downloadable CI reports are attached to the linked runs. Maintained summaries, reproducible tests and input fixtures remain versioned. Do not force-add generated verification output.

Sharing, automatic cross-device autosave, durable multi-step Undo, provider-quality certification, production capacity and hosted rollout rehearsal remain outside the delivered scope. Phone graph movement/connections are read only; form workflows remain available. Unsupported graph regions retain source with explicit edit restrictions. Compiler acceptance alone does not certify RTP. Practitioner/accessibility acceptance remains separate work.

Documentation/artifact cleanup leaves application source unchanged and requires no tests. Routine implementation changes run only affected/new tests; full suites are reserved for substantial integrated features or explicit user requests.
