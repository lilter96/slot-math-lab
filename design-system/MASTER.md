# Precision Workbench design reference

Updated 2026-10-12. Read with [DESIGN_SYSTEM.md](../docs/design/DESIGN_SYSTEM.md) and [UX_MASTERPLAN.md](../docs/design/UX_MASTERPLAN.md). These are design requirements; [implementation evidence](../docs/design/FINAL_QA_REPORT.md) records what has actually been verified.

## Product context

Many users each own many private game/model projects. **My projects** is the entry point, with an empty state for a new workspace. Project creation, personal import and template copying create independent identities. Switching preserves canonical edits, layout and nested context. Browser drafts autosave locally; saving a private server version is explicit. No sharing, collaboration, publication, teams or invitations now.

Show the selected project's name and model-derived facts. Nine Lives reference is a 6×5 cluster/cascade fixture; Ancient Dragon is 5×3. Neither defines every game's geometry or editor schema. Templates are uncalibrated examples. Never display a different project's evidence as current.

## Visual identity and tokens

Calm charcoal workspace, IBM Plex Sans for UI, IBM Plex Mono/tabular numerals for numeric evidence, IDs and seeds. Graphs receive space; tables receive compact rows; evidence retains its mathematical detail. Reuse current components and aliases rather than replacing the framework.

Current semantic CSS tokens live in `frontend/src/index.css`:

| Token | Value | Purpose |
|---|---|---|
| `--sl-bg` | `#0f141b` | App background |
| `--sl-panel` | `#18212c` | Navigation and panels |
| `--sl-panel-raised` | `#22303d` | Raised controls |
| `--sl-canvas` | `#0c1117` | Graph background |
| `--sl-line` | `#344454` | Structural divisions |
| `--sl-text` | `#edf4fb` | Primary text |
| `--sl-muted` | `#afc0d0` | Supporting text |
| `--sl-subtle` | `#8ba0b6` | Secondary text; corrected from the initial proposal after contrast review |
| `--sl-exact` | `#44d6ad` | Confirmed exact method |
| `--sl-sampled` | `#75b9ff` | Sampled evidence |
| `--sl-bounded` | `#f3be68` | Bounded/provisional evidence |
| `--sl-danger` | `#ff8191` | Errors/destructive actions |
| `--sl-focus` | `#a5cfff` | Keyboard focus |

Actual method/state labels accompany color. A valid numeric payload and current source are required for an Exact/Sampled/Bounded badge. Targets, absent values, pending work and failures cannot borrow proof labels. Preserve uncertainty, sample count, bounds, seed and model hash when present.

## Interaction and layouts

- My projects: search and useful create/import actions, independent private copies, explicit server archive status and recovery.
- Overview: selected-model geometry/symbols/mechanics, meaningful next actions and honest empty evidence. Coordinate diagrams are labelled; a reference demo is not a universal project preview.
- Graph: searchable mechanic hierarchy, breadcrumbs, separate node palette, readable focus/fit controls and project/mechanic cameras. Inspector stays hidden without selection or explicit pin. Explicit pinning is implemented with a verified-user preference; Close/Escape clear it and project switching clears prior node selection.
- Forms: capability-gated source mappings, validation, explicit commit/diff and Undo. Preserve unknown fields and unsupported state; route advanced edits to the graph.
- Results/Debug: actual pinned calculations and returned state; source links only when the engine supplies a verified mapping.
- Desktop: contextual panels; 1024–1439 uses inspector overlay. Mobile provides useful project/overview/results/forms and a labelled graph movement/connection limitation. Internal scrolling must not cause document overflow.
- Keyboard: visible focus, skip link, predictable navigation, native expandable mechanic details, Escape and focus return. Honor reduced motion. Validate new interaction patterns with affected checks.

Authoring tables now expose persistent user-scoped density, fixed header/primary columns, keyboard row movement and exact raw copy/failure fallback. Interactive desktop rows use 44px Regular / 38px Compact to preserve 36px inputs; phone controls remain ≥44px. This is a documented exception to the initial 32px Compact target. See the maintained [QA summary](../docs/design/FINAL_QA_REPORT.md); detailed table-review output remains local.

## Evidence and remaining acceptance

The recorded bundle passed 64 viewport overflow checks at four sizes; targeted token contrast is at least 5.0069:1. These observations do not establish complete accessibility certification, graph-interaction performance or practitioner acceptance. Detailed historical visual-review output remains local.

Inspector pinning and scoped cross-browser/focus checks have local evidence. Pointer/wheel RAF measurements are local observations, not certified compositor FPS. Overview's matching verified result uses owner/source checks and explicit original run metadata. Visible-node rendering uses React Flow's measured viewport filtering; local checks cover search, cameras and full-model export. Further accessibility and practitioner acceptance remain separate work. Graph-studios cleanup and current Core CI verification are recorded in the [delivery summary](../docs/design/graph-studios/DELIVERY.md); complete roadmap acceptance is not claimed.


The b752c03 legibility follow-up brings audited graph/inspector/data/hint/provenance text to 12px, uses the charcoal tokens in the data dialog, restores keyboard focus and makes unsupported-board layout compact. Local scoped access evidence covers actual states and limits; complete requirement/practitioner acceptance remains unproven.
