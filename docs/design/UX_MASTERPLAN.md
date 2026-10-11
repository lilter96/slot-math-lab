# Slot Math Lab — UX/UI Redesign Masterplan

**Version:** 1.1 / 2026-10-12
**Product direction:** **Precision Workbench — a mathematical instrument for slot-game design**  
**Project:** https://github.com/lilter96/slot-math-lab  
**Status:** product/design requirements updated for private multi-project workspaces. Implementation evidence is tracked in `IMPLEMENTATION_STATUS.md` and `FINAL_QA_REPORT.md`; no deployed-design claim.
**Audience:** senior frontend engineers, product designers, Codex implementation agent, QA reviewers.

> **Product promise:** Define the math. Run a spin. Explain every result.
>
> **North-star interaction:** From a paid-round result, the user can navigate to the exact rule or model parameter responsible for it, without losing game context or falsifying the provenance of statistical evidence.

---

## 0. Current product scope — private multi-project workspaces

One user may own many projects, games, models and personal materials. Many users use the application independently. The entry point is **My projects** (`/projects`), including an empty state for a new account. Opening a project leads to that project's Overview and editing surfaces. No game is silently created on first open.

- Create a blank project, import personal JSON, or create an independent copy of a template. Search, open, duplicate and rename personal projects.
- Preserve each project's canonical model, edits, layout, open subgraph and supported authoring state when switching. A project identity is separate from its authored model ID: two copies of the same game remain independent.
- Isolate browser workspaces by verified user identity. Enforce ownership of server configurations, runs, experiments and realtime subscriptions; another user's IDs do not grant access.
- Browser drafts autosave locally; server version saving is explicit. Opening a server version creates an editable local copy without changing pinned evidence. Automatic cross-device draft synchronization is not implemented.
- **No sharing or collaboration now.** Public links, invitations, teams and publication are deferred. Legacy inbound `?load=` is import compatibility; private run bookmarks are owner-only.
- Nine Lives is one reference fixture, not the default project or a universal game schema. Read geometry, symbols, mechanics and editor capabilities from the selected model. Nine Lives reference is 6×5; other games can differ. Public templates are examples copied into a private workspace.

This clarification supersedes single-game entry assumptions and sharing suggestions elsewhere in the original proposal. The local autonomy directive in `CODEX_AUTORUN.md` supersedes historical between-phase review pauses. Test selection follows `AGENTS.md`: new/affected tests during ordinary iterations, full suites after a substantial integrated feature or an explicit user request.

## 1. Context & evidence

### Sources inspected
- Screenshot of the actual 1489×1024 Build page, dated 2026-10-11.
- Repository `README.md` and `docs/PRD.md`.
- `frontend/src/App.tsx`, `components/Layout.tsx`, `components/Palette.tsx`, `components/MetricStrip.tsx`, `components/RightPanel.tsx`, `components/editor/ModelNavigation.tsx`, `components/editor/ProjectDataEditor.tsx`, `components/canvas/SlotCanvas.tsx`, `src/lib/projectFiles.ts`, `src/store/index.ts`, `src/index.css`, `frontend/package.json`.
- `frontend/src/games/nine-lives/manifest.ts`: Nine Lives is a **6×5** grid with `cluster-pays`, `cascade`, `free-spins`, `buy-bonus`. Do **not** show it as 5×3/paylines.
- Present pages: Build, Simulate, Results, Export, optional Play; existing model library and integrated Playwright.

