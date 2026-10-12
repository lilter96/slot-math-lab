# Required Rider cleanup

Results page redesign: `frontend/src/pages/Results.tsx`, `frontend/src/pages/results.css`, `frontend/src/lib/results/api.ts`, `frontend/src/components/simulate/VerificationWorkbench.tsx`, `frontend/src/components/simulate/VerificationWorkbench.css`, `frontend/e2e/results-page.spec.ts`. Required passes remain outstanding. Standalone attempts exited 3 in results-page-rider.log, results-composition-rider.log, results-final-rider.log, results-tools-rider.log, results-tools-test-rider.log, results-lower-rider.log, results-scroll-rider.log, results-scroll-tests-rider.log and results-expanded-tests-rider.log.

Editable draft restoration: `frontend/src/lib/editableDraft.ts`, `frontend/src/pages/Projects.tsx`, `frontend/unit/editable-draft.spec.ts`, `frontend/e2e/serialization-errors.spec.ts`. Required passes remain outstanding; standalone attempts in editable-restore-rider.log, editable-restore-tests-rider.log and editable-restore-persist-rider.log exited 3 with `solution can't be null`.

AI source slice: `frontend/src/api/hooks.ts`, `frontend/src/components/AiGenerateModal.tsx`, `frontend/src/components/ai/AutoTunePanel.tsx`, `frontend/src/lib/generatedModel.ts`, `frontend/unit/generated-model.spec.ts`, `frontend/e2e/ai-model-source.spec.ts`. All remain uncleaned by the required Rider passes. Standalone attempts exited 3: ai-source-rider.log, ai-tune-rider.log, ai-tests-rider.log, ai-dialog-rider.log, ai-final-rider.log, ai-focus-rider.log, ai-lifecycle-tests-rider.log, ai-errors-rider.log, ai-candidate-rider.log and ai-color-rider.log.

AGENTS.md requires Reformat Code followed by Built-in: Full Cleanup on every touched source. Standalone Rider fails with `solution can't be null`; frontend files are outside the .NET solution. The pinned backend CLI encounters missing `System.Composition.AttributedModel, Version=10.0.0.12` and bounded passes time out. No successful required cleanup is claimed. Supplementary scoped whitespace/ESLint checks do not replace these passes. Run both in the owner's Rider workspace on this exact source list, review changes, and rerun validation before release.

