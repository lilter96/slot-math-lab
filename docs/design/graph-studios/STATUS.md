# Graph studios status

Scope: Reel & Paytable Studio first, State Machine Designer second. No continuation of the old redesign roadmap, no new runtime, no push/merge/deploy. Contract: START_CODEX and slotmath-graph-studios skill.

## M0 — complete; M1 — in progress

- Assigned worktree: precision-workbench-redesign; branch feat/precision-workbench-redesign. Task-entry HEAD 6bd8669; preserved interrupted Simulate work in local checkpoint 26d8fc9. Its build and scoped Chromium/Firefox/WebKit checks passed, but visual review was interrupted; no new Simulate delivery claim. Existing source/QA data retained.
- Fetched origin/main = b81e71c82cb0314d066d7134278751b85676f356, matching the handoff audit and merge-base. Local main remains the older 0e768f4 checkout; it was not modified. No upstream conflict or auto-merge is required.
- Added only docs/design/graph-studios and .agents/skills/slotmath-graph-studios from the supplied Downloads package. Read scope, editing contract, acceptance and Studio spec.
- Existing worktree components to reuse: modelProjection/modelCommands, reelCommands/reelConditioning, modelJson exact-range admission, projectFiles/store private projects, validatedModelEdit and subgraphComposition. Existing payoutCommands is reference/game-ID restricted and cannot supply the required multi-game Studio contract. Existing reel recognition also needs renamed/nonuniform-source coverage.
- Node 22.23.3. Isolated local services use API 5166, preview 4186 and task PostgreSQL 5546; verify liveness before integration. No system-wide or other-checkout changes.
- Baseline build passes; selected authoring units 14/14; 38 existing fixtures verified without regeneration. Corpus inventory records 65 executable-shape cases, all 18 Le Militare mode/entry cases, ordinary/bought models, separate reference/starters and small fixtures. le-militare.prices.json is explicitly non-graph data. Evidence: m0-* logs and corpus-inventory.json. Acceptance rows G01–V03 remain unproven for the new studios.

## M1 evidence and exact next action

- Added lib/authoring/sourceIndex.ts: scoped string IDs, definition consumers, exact structural paths and explicit state reads/writes; source is never executed or regenerated. Resolving an edit target follows IDs after reorder and rejects missing/duplicate identities. Opaque/plugin data stays opaque. Three independent units, scoped lint and build pass.
- Added unit/fixtures/graphStudiosCorpus.ts and scripts/graph-studios-inventory.mjs for repeatable inventory. Factories are verification inputs only, never a replacement for an edited project.
- Real API foundation 2/2 passes (m1-foundation-api.log): a tiny ordered reel pays exactly 1/3 for AB among AB/BC/CA windows; reorder removes that payout, updates wrap padding, preserves opaque fields and leaves the original untouched. A no-op preserves full seeded play responses for seeds 7/42. Independent weights change exact payout probability 3/4 → 1/4 while preserving outcome ID 001, payloads, edges and unrelated source. These prove existing atoms/adapters, not completion of required game journeys or new Studio bindings.
- Next: implement typed binding read/write/dependency sets and a source-preserving transaction/undo boundary that retains nested context. Extend physical/outcome bindings for renamed/shared sources, then multi-game payout/payline contracts. Complete M1 before the Studio UI; M2–M5 remain pending.

## Known boundaries

Required Rider cleanup remains unavailable for earlier source, including preserved Simulate files: pages/Simulate.tsx, pages/simulate.css, components/simulate/ExecutionConfiguration.tsx, components/simulate/measurements.css and e2e/simulate-page.spec.ts (frontend/ prefix). An isolated frontend formatter attempt exited 3 with solution can't be null; build/lint do not replace Full Cleanup. Revalidate only if environment changes. Full browser Int64 authoring is not supported; existing admission rejects unsafe original tokens rather than rounding them. Pending-reload recovery and authoring metadata persistence must be proven specifically for both new views.

New-task required cleanup is also blocked for frontend/src/lib/authoring/sourceIndex.ts, frontend/unit/authoring-source-index.spec.ts, frontend/unit/fixtures/graphStudiosCorpus.ts, frontend/scripts/graph-studios-inventory.mjs and frontend/e2e/graph-studios-foundation.spec.ts. Scoped attempts exited 3 with the same solution-context failure; m0-corpus-rider and m1-*-rider logs retain evidence. No required cleanup success is claimed.