### Critical observations (verified code + screenshot)
1. **Hierarchy is implementation-driven.** 21 editable subgraphs are displayed as a multirow collection of technical chips above the canvas. Useful game concepts are hidden behind names such as `capture-scatters`.
2. **Screen area is allocated poorly.** Fixed 360px inspector remains visible with no selection; the 67% canvas zoom is tiny; subgraph navigation consumes vertical space. Existing `FitLoadedGraph` uses `fitView({ padding: 0.15, maxZoom: 1 })`, so the problem is partly layout, not lack of zoom logic.
3. **Library and palette are confused.** `Palette` combines `StarterLibrary` examples, primitives, catalog components, and plugins in one sidebar. Distinct user tasks compete.
4. **Project data editing is technically generic.** `ProjectDataEditor` edits state/array fields, but lacks dedicated table UX for reel strips, symbol metadata, and payouts.
5. **Metric provenance is misrepresented when absent.** `MetricStrip.tsx` uses an `Exact` badge fallback when metrics `m` are undefined; screenshot shows EXACT beside dashes. Fix before any redesign polish.
6. **Existing functionality must be preserved.** `ReactFlow`, named subgraphs, `rootProject()`/`openMechanic()`/`closeMechanic()`, `@playwright/test`, Results archive, comparison/verification, optional feature flags, seeded graph execution, provenance and exports exist. Do not reimplement them blindly.
7. **Nine Lives example is explicitly uncalibrated.** The present library says example RTP is uncalibrated. No sample target RTP, maximum payout or hit rate may be displayed as computed evidence.

### Known unknowns — discovery tasks, not assumptions
- Inventory every game-model schema and how reel strips/paytables are actually represented (some may use generic state arrays rather than a canonical `reelSets` field).
- Determine real abilities and limits of current game presentation and deterministic replay endpoints.
- Audit current persisted draft format, scenario hashes and migration hooks.
- Validate mobile feasibility for **authoring**, not only navigation; default to a view/read-only fallback below tablet breakpoint if full editor cannot be used.
- Ask at least 3 relevant practitioners (slot mathematician, game backend developer, math reviewer) to attempt core workflows; treat their feedback as validation, not as a prerequisite to the first engineering slice.

---

## 2. Target users, jobs and UX success

| Persona | Main job | Pain today | Success scenario |
|---|---|---|---|
| Slot mathematician | Tune model, understand RTP/win behavior | Graph first, low data density where needed | Edit a symbol/payout value, validate it, run evaluation and compare evidence |
| Slot backend engineer | Inspect unusual mechanics, reproduce a game | Context scattered across subgraphs/pages | Find `free-spins`, drill into source graph, replay a seed and see matching state transition |
| Lead mathematician / technical director | Review mathematical assumptions and release evidence | Hard to judge what is calculated vs estimated | See provenance, parameters, run metadata and downloadable reproducibility data in ≤3 interactions |
| New contributor | Understand architecture and make safe changes | Technical terms everywhere, poor onboarding | Locate mechanics, edit bounded flow, pass validation and tests |

**Primary goal for v1:** A user can find or create a personal project, understand its game architecture and open a meaningful work surface in **≤30 seconds**. Use Nine Lives and another game as fixtures. Target to validate in usability tests, not a claimed measured metric.

**Success measures (targets; instrument before claims):**
- ≥4/5 testers identify where to edit game math and where to run an evaluation within 30 s.
- Graph canvas visible width is ≥65% of content region when inspector closed at 1440px viewport.
- No falsely labeled Exact/Sampled status in empty/loading/offline states.
- 100% preservation of model round-trip tests, seeded parity and key regression flows.
- 0 unexplained horizontal document overflow at 1440×900, 1280×800, 1024×768 and 390×844; touch fallback is explicitly defined.
- Accessibility: WCAG 2.2 AA-oriented testing, semantic navigation, focus states, tooltips not sole sources of essential info, reduced-motion support.

---

## 3. Design direction and self-critique

### Chosen: Precision Workbench
A specialized engineering workspace: calmly dense, exceptionally legible, mathematical provenance first. Visual identity comes from the shapes of slot reels, outcomes, graph dependencies and confidence intervals, **not** casino/neon visual clichés.

**One memorable element:** a **live game-to-evidence bridge**: the selected symbol, mechanic or winning event is highlighted consistently in the preview, table, graph and evidence view. Do not scatter animation across the interface.

### Rejected alternatives
- **Casino Neon:** eye-catching but undermines mathematical credibility and makes precise data harder to read.
- **Generic Analytics SaaS:** giant KPI cards, gradients and ornamental badges give the same UI for every domain and waste space.
- **Pure IDE clone:** excellent for engineers but confusing for mathematicians who need reels/payout tables first.