- `backend/SlotMath.Api.Tests/PrivateWorkspaceTests.cs`
- `backend/SlotMath.Api/Features/Auth/AuthEndpoints.cs`
- `backend/SlotMath.Api/Features/Runs/Experiments.cs`
- `backend/SlotMath.Api/Features/Runs/RunHub.cs`
- `backend/SlotMath.Api/Infrastructure/AuthAccounts.cs`
- `backend/SlotMath.Api/Infrastructure/InMemoryStore.cs`
- `backend/SlotMath.Api/Infrastructure/RequestOwnership.cs`
- `backend/SlotMath.Api/Program.cs`
- `frontend/e2e/feature-flags.spec.ts`
- `frontend/e2e/golden-path.spec.ts`
- `frontend/e2e/math-workflow.spec.ts`
- `frontend/e2e/metric-integrity.spec.ts`
- `frontend/e2e/nine-lives.spec.ts`
- `frontend/e2e/private-projects.spec.ts`
- `frontend/e2e/results-workspace.spec.ts`
- `frontend/e2e/simulation-live.spec.ts`
- `frontend/e2e/subgraph-library.spec.ts`
- `frontend/e2e/workbench.spec.ts`
- `frontend/e2e/z-component-accounting.spec.ts`
- `frontend/e2e/z-measurements-workspace.spec.ts`
- `frontend/games.html`
- `frontend/src/App.tsx`
- `frontend/src/api/hooks.ts`
- `frontend/src/components/Layout.tsx`
- `frontend/src/components/Login.tsx`
- `frontend/src/components/MetricStrip.tsx`
- `frontend/src/components/Palette.tsx`
- `frontend/src/components/ProvBadge.tsx`
- `frontend/src/components/RightPanel.tsx`
- `frontend/src/components/TweaksPanel.tsx`
- `frontend/src/components/canvas/CanvasToolbar.tsx`
- `frontend/src/components/canvas/SlotCanvas.tsx`
- `frontend/src/components/canvas/nodes/BaseNodeCard.tsx`
- `frontend/src/components/editor/ClusterPayoutEditor.tsx`
- `frontend/src/components/editor/GraphValidation.tsx`
- `frontend/src/components/editor/MechanicTree.tsx`
- `frontend/src/components/editor/ModelNavigation.tsx`
- `frontend/src/components/editor/ProjectDataEditor.tsx`
- `frontend/src/components/editor/StarterLibrary.tsx`
- `frontend/src/components/editor/WeightedDrawEditor.tsx`
- `frontend/src/games/client/bootstrap.ts`
- `frontend/src/games/client/engine/dispatcher.ts`
- `frontend/src/games/client/engine/revisioned-api.ts`
- `frontend/src/games/nine-lives/NineLives.tsx`
- `frontend/src/games/nine-lives/launch.ts`
- `frontend/src/games/nine-lives/play-api.ts`
- `frontend/src/games/server/index.ts`
- `frontend/src/hooks/useLiveMetrics.ts`
- `frontend/src/hooks/useModelProjection.ts`
- `frontend/src/hooks/useProjectInitialization.ts`
- `frontend/src/hooks/useSimulation.ts`
- `frontend/src/index.css`
- `frontend/src/lib/browserIdentity.ts`
- `frontend/src/lib/featureCommands.ts`
- `frontend/src/lib/graphApi.ts`
- `frontend/src/lib/measurements/store.ts`
- `frontend/src/lib/metricEvidence.ts`
- `frontend/src/lib/modelCommands.ts`
- `frontend/src/lib/modelProjection.ts`
- `frontend/src/lib/payoutCommands.ts`
- `frontend/src/lib/projectFiles.ts`
- `frontend/src/lib/roundTrace.ts`
- `frontend/src/lib/validatedModelEdit.ts`
- `frontend/src/lib/weightCsv.ts`
- `frontend/src/main.tsx`
- `frontend/src/pages/Build.tsx`
- `frontend/src/pages/Debug.tsx`
- `frontend/src/pages/Export.tsx`
- `frontend/src/pages/Features.tsx`
- `frontend/src/pages/ModelData.tsx`
- `frontend/src/pages/Overview.tsx`
- `frontend/src/pages/Projects.tsx`
- `frontend/src/pages/Results.tsx`
- `frontend/src/pages/results.css`
- `frontend/src/store/index.ts`
- `frontend/unit/feature-commands.spec.ts`
- `frontend/unit/metric-integrity.spec.ts`
- `frontend/unit/model-commands.spec.ts`
- `frontend/unit/payout-commands.spec.ts`
- `frontend/unit/round-trace.spec.ts`
- `frontend/unit/weight-csv.spec.ts`

- `frontend/src/pages/Simulate.tsx`

- `frontend/e2e/context-inspector.spec.ts`

Inspector-pin follow-up: scoped standalone Reformat attempts again exit 3 (`solution can't be null`), including the changed source above. Full Cleanup still cannot execute for these frontend files outside the .NET solution. This is an unresolved required pass, not successful cleanup; new logs are `inspector-pin-*-rider.log` and `inspector-pin-rider-reformat.log`.

- `backend/SlotMath.Api/Features/Runs/RunArchive.cs`

- `backend/SlotMath.Api/Features/Runs/RunsEndpoints.cs`

- `backend/SlotMath.Api.Tests/RunArchiveTests.cs`

- `frontend/src/components/OverviewEvidence.tsx`

- `frontend/src/lib/overviewEvidence.ts`

