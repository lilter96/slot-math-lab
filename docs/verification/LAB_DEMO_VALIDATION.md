# Nine Lives migration validation

Rules are authored with existing atoms and expressions in editable JSON
subgraphs. Generic compiler changes optimize the existing Map, Filter, Fold and
Count expressions, constant sources, scoped bindings and integer counters.
No new atom, expression function or mechanic dispatch was added. The source configuration hash is
`315b8589749359b42ecc2b91c56c72dbeb61703f1fd3e495a6987c55c34dcce4`.

## Evidence

- Build, TypeScript and ESLint passed.
- 1,120 Core tests, 134 API tests and 91 frontend unit tests passed after
  optimization. Core took 3 minutes 52 seconds.
- The first combined backend attempt lacked the PostgreSQL environment variable
  and failed API authentication. The isolated API rerun passed all 134 tests;
  failure evidence is retained in `backend-full-optimized.log` and the first
  `optimized-full.trx` under the API test project.
- The first optimized Chromium run passed 69 tests and failed one: a pinned
  replay received two consecutive HTTP 429 responses, while the client retried
  only once. Its log, JSON and trace are retained as
  `browser-before-launch-fix.{log,json}` and `launch-quota-failure.zip` in the
  Core test results directory. The client now permits two retries of explicitly
  rejected requests; accepted requests and network failures are not retried.
  The failed scenario passed separately after the fix. The regression also
  checks two consecutive rejections and both Retry-After delays.
- The full Chromium rerun after the launch fix finished in 17.8 minutes:
  69 passed, one failed, zero skipped and zero test retries. Both the formerly
  failing replay and the two-consecutive-429 regression passed. The remaining
  failure was an existing five-second Results assertion while the server
  explicitly requested a sixty-second quota pause; the UI correctly waited.
  Its trace is retained as `profile-quota-failure.zip`. The assertion now uses
  the same seventy-second quota allowance as neighboring checks. All three
  tests in `z-verification-profile.spec.ts` then passed in 14.5 seconds, without
  retries. The whole suite was not rerun after this test-only timeout change;
  a single green 70/70 full-suite run is not claimed.
- Six graph/oracle tests passed, including full-round traces, shared Wilds,
  an Exact reduced fixture, negative control and exact cap termination.
- Play/replay and purchase of nine lives passed browser tests.
- [Play screenshot](lab-demo-play.png) shows the preserved Pixi client.
- The Play container fills the available workspace instead of shrinking to its
  content. Browser geometry checks passed at 2048×1120, 1920×1080, 1280×720,
  390×844 and 844×390. Desktop controls fit without scrolling; phone layouts
  retain vertical scrolling and have no horizontal overflow. A real round
  passed after resizing, and the screenshot was refreshed.
- Deployment scripts require fresh Nine Lives evidence; their production runs
  have not been executed during this migration.
- Final browser logs and machine-readable results are retained under
  `backend/SlotMath.Core.Tests/TestResults/browser-full-optimized.{log,json}`.
  The corrected profile-file rerun is in `browser-profile-fixed.{log,json}`.
  Validation uses isolated API/preview ports and a task-owned PostgreSQL
  database. Backend results are in `optimized-full.trx` and
  `backend/SlotMath.Api.Tests/TestResults/api-full-optimized.trx`.
- Export drift check, independent Fraction fixture check and product branding scan passed.
- [Ten-million-round sample](lab-demo-sampled.json): 96.370975% in 85.098 seconds,
  fixed-count 95% normal approximation 95.138468%–97.603482%. Full-game Exact is
  unavailable.
- [Throughput](lab-demo-performance.json): median 33,100 rounds/second on one
  worker; [four-worker total](lab-demo-performance-four-workers.json): 105,050
  rounds/second. The required 39,800 per worker is not met. Final measurements
  ran sequentially with task-owned validation stopped, on a shared host.
- [Paired baseline](lab-demo-optimization-baseline.json) confirms identical
  one-million-round payout statistics and histogram for original and optimized
  composition on the same binary.
- [Fold-bound proposal](../FOLD_BOUNDS_PROPOSAL.md) is not implemented or authorized.

## Cleanup blockers

Mandatory JetBrains Reformat Code and Built-in: Full Cleanup were requested with
the pinned tool, solution context and owner settings. Both backend passes emitted
Roslyn ProviderInstantiation errors: `System.Composition.AttributedModel,
Version=10.0.0.12` could not be loaded, then stalled. The stalled task-owned
processes were terminated. Successful completion of either full scoped pass is
not claimed. Scoped dotnet formatting was applied and verified, but does not
replace the required JetBrains profiles.