### Revision after critique
- Keep the existing cool dark palette DNA and IBM Plex typography; refine the hierarchy instead of redesigning for novelty.
- Different workflows use different density: graph gets spatial canvas; tables get compact rows; results get large plots. Do not force a bento-grid template on all pages.
- A game is not always based on physical reels/paylines. The UI **conditionally shows relevant editors** based on model capabilities and never silently invents domain schema.

---

## 4. Navigation and information architecture

### Primary UI (desktop)
**Global top bar (52–56px):** brand · personal project switcher (selected project name) · unsaved/saved state · `Validate`/`Run analysis` and context action · account/help. Do not keep a global Exact/ε/Sampled legend when no evidence is visible; show provenance in context.

**Workspace sidebar (220–252px, collapsible):**

```text
MY PROJECTS                 [open]
SELECTED PROJECT            [switch]
  Overview
  Model
    Symbols & reels       [if supported]
    Pays & rules          [if supported]
    Features
    Logic graph
  Evaluate
    Simulation
    Results & evidence
  Play & debug            [if enabled]
  Export

+ Create / Import game
```

**Navigation adaptation:** preserve `/build`, `/simulate`, `/results`, `/export`, `/play` for existing inbound links. New nested routes or query-scoped views may be introduced with redirect/alias behavior. Do not break `?project=nine-lives`, `?load=...`, deep links to runs, feature gates or archive routes.

**Keyboard-centric advanced entry:** command palette `⌘/Ctrl+K` or searchable tree: Find mechanic, Open graph, Start simulation, Go to run. Keep technical `id` in secondary text and user-facing label as primary text.

### Workspace layouts
**My projects**: personal draft collection, search, template copies, blank/JSON import, saved private server versions.

**Overview**: selected-game image/interactive preview + model facts + features list + last verified result. **No forced real-time full analysis on first paint.**

**Tabular authoring**: domain tree/filter left; real table center; inline validation / restrained inspector to the right when requested.

**Graph authoring**: collapsible mechanic explorer (left), max-size canvas (center), contextual inspector drawer (right, starts closed). Bottom evidence/status strip only when relevant; never visually covers graph controls.

**Results**: keep existing archive/evidence facilities, simplify hierarchy and maintain deep links; not a new dashboard disconnected from real runs.

**Play & debug**: Debug executes the selected model and shows actual returned state; use a board presentation only when a compatible trace exists. Existing `/play` remains explicitly labelled Reference demo. Trace features are gated by backend support (see Phase 6).

### The first 30 seconds
1. Open **My projects**; find a personal project or create a blank/template copy. A fresh account has no automatic game.
2. Open that project's **Overview**. Facts describe the selected model: Nine Lives reference is 6×5 with clusters/cascades/free spins; Ancient Dragon is 5×3. Unsupported geometry stays explicit.
3. Primary choices: **Edit math**, **Explore mechanics**, **Test spin** (when supported/enabled), **Run analysis**.
4. If no valid completed result belongs to this model, show `No verified result yet` + `Run analysis`; do not reuse another project's evidence.
5. Open a feature's readable summary or underlying graph. Return to My projects and switch without losing edits.

---

## 5. UX wireframes (not final pixels)

### A. Overview · 1440px desktop (Nine Lives reference project selected)