- `frontend/src/api/generated-types.ts`

- `frontend/unit/overview-evidence.spec.ts`

- `frontend/e2e/overview-evidence.spec.ts`

Overview evidence follow-up: both scoped backend Reformat and Full Cleanup timed out at 22 seconds; Full Cleanup again reports missing System.Composition.AttributedModel 10.0.0.12. The task-owned process groups were terminated to avoid orphaned cleanup workers. Scoped frontend/generated/CSS Reformat attempts exit 3 with the same missing-solution failure. No successful required cleanup is claimed; `overview-evidence-*-rider.log` and `overview-evidence-rider-*.log` retain the local attempts.

- `frontend/e2e/graph-visibility.spec.ts`

Viewport filtering follow-up: scoped Reformat for SlotCanvas/new visibility test and the corrected subgraph test again exits 3; frontend Full Cleanup remains unavailable. See `graph-visibility-rider.log` / `graph-visibility-test-rider.log`.

- `frontend/src/components/Inspector.tsx`

- `frontend/src/components/editor/GraphDialog.tsx`

- `frontend/src/components/editor/GraphDialog.css`

- `frontend/e2e/workbench-accessibility.spec.ts`

Legibility/access follow-up: scoped Reformat for changed inspector/dialog/Overview/CSS/test files again exits 3. Frontend Full Cleanup remains unavailable; no required-pass success claimed. Logs: `workbench-legibility-rider.log`, `workbench-inspector-target-rider.log`, `workbench-legibility-layout-rider.log`.


- `frontend/src/lib/validationSource.ts`
- `frontend/unit/validation-source.spec.ts`
- `frontend/e2e/validation-navigation.spec.ts`

Validation navigation follow-up: GraphValidation and CanvasToolbar also require both passes. Scoped standalone attempts exit 3; frontend Full Cleanup cannot execute outside the solution. Actual evidence: `validation-navigation-rider.log`, `validation-navigation-test-rider.log`, `validation-navigation-nested-rider.log`.

- `frontend/src/lib/confirmGraphDelete.ts`
- `frontend/src/components/editor/NativeInspector.tsx`
- `frontend/e2e/destructive-actions.spec.ts`

Destructive-action follow-up: SlotCanvas, Inspector and subgraph-library tests also require both passes. Scoped standalone attempts exit 3; Full Cleanup remains unavailable. See `destructive-actions-rider.log` and `destructive-actions-native-rider.log`. This is not a successful cleanup claim.


- `frontend/src/components/editor/EvidenceInputsEditor.tsx`
- `frontend/src/components/mechanics/PluginManager.tsx`
- `frontend/e2e/data-input-safety.spec.ts`

Data/evidence follow-up also touches ProjectDataEditor and api/hooks. Reformat attempts exit 3; frontend Full Cleanup remains unavailable. Actual logs: `data-input-safety-rider.log`, `data-input-safety-hook-rider.log`, `data-input-safety-export-rider.log`. Scoped lint/build/browser checks do not replace required cleanup.


Circular reel follow-up: all touched files below require Reformat then Full Cleanup. CLI Reformat exits 3; Full Cleanup remains unavailable outside the solution. Actual attempts: reel-*-rider.log, including later density/focus/touch/action/caption edits. No successful cleanup claimed.

- `frontend/src/lib/reelCommands.ts`
- `frontend/src/lib/reelCsv.ts`
- `frontend/src/components/editor/ReelStripEditor.tsx`
- `frontend/src/components/editor/ReelStripEditor.css`
- `frontend/unit/reel-commands.spec.ts`
- `frontend/unit/reel-csv.spec.ts`
- `frontend/e2e/reel-strips.spec.ts`

- `frontend/src/lib/reelConditioning.ts`

- `frontend/unit/reel-conditioning.spec.ts`

