# Graph studios status — 2026-10-13

Reel & Paytable Studio and State Machine Designer operate on the selected private project's canonical GraphConfig. Each owner can have many projects; sharing is deferred. Implementation source: `4f18f1ffa771187c39647861d2a66286913426e4`.

**F01–F06 and M0–M5 acceptance are complete for the supported scope.** The [delivery matrix](DELIVERY.md) maps the unchanged [original acceptance contract](04_EXECUTION_AND_ACCEPTANCE.md) to reproducible tests and records limitations.

[CI run 38425695744](https://github.com/lilter96/slot-math-lab/actions/runs/38425695744) passed on publication head `d29a1427377d2b8db58fd89d6371d99d3b921f92`: **1,389 Core, 142 API, 299 frontend unit and 35 critical browser tests**, zero failed/skipped/retried test cases. Build, lint, formatting, original-fixture checks and gitleaks passed. Docker/smoke are excluded from PR CI by the existing main-push condition; actual local Dockerfile builds and isolated smoke passed separately.

Whole-page visual review covered **446 matched before/after captures** at 1920×1080, 1440×900, 1280×800 and 390×844, including tabs, lower/internal scroll regions, source drawers, dialogs and invalid/applied states. Final corrections made numeric errors readable and kept Designer states visible after Apply/Undo. The final canvas revision passed **25/25 affected browser scenarios**.

Raw screenshots, logs, generated manifests and per-iteration notes are retained in ignored local output directories. CI reports are available from the linked run's artifacts. These outputs are not repository source; maintained summaries, tests and input fixtures remain versioned. Documentation/artifact cleanup leaves the application source unchanged and requires no test rerun. Current publication checks are on [PR19](https://github.com/lilter96/slot-math-lab/pull/19/checks).

Undo is session-only latest-edit history scoped to owner/project/source. Browser drafts recover locally; server version saving is explicit. Arbitrary unresolved custom regions remain intact, with unsupported edits identified locally. No new kernel atoms, evaluator, RNG or game-specific runtime dispatch were introduced. Wider roadmap and practitioner acceptance remain separate from the studios' completed contract.