```text
┌─────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│ SML  ▾ Nine Lives                              Draft saved                 Validate   [Run analysis] │
├──────────────────┬──────────────────────────────────────────────────────────────────────────────────────┤
│ Overview ●       │ Nine Lives                      6 × 5 · Cluster wins · Cascades                │
│ Model            │                                                                             │
│  Symbols & reels │ ┌──────────────────────────────────────┐ ┌────────────────────────────────────┐ │
│  Pays & rules    │ │                                      │ │ Model overview                    │ │
│  Features        │ │    [REAL GAME PREVIEW or art]        │ │ Cluster pays · Cascades           │ │
│  Logic graph     │ │                                      │ │ Free spins · Bonus buy            │ │
│ Evaluate         │ │   [Test spin]  [Inspect game]        │ │ [Explore mechanics]              │ │
│  Simulation      │ └──────────────────────────────────────┘ └────────────────────────────────────┘ │
│  Results         │                                                                             │
│ Play & debug     │ Last verified run   [if evidence exists]   /   No verified run — Start one  │
│ Export           │ ┌───────────────────┬───────────────────┬────────────────────────────────────┐ │
│                  │ │ RTP + provenance  │ Hit frequency     │ Run date / sample count / seed     │ │
│                  │ └───────────────────┴───────────────────┴────────────────────────────────────┘ │
└──────────────────┴──────────────────────────────────────────────────────────────────────────────────────┘
```

### B. Logic Graph · responsive workbench (example selected project)

```text
┌─────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│ ▾ Nine Lives / Model / Logic graph                              [Validate] [Run analysis]              │
├───────────────┬────────────────────────────────────────────────────────────────────────────┬─────────────┤
│ Find mechanic │  Free spins  /  13 nodes                                  Fit  100%  ⤢ │  INSPECTOR  │
│               │                                                                            │ [only when │
│ ▸ Base spin   │        [ Trigger ] ────── [ Award spins ] ────── [ Loop ]                 │  selected] │
│ ▸ Cascades    │                                  │                                         │             │
│ ▾ Bonuses     │                        [ Retrigger condition ]                            │             │
│  • Free spins │                                                                            │             │
│  • Coin prize │                                                                            │             │
│               │                                                                            │             │
│ [+ New mech.] │                                                                            │             │
├───────────────┴────────────────────────────────────────────────────────────────────────────┴─────────────┤
│ Validation: 0 errors             Preview: not evaluated         [Show evidence]    [Collapse]            │
└─────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

### C. Mathematical data table

```text
┌─────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│ Model / Symbols & reels        [Reel set ▾]  [Import CSV]  [Compare version]              [Save]           │
├──────────────────────────────┬──────────────────────────────────────────────────────────────────────────┤
│ REELS                        │ Reel 1   Reel 2    Reel 3  ...  Reel 6            SYMBOLS / VALIDATION   │
│  Base set ●                  │  W       C         A            B                 [counts + filters]    │
│  Free-spins set              │  A       W         Q            A                 [warning details]     │
│                              │  ...                                                  [preview stop]    │
│ PAYOUTS                      │                                                                    │
│  Cluster pays                │ Remember: order and adjacent windows matter, not just frequencies.       │
└──────────────────────────────┴──────────────────────────────────────────────────────────────────────────┘
```

**Conditional model notice:** If a specific game does not expose physical strips, show an appropriate weighted-draw or symbolic-state editor with a clear explanation. Do not fabricate reels just to fill the screen.

---

## 6. Visual system — v1 spec

### Typography
- **IBM Plex Sans** for navigation, text, tables, forms and labels (already configured in `src/index.css`); **IBM Plex Mono** for seeds, hashes, values, graph IDs, diffs. Ensure fonts load or system fallbacks are safe.
- Body: 14px/21px; utility: 12px/17px; section title: 17–20px/25px; project heading: 27px/34px; numeric value: 26px/31px (only where metrics truly exist). Do not use all-caps eyebrow labels everywhere.
- Numeric tables: `font-variant-numeric: tabular-nums`; right-align amounts, left-align labels.

### Color proposal (validate contrast with actual font sizes)
| Token | Hex | Role |
|---|---|---|
| `--sl-bg` | `#0F141B` | full app backdrop |
| `--sl-panel` | `#18212C` | panels & editor shell |
| `--sl-elevated` | `#22303D` | active controls, overlays |
| `--sl-line` | `#344454` | structural divisions |
| `--sl-text` | `#EDF4FB` | primary text |
| `--sl-muted` | `#AFC0D0` | secondary text |
| `--sl-exact` | `#44D6AD` | **only** confirmed exact value / success when appropriate |
| `--sl-sampled` | `#75B9FF` | sampled estimate and interval |
| `--sl-bounded` | `#F3BE68` | ε-pruned/bounded/provisional |
| `--sl-danger` | `#FF8191` | destructive/error |

