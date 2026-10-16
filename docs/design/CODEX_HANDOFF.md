# Codex handoff: install design skills and implement Slot Math Lab redesign

> Copy `UX_MASTERPLAN.md`, `DESIGN_SYSTEM.md` and this file into **your local repository** at `docs/design/` before using Codex. These files are proposed requirements, not edits already applied to GitHub.

## 1. Skills to install — recommended combination

### A. Primary: UI UX Pro Max (design system + domain UX rules)
Source: https://github.com/nextlevelbuilder/ui-ux-pro-max-skill

Install into the **local project** for Codex:

```bash
cd ~/path/to/slot-math-lab
npm install -g ui-ux-pro-max-cli
uipro init --ai codex
```

Current upstream package name is **`ui-ux-pro-max-cli`**, not the older `uipro-cli`. Review third-party scripts before running. Alternative no-global route, if supported by your npm setup:

```bash
npx ui-ux-pro-max-cli init --ai codex
```

**Important:** generated recommendations are reference material, not permission to ignore the existing project design brief, math contracts, or user flow. The skill also has generic landing-page templates: explicitly require the **professional IDE / developer-tool** product type, React stack, data-dense workspace and no marketing-site hero.

### B. Complementary: `frontend-design` by Anthropic (deliberate visual choices)
Source: https://github.com/anthropics/skills/tree/main/skills/frontend-design

Using the Codex skill installer **inside Codex**, after reviewing the skill source:

```text
$skill-installer install https://github.com/anthropics/skills/tree/main/skills/frontend-design
```

Alternative project-level CLI installation (if `skills` CLI is available):

```bash
npx skills add anthropics/skills --skill frontend-design -a codex
```

Restart Codex if a skill does not appear. The two skills complement each other: UI UX Pro Max supports design-system and UX heuristics; frontend-design insists on opinionated visual craft, specificity and avoiding templated aesthetics.

### C. Browser testing — already available in the project
`frontend/package.json` already includes **`@playwright/test`** and scripts `test:e2e`, `test:e2e:critical`, `test:unit`. Use these plus screenshots and interaction checks. Installing additional browser skills is **optional**, not a substitute for actual browser verification.

## 2. Current product scope and execution

Updated 2026-10-12 after the user's clarification. This section replaces the original single-game handoff and between-phase approval prompts.

Read `UX_MASTERPLAN.md`, `DESIGN_SYSTEM.md`, `PRIVATE_WORKSPACES.md`, `IMPLEMENTATION_STATUS.md`, `README.md`, `docs/PRD.md`, `docs/TESTING.md` and applicable `AGENTS.md`. Inspect the current source and recorded evidence before continuing; do not restart completed phases.

The product is a private workbench for **many users, each owning many projects, games, models and personal materials**. `/` leads to **My projects** (`/projects`). A fresh workspace has no automatically selected game. Create a blank model, import personal JSON or copy a game template; search/open/duplicate/rename projects. Opening a project leads to its model-derived Overview. Public examples become independent private copies.

Nine Lives reference is one regression fixture: 6×5, cluster/cascade/free-spins/bonus-buy, uncalibrated. Other models use their own geometry, symbols, definitions and supported editors. Never turn every game into Nine Lives or display another project's result as current.

Project switching retains canonical edits, layout and nested context. Generated project/storage identity is independent of authored model ID. Scope staged edits, Debug receipts, asynchronous validation and presentation state by project. Verify browser identity before opening user-scoped stores. Enforce ownership of server configurations, runs, experiments and SignalR subscriptions.

**Sharing/collaboration are out of scope now.** Do not add public project links, publication, teams or invitations. Preserve JSON backup/import and legacy inbound `?load=` compatibility. A private run bookmark does not grant access to another user. Local drafts autosave within browser capacity; server save is explicit, and automatic cross-device synchronization is not implemented.