Conditioned-reel follow-up also changes reelCommands, ReelStripEditor, reel-commands units and reel-strips browser tests. Reformat attempts exit 3; Full Cleanup remains unavailable. Actual logs: reel-conditioning-rider.log and conditioning-*-rider.log. Build/lint/scoped checks do not replace required cleanup.

Keyboard connection follow-up (026e7e0): required Reformat/Full Cleanup remain unavailable; actual standalone attempts exit 3, logs keyboard-connection-*-rider.log. Existing toolbar/canvas/card/store entries above are also affected. Added files:

- `frontend/src/components/editor/ConnectNodesDialog.tsx`
- `frontend/src/lib/graphConnections.ts`
- `frontend/unit/graph-connections.spec.ts`
- `frontend/e2e/keyboard-connections.spec.ts`

Data execution follow-up (7191f0c): ProjectDataEditor.tsx above and new `frontend/e2e/data-execution-scope.spec.ts` require both passes. Actual Reformat attempts exit 3; Full Cleanup unavailable. Logs data-execution-rider.log and data-execution-test-recheck-rider.log.

Weighted/project async follow-up (5913d07): WeightedDrawEditor.tsx and Projects.tsx listed above plus new `frontend/e2e/weighted-async.spec.ts` still require Reformat and Full Cleanup. Actual standalone attempts exit 3; Full Cleanup unavailable. Logs weighted-async-*-rider.log and project-import-async-rider.log.

Validation lifecycle follow-up (482c548): existing validatedModelEdit.ts, Features.tsx, ClusterPayoutEditor.tsx and reel-strips.spec.ts entries remain affected; new `frontend/e2e/validation-lifecycle.spec.ts` also requires Reformat/Full Cleanup. Actual standalone passes exit 3; no Full Cleanup success. Logs validation-lifecycle-*-rider.log.

Controlled race tests: new `frontend/e2e/focus-scope.spec.ts` and changed `frontend/e2e/data-execution-scope.spec.ts` require both passes. Actual standalone attempts exit 3; Full Cleanup remains unavailable. Logs focus-scope-rider.log and nested-execution-rider.log.

Numeric-table follow-up (65113ba/92af329): listed ModelData.tsx, WeightedDrawEditor.tsx, ClusterPayoutEditor.tsx, store/index.ts and index.css plus these new files require both passes:

- `frontend/src/components/editor/RawNumber.tsx`
- `frontend/src/lib/tableKeyboard.ts`
- `frontend/e2e/table-contract.spec.ts`

Actual table-contract-*-rider.log attempts exit 3; no Reformat/Full Cleanup success is claimed.

Scored-view follow-up (6fa0468/cf40f6c/d96a4ae): existing Layout.tsx, index.css, Export.tsx, results.css and simulate.css entries plus new test files require both passes:

- `frontend/e2e/project-header.spec.ts`
- `frontend/e2e/evidence-legibility.spec.ts`

Actual project-header-*, evidence-legibility-*, evidence-select-*, phone-graph-*, trace-index-*-rider.log attempts exit 3. No Full Cleanup success.

Reference/wide weights (ac65826/43f32ce/626ad84): existing NineLives.tsx, launch.ts, WeightedDrawEditor.tsx, modelCommands.ts and model-commands.spec.ts entries plus these files require both passes:

- `frontend/src/games/nine-lives/style.css`
- `frontend/e2e/reference-legibility.spec.ts`
- `frontend/e2e/wide-weights.spec.ts`

Scoped reference-*-rider.log and wide-weights-*-rider.log attempts exit 3; Full Cleanup remains unavailable.

Reference rendering follow-up: `frontend/src/games/nine-lives/presentation/asset-registry.ts` and `frontend/e2e/reference-rendering.spec.ts` require Reformat Code followed by Built-in Full Cleanup. Actual scoped attempts in reference-rendering-source-rider.log and reference-rendering-test-rider.log exit 3 (`solution can't be null`). Neither pass is claimed complete; scoped ESLint is separate validation.