**Semantic principle:** a status is a **label and method**, not a colored dot. `Exact`, `Sampled · n=1M`, `Bounded · ε≤...`, `Not evaluated`, `Calculating`, `Failed`, `Needs full run` have distinct icons/text and accessible descriptions. `Exact` must never be a default when data is absent.

### Layout & density
- Topbar 54px. Main nav collapsible 232px. Standard content gutter 24px desktop, 16px medium, 12px compact.
- Inspector width 320–380px, mounted **only when needed** or explicitly pinned. Persist user preference.
- Graph zoom: fit to selection/layout with minimum **legibility target**; do not simply zoom beyond entire graph width. Present readable zoom and `Fit graph` action; preserve per-mechanic camera state.
- Tap/click targets: ≥36px desktop and comfortable touch targets where touch editing is supported.
- Transitions: 120–180ms for user-triggered state changes. Honor `prefers-reduced-motion`.
- Data dense where meaningful: no excessive card nesting, no repeated heavy borders, no gratuitous gradients.

### Shared component inventory
`ProjectSwitcher`, `WorkspaceSidebar`, `PageHeading`, `ContextActions`, `EvidenceBadge`, `MetricWithProvenance`, `RunStatus`, `MechanicTree`, `SearchCommandPalette`, `ContextInspectorDrawer`, `ResizableSplitPane`, `EmptyState`, `InlineValidation`, `DataGrid`, `StatusFooter`, `GamePreview`, `UndoRedoControls`, `ConfirmUnsavedChanges`.

**Rule:** prefer existing semantics/components over adding a broad new UI framework. Avoid replacing existing React Flow, React Router, Zustand, CodeMirror or Playwright stack.

---

## 7. Screen requirements & behavior contracts

### 7.1 Overview (new)
**Purpose:** immediate understanding of the selected project and next action. Use compatible real game assets when available; otherwise a clearly labelled model-derived diagram or safe static fallback. Never apply Nine Lives facts to unrelated models.
- Show model attributes **derived from manifest/config**, not hardcoded.
- Show feature summary grouped by player-facing semantics, e.g. Cluster wins, Cascades, Free spins.
- Last analysis: value(s) plus `runId`, method, seed, sample count and date when applicable; otherwise explicit empty state.
- Actions: Open data, Open feature, Test spin (only with enabled Play), Run analysis.
- Project switching preserves each draft; example import creates a separate private project. Same-template copies have different project identities, retain independent edits and do not share version histories.

### 7.2 Logic graph (revise)
- Replace 21 top chips with hierarchy/search in mechanic explorer. Keep raw ID discoverable via tooltip/detail.
- Collapse or detach palette into `Add node` command; do not mix example libraries and primitives.
- Hide inspector when no node selected; user may pin panel.
- `Fit graph`, `Fit selection`, `100%`, zoom, minimap as optional controls.
- Drill into mechanics and breadcrumb back; switching cannot discard edits.
- `Validation error` links to affected node/edge and zooms it into view.
- Preserve all node/edge and export serialization semantics.

### 7.3 Symbols & reels (progressive enhancement)
- Capability detection first: *reel strips*, *weighted outcomes*, or *symbolic state*; the supported editor changes accordingly.
- For reels: column-based strips, index, symbol badge, counts and cumulative probabilities; add/remove/reorder through keyboard too; warning on invalid empty/unknown symbols; CSV import preview.
- For weighted model: value/weight table with validation, normalization preview and event probabilities.
- Editing is transactional: draft change → validate (debounced) → commit with Undo; no silent mutation of unspecified fields.
- Data mappings must be explicit and round-trip-tested on Nine Lives fixtures and a simpler example. Also verify switching between different games and independent copies; unsupported mappings must preserve raw data.