Rider frontend reformat initially failed while opening the project with
`solution can't be null`; the final retry reported `Only one instance of Rider
can be run at a time`. The owner's running IDE was left untouched. Frontend Full Cleanup could not be executed through
available tooling. ESLint and TypeScript passed; they do not replace those passes.
Reformat was requested again after the viewport changes to `NineLives.tsx` and
`style.css` and failed with the same running-instance error; Full Cleanup remains
unavailable for those files.
Python and standalone verification scripts also remain without Rider Full Cleanup.

The solution-wide formatting check reports pre-existing errors in untouched
kernel files. Task files pass the scoped check; unrelated files were not changed.

Backend source files whose required cleanup is unconfirmed:

- [backend/SlotMath.Core/Compiler/SamplingCodegen.cs](../../backend/SlotMath.Core/Compiler/SamplingCodegen.cs)
- [backend/SlotMath.Core/Compiler/SamplingExpressions.cs](../../backend/SlotMath.Core/Compiler/SamplingExpressions.cs)
- [backend/SlotMath.Core/Compiler/SamplingFrame.cs](../../backend/SlotMath.Core/Compiler/SamplingFrame.cs)
- [backend/SlotMath.Core/Compiler/SamplingPlan.cs](../../backend/SlotMath.Core/Compiler/SamplingPlan.cs)
- [backend/SlotMath.Core/Expressions/CollectionFunctions.cs](../../backend/SlotMath.Core/Expressions/CollectionFunctions.cs)
- [backend/SlotMath.Api.Tests/CompiledGraphCacheTests.cs](../../backend/SlotMath.Api.Tests/CompiledGraphCacheTests.cs)
- [backend/SlotMath.Api.Tests/GraphExecutionTests.cs](../../backend/SlotMath.Api.Tests/GraphExecutionTests.cs)
- [backend/SlotMath.Api.Tests/MeasurementApiTests.cs](../../backend/SlotMath.Api.Tests/MeasurementApiTests.cs)
- [backend/SlotMath.Api.Tests/RunArchiveTests.cs](../../backend/SlotMath.Api.Tests/RunArchiveTests.cs)
- [backend/SlotMath.Benchmarks/GraphPerformance.cs](../../backend/SlotMath.Benchmarks/GraphPerformance.cs)
- [backend/SlotMath.Core.Tests/Benchmarks/ComposedFeatureBenchmarkTests.cs](../../backend/SlotMath.Core.Tests/Benchmarks/ComposedFeatureBenchmarkTests.cs)
- [backend/SlotMath.Core.Tests/Benchmarks/ComposedFeatureNoPluginBenchmarkTests.cs](../../backend/SlotMath.Core.Tests/Benchmarks/ComposedFeatureNoPluginBenchmarkTests.cs)
- [backend/SlotMath.Core.Tests/Benchmarks/SamplingPlanEquivalenceTests.cs](../../backend/SlotMath.Core.Tests/Benchmarks/SamplingPlanEquivalenceTests.cs)
- [backend/SlotMath.Core.Tests/Benchmarks/SamplingTierEquivalenceTests.cs](../../backend/SlotMath.Core.Tests/Benchmarks/SamplingTierEquivalenceTests.cs)
- [backend/SlotMath.Core.Tests/EngineFixRegressionTests.cs](../../backend/SlotMath.Core.Tests/EngineFixRegressionTests.cs)
- [backend/SlotMath.Core.Tests/Math/NineLivesGraphTests.cs](../../backend/SlotMath.Core.Tests/Math/NineLivesGraphTests.cs)

Frontend and verification sources whose required cleanup is unconfirmed:

- [frontend/e2e/nine-lives.spec.ts](../../frontend/e2e/nine-lives.spec.ts)
- [frontend/e2e/realtime-durability.spec.ts](../../frontend/e2e/realtime-durability.spec.ts)
- [frontend/e2e/results-workspace.spec.ts](../../frontend/e2e/results-workspace.spec.ts)
- [frontend/e2e/simulation-live.spec.ts](../../frontend/e2e/simulation-live.spec.ts)
- [frontend/e2e/z-measurements-workspace.spec.ts](../../frontend/e2e/z-measurements-workspace.spec.ts)
- [frontend/e2e/z-verification-profile.spec.ts](../../frontend/e2e/z-verification-profile.spec.ts)
- [frontend/scripts/export-lab-demo-fixtures.mjs](../../frontend/scripts/export-lab-demo-fixtures.mjs)
- [frontend/src/App.tsx](../../frontend/src/App.tsx)
- [frontend/src/components/canvas/CanvasToolbar.tsx](../../frontend/src/components/canvas/CanvasToolbar.tsx)
- [frontend/src/components/editor/GraphDialog.css](../../frontend/src/components/editor/GraphDialog.css)
- [frontend/src/components/editor/GraphDialog.tsx](../../frontend/src/components/editor/GraphDialog.tsx)
- [frontend/src/components/editor/ModelNavigation.tsx](../../frontend/src/components/editor/ModelNavigation.tsx)
- [frontend/src/components/editor/ProjectDataEditor.tsx](../../frontend/src/components/editor/ProjectDataEditor.tsx)
- [frontend/src/components/simulate/AccountingEditor.tsx](../../frontend/src/components/simulate/AccountingEditor.tsx)
- [frontend/src/components/simulate/MeasurementEditor.tsx](../../frontend/src/components/simulate/MeasurementEditor.tsx)
- [frontend/src/components/simulate/VerificationProfileEditor.tsx](../../frontend/src/components/simulate/VerificationProfileEditor.tsx)
- [frontend/src/games/nine-lives/NineLives.tsx](../../frontend/src/games/nine-lives/NineLives.tsx)
- [frontend/src/games/nine-lives/graph.ts](../../frontend/src/games/nine-lives/graph.ts)
- [frontend/src/games/nine-lives/launch.ts](../../frontend/src/games/nine-lives/launch.ts)
- [frontend/src/games/nine-lives/manifest.ts](../../frontend/src/games/nine-lives/manifest.ts)
- [frontend/src/games/nine-lives/play-api.ts](../../frontend/src/games/nine-lives/play-api.ts)
- [frontend/src/games/nine-lives/presentation-types.ts](../../frontend/src/games/nine-lives/presentation-types.ts)
- [frontend/src/games/nine-lives/presentation/asset-registry.ts](../../frontend/src/games/nine-lives/presentation/asset-registry.ts)
- [frontend/src/games/nine-lives/presentation/layout.ts](../../frontend/src/games/nine-lives/presentation/layout.ts)
- [frontend/src/games/nine-lives/presentation/reel-art.ts](../../frontend/src/games/nine-lives/presentation/reel-art.ts)
- [frontend/src/games/nine-lives/presentation/signal.ts](../../frontend/src/games/nine-lives/presentation/signal.ts)
- [frontend/src/games/nine-lives/presentation/spin-speed.ts](../../frontend/src/games/nine-lives/presentation/spin-speed.ts)
- [frontend/src/games/nine-lives/presentation/win-overlay.ts](../../frontend/src/games/nine-lives/presentation/win-overlay.ts)
- [frontend/src/games/nine-lives/style.css](../../frontend/src/games/nine-lives/style.css)
- [frontend/src/games/nine-lives/types.ts](../../frontend/src/games/nine-lives/types.ts)
- [frontend/src/hooks/useSimulation.ts](../../frontend/src/hooks/useSimulation.ts)
- [frontend/src/lib/graphApi.ts](../../frontend/src/lib/graphApi.ts)
- [frontend/src/lib/results/api.ts](../../frontend/src/lib/results/api.ts)
- [frontend/src/pages/Build.tsx](../../frontend/src/pages/Build.tsx)
- [frontend/src/pages/Simulate.tsx](../../frontend/src/pages/Simulate.tsx)
- [frontend/unit/bonus-boundary.spec.ts](../../frontend/unit/bonus-boundary.spec.ts)
- [frontend/vite.config.ts](../../frontend/vite.config.ts)
- [scripts/verification/check-demo-names.py](../../scripts/verification/check-demo-names.py)
- [scripts/verification/component-accounting-production.mjs](../../scripts/verification/component-accounting-production.mjs)
- [scripts/verification/demo_reference.py](../../scripts/verification/demo_reference.py)
- [scripts/verification/measurements-advanced-production.mjs](../../scripts/verification/measurements-advanced-production.mjs)
- [scripts/verification/measurements-production.mjs](../../scripts/verification/measurements-production.mjs)
- [scripts/verification/realtime-production.mjs](../../scripts/verification/realtime-production.mjs)
- [scripts/verification/results-production.mjs](../../scripts/verification/results-production.mjs)
- [scripts/verification/verification-profile-production.mjs](../../scripts/verification/verification-profile-production.mjs)
- [scripts/verification/ws-negotiation-production.mjs](../../scripts/verification/ws-negotiation-production.mjs)

## Subsequent cleanup and integration

The cleanup limitations and file inventories above describe the earlier agent
attempts. The owner subsequently reported completing manual cleanup and asked
for a commit without rerunning tests. The branch then merged `origin/main` at
`fa9c088` without conflicts. Earlier test counts and performance fingerprints
remain historical evidence; CI on the pull request checks the integrated code.
See `lab-demo-cleanup-status.json` and `lab-demo-validation.json` for the recorded
validation boundary.
