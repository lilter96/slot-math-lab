# Repository Guidelines

## Project Structure & Module Organization

`backend/SlotMath.Core` contains the math kernel; `SlotMath.Api` hosts ASP.NET Core endpoints. The corresponding `.Tests` projects contain xUnit tests, and `SlotMath.Benchmarks` measures throughput. `frontend/src` contains React/TypeScript code; `frontend/public` holds static assets. Frontend tests live in `frontend/unit` and `frontend/e2e`. The implemented system and verification workflow are documented in `docs/SYSTEM.ru.md`. CI workflows are under `.github/workflows`.

## Build, Test, and Development Commands

Use .NET 10 and Node.js 22. Run from the repository root:

- `dotnet build backend/SlotMathLab.slnx`: build the backend.
- `dotnet test backend/SlotMathLab.slnx`: run backend tests.
- `dotnet format backend/SlotMathLab.slnx --verify-no-changes`: check formatting.
- `docker compose up -d postgres redis`: start local dependencies.
- `dotnet run --project backend/SlotMath.Api`: run the API.

From `frontend`:

- `npm ci --legacy-peer-deps`: install dependencies as CI does.
- `npm run dev`: start Vite.
- `npm run build` and `npm run lint`: check TypeScript/build output and ESLint rules.
- `npm run test:unit`: run browser-free logic tests.
- `npm run test:e2e:critical`: run critical browser workflows; build first and provide database dependencies. Playwright starts the API and preview server.

## Private Multi-Project Product Scope (Mandatory)

Each user can own many private projects, games, models and personal materials. Start at **My projects**; do not automatically create or select one game for a fresh workspace. Nine Lives is a reference fixture, not the universal game schema. Derive geometry, symbols, mechanics and editor capabilities from the selected model.

For new persistence and asynchronous operations, follow the data scope table in `docs/SYSTEM.ru.md#projects`: user ownership, editable project identity and evaluated configuration/source identity are separate boundaries.

Keep project identity separate from authored model/template IDs. Switching projects must preserve each project's edits and nested context; independent template copies must not share mutable editing state. Scope browser storage to verified user identity and enforce ownership of server configurations, runs, experiments and realtime access. Never show another project's result as current evidence.

Sharing, collaboration, public links, invitations, teams and publication are deferred. Preserve personal JSON export/import and legacy inbound import compatibility. Browser drafts autosave locally; server version saving is explicit. Do not claim automatic cross-device synchronization or large-scale capacity without implementation and evidence. See `docs/SYSTEM.ru.md#projects` for current behavior and limits.

## Coding Style & Naming Conventions

For the workbench redesign, deliver coherent pages rather than a sequence of tiny control/style patches. Each page pass covers hierarchy, content, actions, empty/loading/error states, responsive layout and keyboard interaction together. Validate the finished page with relevant scenarios and visual inspection; do not rerun broad suites or create a separate iteration for each cosmetic adjustment. Preserve actual mathematical evidence and private multi-project behavior while improving the interface.

Visual acceptance must inspect the whole page from its header to the footer, including content below the fold, every tab and expanded details/forms. A first-viewport screenshot or green interaction test cannot certify a page. Capture and inspect desktop and phone views of the lower sections as well as the primary workspace; correct native unstyled controls, cramped labels and inconsistent shared components before delivering the page.

Follow `backend/.editorconfig`: four spaces, LF endings, file-scoped namespaces, and braces on separate lines. Use PascalCase for C# types/methods and camelCase for locals. Match nearby TypeScript code: typically two spaces, single quotes, and semicolons. Use PascalCase component names and camelCase functions. Use the generation scripts for API/schema types rather than editing generated files manually.

## Atomic Graph Composition (Mandatory)

All game mechanics must be authored by composing generic atomic nodes and typed expressions into JSON graphs, extracting reusable subgraphs, and composing those subgraphs. This applies to cluster detection, gravity, cascades, collectors, bonuses, and any future mechanic.

Never add game- or mechanic-specific dispatch, type inference, validation, execution, or optimization to the kernel, compiler, interpreters, or API. In particular, checking mechanic names or IDs (for example, a transform identifier) to select hardcoded behavior or result shapes is strictly prohibited. Moving that dispatch into a helper, registry, or special evaluator does not satisfy this rule.

During game migrations, use the existing atoms and expression capabilities. Do not extend the kernel or add branches for named expression functions (including array-update helpers) as part of the migration. Propose any required new atom separately and obtain explicit user authorization before implementing it. Compose array updates from existing index-aware map and conditional expressions.

When existing atoms cannot express an operation, document the gap and propose the smallest generally useful atom or expression capability separately. Implement it only after explicit user authorization, with a documented contract and tests independent of any game. Implement the mechanic itself in editable JSON subgraphs. Performance work must optimize generic atoms, expressions, or control flow and must not recognize a game, mechanic, or authored graph pattern. Existing fast-path exceptions in architecture documents do not authorize new exceptions; obtain explicit user authorization before proposing any exception to this rule.

## Testing Guidelines

Use `*Tests.cs` for xUnit tests and `*.spec.ts` for Playwright tests. Add mathematical regressions at the lowest appropriate layer, with pinned seeds and independent expected results. Preserve the implemented PRNG/constants contract documented in `docs/SYSTEM.ru.md#numbers`. CI has no numeric coverage threshold. Retain failure evidence.

### Test Selection and Run Frequency (Mandatory)

- Do not run complete test suites on every iteration.
- For routine changes, run only affected tests and newly added tests. Select relevant unit, API, or browser scenarios based on the behavior changed, including shared runtime changes.
- Run a full suite only after a substantial feature has been integrated, or when the user explicitly requests it. Do not repeat a successful full run without new changes or unresolved failures that justify it.
- If a full run fails, fix the failures and rerun only failed or affected tests; do not automatically repeat the entire suite.
- Documentation-only changes do not require tests.

This policy takes precedence over broader per-iteration testing instructions in autorun files, design handoffs, and other repository documents. Required release checks still apply at release time; they do not authorize full suites during each implementation iteration.

### Generated Artifacts (Mandatory)

Keep screenshots, contact sheets, test logs, browser reports, generated audit JSON and per-iteration review notes out of Git. Use ignored local output directories or CI artifacts; never force-add ignored verification output. Retain failure evidence locally or as CI artifacts. Commit maintained acceptance summaries and reproducible test inputs, not generated run output. Removing generated artifacts or updating documentation does not require rerunning tests.

## Commit & Pull Request Guidelines

History favors imperative, descriptive subjects such as “Document feature flags.” Keep commits focused. PRs should explain behavior changes and validation, link relevant issues or system documentation sections, and include screenshots for visual changes. Pass applicable CI checks before merging.

## Concurrent Task Workspaces

Use one folder and task branch per conversation. Create unique worktrees from fetched `origin/main`, for example `git worktree add -b task/<name> ../slot-math-lab-worktrees/<name> origin/main`. Never modify another checkout. Isolate service ports and writable runtime data.