### 7.4 Pays & rules (progressive enhancement)
- Render paytable/cluster rules only where data schema is explicit.
- Matrix/table with sticky labels, numeric input precision and validation warnings. Do not assume 3/4/5-payline rules for Nine Lives' cluster model.
- Show exact affected parameters and difference summary before save.
- Never claim instantaneous full RTP on every keystroke; use cached light evaluation with visible `calculating`, `too expensive`, `failed`, bounded/exact/sampled provenance.

### 7.5 Features
- Catalog cards/tree are *user-facing* (Cascades, Free spins, Bonus buy). Open inspector with feature **readable summary** and `Edit parameters` when schema supports it; `Open logic graph` always available.
- Do not attempt universal free-form graph↔form round-trip in first release. Form editors are **capability-gated** to explicitly mapped known mechanic patterns; unsupported changes route to graph rather than losing nodes.
- All feature changes validated and round-trip-tested.

### 7.6 Simulation / Results
- Existing simulation/evidence UI remains the functional source. Improve filtering, headings and status grouping without weakening mathematical content.
- Evidence trust contract: display method and sample/budget/ε, confidence interval where applicable, max-win provenance (observed vs proven bound vs unknown).
- Distinguish **configured target** vs **calculated actual**; don't show a target as calculated evidence.
- On Compare, preserve seed, params, model hash, sample counts and CI — difference between two Monte Carlo point estimates is not definitive without uncertainty.

### 7.7 Play / Debug
- **Stage A:** Debug executes the selected project through existing paid-round endpoints and shows actual returned state. Reuse a compatible board-history adapter when available. The separate Nine Lives Play surface remains a labelled reference demo, not a universal project preview.
- **Stage B (requires instrumented backend):** trace stream format with event ID, round ID, source node/mechanic reference, state delta, RNG stream/seed mapping where safe, payout component, timing. Store or regenerate required deterministic state.
- Replay of same seed is only claimed if all relevant inputs, engine version and stream mapping are pinned and validated.
- Link trace event to graph when source mapping exists; show `Not available` otherwise.

### 7.8 Export
- Preserve current files/endpoints. Export includes provenance metadata and version, and never exports a fake analysis.

---

## 8. Data architecture & mathematical correctness

### Single source of truth
- `GraphConfig`/AST + table data remain canonical. No separate editable copy for each presentation.
- A read-only `ModelProjection` adapter derives view models for Overview, mechanic tree and data editors; a controlled `ModelCommand` layer produces validated updates. Consider this interface proposal, not a request to replace backend types.
- All write commands must preserve unknown fields, plugin references, IDs, typed expressions, edge ports, shared mechanics, and schema version. If a mapping cannot be safely round-tripped, **display read-only** with link to graph.
- No reverse transforms from rendered pixels/UI labels; use stable IDs.
- Draft management/restore and navigation into nested mechanics must preserve the `graphTrail` semantics. Diff against `peekRootProject()` prior to applying. Use project identity, not authored graph ID alone, to scope editor buffers, camera state and asynchronous validation. Late responses must not mutate another project or publish its evidence.

### Provenance and missing data
- Data model: `unavailable | loading | error | exact | bounded | sampled | needsFullRun` as explicit UI states, with numeric payload only when present.
- Fix known `MetricStrip` fallback (`Exact` beside empty metrics) as first engineering issue.
- No fabricated sample values, fake live metrics, approximated max win labeled exact or misleading 'success' chips.
- If no verified RTP, present `Not evaluated` and a call to action.

### Performance
- Render graph nodes only when visible; avoid recomputing all charts on every textarea keystroke.
- Debounce validation/preview and cancel obsolete requests; protect UI from state changes while expensive work is running.
- Avoid growing mega-components; progressively extract shell/navigation, evidence primitives and editor adapters.
- Budget proposal (measure baseline first): initial interactive Overview ≤2s on development-class hardware after data available; graph interaction at target ≥50fps on the Nine Lives full graph; benchmark rather than claiming achieved performance.

---

## 9. Shipping roadmap — small, reversible PRs