Retain React 19, TypeScript, Vite, React Router, Zustand, React Flow, CodeMirror, Playwright and .NET 10. Keep one canonical GraphConfig/AST model and preserve unknown fields, nested graphs, model round trips, feature flags and pinned mathematical evidence. Do not change kernel/compiler/PRNG semantics to implement presentation features. No fabricated metrics, fake Exact/Sampled labels, universal reel schema or game-specific kernel dispatch.

The user authorizes continuous local work through the roadmap. Historical Phase 0/1-only and screenshot-approval pauses are superseded by `/home/terentiy/Downloads/CODEX_AUTORUN.md`. Keep coherent local commits and report blockers. Pushes, merges, deployment and paid services still need explicit authorization.

**Test frequency is mandatory in AGENTS.md.** Routine changes run only affected/new tests. Full suites run after a substantial integrated feature or an explicit user request. After fixing a failed full run, rerun failed/affected scenarios rather than automatically repeating the entire suite. Documentation-only edits need no tests. Do not interpret the list of available commands as a checklist to run every iteration.

## 3. Implementation slices and evidence

1. **Trust and shell:** accurate missing/loading/error/provenance states, responsive navigation, project switcher and feature gates. Preserve old routes and deep links.
2. **Personal project library and Overview:** private collection/empty state, independent template copies, import, search, switching and saved owner-only versions. Model-derived facts must match at least two different games; Nine Lives reference remains 6×5.
3. **Graph workbench:** searchable mechanic hierarchy and breadcrumbs, separate templates/palette, contextual inspector, readable framing and project/mechanic camera state. Preserve nested graph serialization and source IDs.
4. **Real math editors:** capability detection first, then weighted tables/payouts/feature forms only where source mappings are explicit and safe. Validate transactions, Undo, export/reload and independent copy edits; unsupported models retain raw data and graph escape hatches.
5. **Evidence and Debug:** keep saved-run authority, seeds/hashes/N/uncertainty; Debug executes the current project, using compatible returned board histories or raw state. No invented event-to-node mapping. Missing instrumentation belongs in the separate generic trace specification.
6. **Hardening:** targeted keyboard/accessibility/cross-browser checks and genuine interaction performance measurement; compatibility, migration/rollback plan and truthful final QA. Take actual captures at 1440×900, 1280×800, 1024×768 and 390×844. Preserve release gates and do not claim acceptance or certification without evidence.

For each slice, inspect current state, implement the missing behavior, run relevant checks under AGENTS.md, review real browser output where applicable, and update `IMPLEMENTATION_STATUS.md` with evidence and the next exact action. Use the installed frontend-design and UI UX Pro Max skills as guidance within the project brief.

## 4. Rejection criteria

- A single hardcoded game/default draft replaces the personal collection, or unrelated models inherit Nine Lives facts.
- Project switching loses edits; identical template copies share mutable state; late responses change a different project.
- A user's archive or browser state is visible to another identity, or share/invite/publish controls appear now.
- Wholesale CSS/framework rewrite, generic decorative dashboard or invented measurements.
- Nine Lives reference rendered as 5×3/paylines; unsupported reel/paytable structures fabricated.
- Backend math, seeded behavior, exports or feature flags weakened to fit a UI.
- Exact/Sampled shown without current numeric evidence; targets presented as computed actuals.
- Screenshots, cleanup, tests, performance, user acceptance or release readiness claimed without relevant current evidence.
- Complete suites run on every iteration, unrelated tests rerun after small fixes, or documentation changes trigger testing.
- Unrelated work overwritten, or code pushed/deployed without authorization.

## 5. Sources and installation notes

- [UI UX Pro Max (upstream CLI usage)](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill#installation)
- [Anthropic frontend-design SKILL.md](https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md)
- [OpenAI frontend testing/debugging guide](https://github.com/openai/plugins/blob/main/plugins/build-web-apps/skills/frontend-testing-debugging/SKILL.md)

The external skills are third-party files/code; inspect content and dependency changes before installing. No skill guarantees senior-level output without product-specific acceptance criteria and screenshot review.
