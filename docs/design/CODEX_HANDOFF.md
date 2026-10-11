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

## 2. Start Codex from the repository root

Before starting, put documents in repo:

```bash
mkdir -p docs/design
cp /path/to/downloaded/*.md docs/design/
# Review diffs; commit design documents on your own branch if desired.
```

Launch Codex from repo root in **higher reasoning/plan mode if available**. The following is the opening prompt (copy all):

```text
You are the principal product designer, senior frontend architect and QA lead for Slot Math Lab.
Read first, in full:
- docs/design/UX_MASTERPLAN.md
- docs/design/DESIGN_SYSTEM.md
- docs/design/CODEX_HANDOFF.md
- README.md
- docs/PRD.md

Activate/use installed UI UX Pro Max and frontend-design skills. Where external skill suggestions conflict with this product-specific design brief or math correctness, the repo requirements and brief win.

Task: modernize the existing React 19/Vite/TypeScript Slot Math Lab into a precise, domain-first mathematical workbench. THIS IS NOT A NEW LANDING PAGE. Preserve and reuse React Flow, Zustand, React Router, existing Playwright infrastructure, exports, backend contracts, feature flags, graph editor and mathematical provenance.

IMPORTANT REAL FACTS:
- Nine Lives is a SIX-REEL FIVE-ROW cluster-pays game (6×5), with cascade, free spins and bonus buy.
- The current prototype library says the sample RTP is uncalibrated. Never invent a verified RTP, win frequency, max win or results.
- Existing MetricStrip can show an Exact status when values are unavailable; fix semantic validity early.
- Current editing/nested subgraph state must survive navigation and serialization.

WORK IN ITERATIONS:
1) PHASE 0 ONLY: Inspect the actual code, data model, tests and screenshot references. Summarize findings, dependencies, risks, design decisions and discrepancies against this brief; list exact components/paths to change. Show a small implementation plan by PR. DO NOT modify source code before presenting the audit.
2) Then implement PHASE 1 only after the plan is approved by the user: semantic data-state integrity + design tokens + responsive shell/nav, preserving all existing functionality.
3) Run frontend build, lint, units, critical Playwright E2E; run backend tests if touched or needed to verify integration. On failures distinguish pre-existing/environmental issues from regressions.
4) Start local app and use a real browser or Playwright to validate desktop and small viewport. Capture visual screenshots with evidence, test keyboard navigation, status empty state, old routes, feature gates, nested mechanics and selected-node inspector.
5) Produce a QA report: screenshot before/after, tests, failures, trade-offs, remaining risks. Stop for review before Phase 2. Never push to main or deploy without explicit request.

DESIGN STANDARD:
- No 21 unstructured subgraph chips at the top of the final graph UX.
- No always-open 360px empty inspector.
- No fake Exact/Sampled statuses.
- No generic bento-card/gradient dashboard or neon casino UI.
- Primary focus: Nine Lives Overview, feature navigation, usable graph workspace and honest evidence.
- Preserve `?project=nine-lives`, existing run deep links, Play flag and full data round-trip.
- 1440x900, 1280x800, 1024x768, 390x844 screenshots.

Start with Phase 0 audit and a short design-choice critique. Do not skip it.
```

## 3. Follow-up prompts by scope (send after review)

### PR 1 — shell & trust
```text
Proceed with Phase 1 / PR 1 from docs/design/UX_MASTERPLAN.md only. Implement semantic MetricStrip provenance states and responsive workspace shell. Maintain backwards-compatible routes and behavior. Create focused tests. Verify screenshots and keyboard interaction. Do not start Phase 2.
```

### PR 2 — Nine Lives overview + tree
```text
Proceed with Phase 2 / PR 2 from docs/design/UX_MASTERPLAN.md only. Build an evidence-honest, truly 6×5 Nine Lives Overview. Replace chip clutter with hierarchical MechanicTree and preserved graph drilldowns. Verify draft preservation, deep links, responsive screens and Play feature flag. Screenshot review gate before next PR.
```

### PR 3 — graph workbench
```text
Proceed with Phase 3 / PR 3 from docs/design/UX_MASTERPLAN.md only. Refactor the graph editor's layout without replacing React Flow. Hide inspector unless selected/pinned. Separate starter games from node palette. Improve fit, search, breadcrumbs, validation focus and no-overlap layout. Preserve round-trip semantics and take screenshots at required viewports.
```

### PR 4 — first real math table
```text
Proceed with Phase 4 only after inspecting Nine Lives model data shape. Implement one capability-detected tabular editor for a model capability that is actually present (weighted draws/symbols/reel strips, whichever source data supports). Do not fabricate a payline or reel-strip schema. Provide stable mapping, undo, validation, import/export round-trip tests and browser screenshots. Stop after one vertical slice.
```

## 4. Red flags that mean reject Codex's PR

- Entire `index.css` blindly replaced with Tailwind components or unrelated framework.
- Hero/card dashboard that looks generic or uses artificial metrics.
- Nine Lives incorrectly rendered 5×3/payline instead of 6×5/cluster.
- Changes `GraphConfig` in a way that breaks models, exact math, seeded behavior, drafts or exports.
- Status badges claim Exact/green with no numerical result.
- No screenshots, only 'looks good' in textual claims.
- Tests skipped or failing without analysis.
- Agent tries to implement all seven roadmap phases in one branch.

## 5. Sources and installation notes

- [UI UX Pro Max (upstream CLI usage)](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill#installation)
- [Anthropic frontend-design SKILL.md](https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md)
- [OpenAI frontend testing/debugging guide](https://github.com/openai/plugins/blob/main/plugins/build-web-apps/skills/frontend-testing-debugging/SKILL.md)

The external skills are third-party files/code; inspect content and dependency changes before installing. No skill guarantees senior-level output without product-specific acceptance criteria and screenshot review.