### Phase 0 — Discovery + test baseline (PR 0)
**Scope:** collect screenshots of each current surface at desktop and tablet; map routes, state ownership, feature flags, model schemas, current tests. Prepare `design-system/MASTER.md` and `docs/design/...` checked into repo *after review*.

**Outputs:** route inventory, component audit, before screenshots, model capability table, list of data-contract hazards, P0 bug list.

**Gate:** baseline frontend build/lint/units/critical E2E and backend compilation/tests documented with known pre-existing failures. No design work yet.

### Phase 1 — Trust & visual foundation (PR 1, **START HERE**)
**Scope:** semantic provenance fix for missing/loading/error metrics; shared UI tokens; workspace shell and navigation semantics; no new business behavior.

**Files likely:** `index.css`, `components/Layout.tsx`, `components/MetricStrip.tsx`, `components/ProvBadge.tsx`, new shell components. Audit actual source before editing.

**Gate:** project switcher preserved; keyboard nav; all existing routes resolve; metrics never claim Exact absent evidence; screenshots 1440×900/1280×800/390×844; old tests pass.

### Phase 2 — Overview & mechanic explorer (PR 2)
**Scope:** personal project collection and model-derived Overview; correct facts for each selected game, including Nine Lives reference 6×5; real/empty last run state; expandable `MechanicTree` replacing top-chip navigation; deep links maintained.

**Gate:** create/open/switch/duplicate/import personal projects; Overview navigates to graph, data, results and enabled Debug; no hardcoded fake RTP or game facts; return from mechanics and project switching preserve independent drafts; private owner isolation is verified.

### Phase 3 — Graph workbench (PR 3)
**Scope:** move library examples out of primitive palette; context-only inspector; full usable canvas; legible fit & per-mechanic camera; search and breadcrumbs; validation focus.

**Gate:** click/drag/connect/edit nested subgraph and return round-trips unchanged; no editor overlap; graph keyboard focus; screenshot/DOM interaction evidence.

### Phase 4 — Real data editors (PR 4–5)
**Scope:** first capability-detected **weighted table / symbols** (choose a capability actually present in the selected model; Nine Lives is a regression fixture), then reel strips and paytables on known schema. CSV import only after validation and confirmation UX.

**Gate:** data changes round-trip without losing unknown mechanics and edge mappings; seeds and core math results unchanged unless intended parameter changed; input validation and undo tested.

### Phase 5 — Features abstraction (PR 6–7)
**Scope:** read-friendly feature summaries first; one bounded rule form second (prefer cascade or free-spins pattern with proven mapping); graph escape hatch.

**Gate:** a graphic/form round-trip on chosen fixture; unsupported models remain readable and editable in graph only.

### Phase 6 — Evidence and debugger (PR 8+)
**Scope:** enhance RTP anatomy from existing result attribution if supported (otherwise specification/measurement first); Play Trace with explicit backend instrumentation as a separate engineering RFC. Scenario Lab later.

**Gate:** no invented attribution; sums & overlap methodology documented; event-to-graph mapping validated on seeded replay; test evidence.

### Phase 7 — Release hardening
**Scope:** cross-browser interactive tests, keyboard and accessibility audit, performance measurement, feature flag rollout, backend/frontend compatibility, migration/rollback plan, docs.

**Gate:** no release until build, tests, math verification, screenshots, security constraints and user acceptance pass. Staged release + rollback.

### MVP demo vs roadmap
**Demonstrable redesign MVP = PR 1–3**: trustworthy evidence states + navigable Overview + professional graph workbench. **Do not** block these on implementing a full auto-tuner, DSL or mathematical state debugger.

---

## 10. Acceptance test scenarios