Model JSON ingestion: required Reformat/Full Cleanup remain unavailable for `frontend/src/lib/modelJson.ts`, `frontend/src/lib/projectFiles.ts`, `frontend/src/pages/Projects.tsx`, `frontend/src/pages/Export.tsx`, `frontend/src/components/canvas/CanvasToolbar.tsx`, `frontend/src/hooks/useProjectInitialization.ts`, `frontend/src/api/client.ts`, `frontend/src/lib/results/api.ts`, `frontend/src/components/Layout.tsx`, `frontend/unit/model-json.spec.ts` and `frontend/e2e/model-json.spec.ts`. Scoped model-json-*-rider.log attempts exit 3; subsequent edits received fresh attempts. ESLint is separate validation.

Draft recovery: `frontend/src/store/index.ts`, `frontend/src/components/Layout.tsx` and `frontend/e2e/draft-recovery.spec.ts` require both passes. Actual scoped draft-recovery-rider.log and later test-only draft-recovery-test-rider.log and draft-recovery-write-rider.log exit 3 with missing solution context. Scoped ESLint passed independently; no cleanup success.

Raw numeric state/AST: `frontend/src/lib/modelJson.ts`, `frontend/src/components/editor/ModelNumberInput.tsx`, `frontend/src/components/editor/ProjectDataEditor.tsx`, `frontend/src/lib/measurements/model.ts`, `frontend/e2e/draft-recovery.spec.ts`, `frontend/e2e/raw-number.spec.ts`, `frontend/unit/model-json.spec.ts` and `frontend/unit/measurements.spec.ts` require both passes. Initial/follow-up raw-number*-rider.log attempts exit 3 with missing solution context, including later source/test edits and the grid alignment follow-up (raw-number-grid-rider.log). Scoped ESLint is independent validation.

Inspector integers: `frontend/src/components/Inspector.tsx`, `frontend/src/components/editor/NativeInspector.tsx`, `frontend/src/components/editor/ModelNumberInput.tsx`, `frontend/src/lib/drawRows.ts`, `frontend/unit/draw-rows.spec.ts` and `frontend/e2e/inspector-number.spec.ts` require both passes. Initial/follow-up/alias/caption/density/scroll inspector-number*-rider.log attempts exit 3 with missing solution context. Source and test edits received fresh attempts; scoped lint is separate validation.

Draw source cards: `frontend/src/components/canvas/nodes/BaseNodeCard.tsx`, `frontend/src/lib/drawRows.ts`, `frontend/unit/draw-rows.spec.ts` and `frontend/e2e/draw-card-summary.spec.ts` require both passes. Initial/source/test/long/wrap draw-card*-rider.log attempts exit 3 with missing solution context; later edits received fresh attempts. ESLint is separate validation.

Serialization recovery: `frontend/src/lib/projectFiles.ts`, `frontend/src/lib/configPayload.ts`, `frontend/src/lib/validatedModelEdit.ts`, `frontend/src/hooks/useModelProjection.ts`, `frontend/src/hooks/useLiveMetrics.ts`, `frontend/src/components/Layout.tsx`, `frontend/src/pages/Export.tsx`, `frontend/src/components/mechanics/MechanicManager.tsx`, `frontend/src/components/canvas/CanvasToolbar.tsx`, `frontend/src/components/editor/ProjectDataEditor.tsx`, `frontend/src/components/editor/WeightedDrawEditor.tsx`, `frontend/src/components/simulate/MeasurementEditor.tsx`, `frontend/src/components/simulate/AccountingEditor.tsx`, `frontend/src/components/simulate/VerificationProfileEditor.tsx`, `frontend/e2e/serialization-errors.spec.ts` and `frontend/unit/config-serialization.spec.ts` require both passes. Initial/follow-up/submit/profile/dialog/selector serialization*-rider.log attempts exit 3; later edits received fresh attempts. No cleanup success; scoped ESLint is independent validation.