1. **First open**: My projects shows the current user’s collection or empty state. Explicitly creating/opening Nine Lives reference shows 6×5; opening another game shows its own geometry/mechanics. No invalid `EXACT` badges or invented RTP when backend is idle/offline.
2. **Route survival**: `/build`, `/simulate`, `/results`, `/export`, and feature-gated `/play` still behave correctly; deep linked `?project=nine-lives` remains functional.
3. **Explorer**: search `free spins`; open nested graph; go back; edit persists; no lost node/edge.
4. **Canvas**: no selected node → no wasted 360px inspector; selection opens panel; `Fit graph` yields legible controls and safe interactions.
5. **Data edit**: change one known weight in a supported model; validate; export and reload; no unrelated graph changes.
6. **Evidence**: run pending → no green Exact; completed sampled run → `Sampled`, N & CI; exact → Exact only with evidence; bounded → explicit ε or bound; failure → concrete next step.
7. **Feature flags**: disabled Play/AI/plugins are not leaked in navigation; auth guards unchanged.
8. **Saved state**: importing a template creates an independent project; switching different games and identical copies preserves edits, layout and nested context; reload/export/restore work.
9. **Keyboard**: focus rings, keyboard tree navigation, ESC close, dialogs restore focus; reduced-motion state tested.
10. **Viewport**: 1440×900, 1280×800, 1024×768 and 390×844. Mobile exposes a usable read-only or minimal editing experience if advanced graph cannot fit.

11. **Private ownership**: two authenticated users cannot list/read/update/delete each other's configurations, runs or experiments, subscribe to their realtime evidence, or hydrate each other's browser workspace. Trusted workers can complete their own admitted work.
12. **Persistence and scope**: local autosave failures preserve the in-memory graph and offer a backup. Explicit server save appears in the owner's archive; opening it makes a separate draft. There are no share/invite/publish controls.

**Available commands, not a per-iteration checklist** (from `frontend/`): `npm ci`, `npm run build`, `npm run lint`, `npm run test:unit`, `npm run test:e2e:critical`. From `backend/`: `dotnet build`, `dotnet test`. For routine work select new/affected tests under AGENTS.md; full suites are reserved for a substantial integrated feature or an explicit user request. Respect existing scripts/environment and document infrastructure failures rather than hiding or force-skipping them.

### Screenshot QA evidence expected per PR
- Before screenshot(s), after screenshot(s), selected node and empty panel state, off-line/no-metrics state, focus/keyboard view, responsive view.
- Track mismatches to brief: `Expected → observed → corrected / intentional deviation`.
- Visual sign-off by human after reviewing screenshots, **before** broad next phase.

---

## 11. Explicit non-goals and red lines

- No wholesale backend rewrite or second canonical game representation.
- No new design library/Tailwind rewrite for aesthetics alone.
- No hardcoded fake measurements, max win, RTP or Exact labels.
- No destructive schema migrations or auto-push-to-main without review.
- No universal arbitrary graph↔form conversion unless mappings are proven lossless.
- No hiding advanced mathematical metadata to make a screenshot 'pretty'.
- No ignoring failing math correctness tests or deleting QA to make CI green.
- No using `public Play` as proof that a certified production casino engine exists. The repo describes a research/portfolio prototype.

---

## 12. Decision record for implementation agent

- **D-01:** Preserve current high-value engine and domain semantics, redesign presentation/navigation.
- **D-02:** Semantic status integrity is P0 and comes before styling.
- **D-03:** Nine Lives fixture must be **6×5** and **cluster/cascade**, not generic 5×3 line slot.
- **D-04:** One structural source of truth for model edits, capability-gated specialized editors.
- **D-05:** Keep CodeMirror / React Flow / router / Zustand / Playwright; reuse with narrow refactors.
- **D-06:** Keep local commits scoped to coherent slices with screenshots and relevant checks; no push/deployment without authorization.
- **D-07:** Many private projects per user, many independent users; My projects is the entry point. Sharing/collaboration are deferred.
- **D-08:** Respect the mandatory AGENTS.md test-frequency policy; no complete suites on every iteration.

**Continuation task:** Read this masterplan, `DESIGN_SYSTEM.md`, `CODEX_HANDOFF.md` and `IMPLEMENTATION_STATUS.md`. Audit current evidence, then continue unfinished authorized local work. Historical phase-review pauses do not override the user’s autonomy directive; keep release blockers explicit.

