# Slot Math Lab — Precision Workbench Design System

**Status:** design requirements updated for private multi-project workspaces (2026-10-12). Current implementation and browser evidence are tracked separately; validate new changes before claiming them verified.
**Companion:** `UX_MASTERPLAN.md`. Current app has a cool charcoal UI, IBM Plex type tokens and per-method provenance colors; evolve them rather than replace them blindly.

## A. Identity: mathematical instrument, not a casino lobby

**Tone:** precise, calm, transparent, capable. Attractive because the workspace is legible and domain-rich, not because it copies generic AI dashboard aesthetics.

**Signature feature:** selected game symbol / mechanic / outcome retains its identity across game preview → editable data → graph → result evidence. This is the single prominent visual expression of the product.

**Avoid:** orange/purple gradient blobs; floating SaaS cards for every datum; ubiquitous micro-badges; random iconography; decorative ticker animations; overuse of monospace and uppercase meta labels; unrelated 'jackpot neon' in engineering panels.

### Workspace context

- The starting surface is **My projects**, with the verified user's personal collection or a useful empty state. A user can own many games/models; many users have independent workspaces.
- Show the selected project's name in the switcher and headings. Create/import/template-copy actions produce separate private drafts; switching preserves edits and nested context.
- Treat project identity separately from model/template ID. Same-template copies must not share staged edits, layout, Debug receipts or mutable analysis state. Saved evidence must match the current source; an owned immutable run for identical serialized input may be referenced explicitly with its original ID/version, following the archive fingerprint contract.
- Use the selected model's geometry, symbols and capability mappings. Nine Lives reference is one 6×5 fixture, not a global layout or default game.
- Public templates become private copies. No sharing, invitations, teams or publication controls now. Saved server versions and run bookmarks remain owner-only.
- Distinguish browser autosave from explicit server saving. Show truthful recovery/backup actions on storage failure; do not imply automatic cross-device synchronization.

## B. Tokens (starting proposal)

```css
:root {
  --sl-bg: #0f141b;
  --sl-panel: #18212c;
  --sl-panel-raised: #22303d;
  --sl-canvas: #0c1117;
  --sl-line: #344454;
  --sl-text: #edf4fb;
  --sl-muted: #afc0d0;
  --sl-subtle: #7f94aa;
  --sl-exact: #44d6ad;
  --sl-sampled: #75b9ff;
  --sl-bounded: #f3be68;
  --sl-danger: #ff8191;
  --sl-focus: #a5cfff;
  --sl-radius-control: 7px;
  --sl-radius-panel: 11px;
  --sl-space-1: 4px;
  --sl-space-2: 8px;
  --sl-space-3: 12px;
  --sl-space-4: 16px;
  --sl-space-6: 24px;
  --sl-space-8: 32px;
  --sl-font-body: "IBM Plex Sans", system-ui, sans-serif;
  --sl-font-numeric: "IBM Plex Mono", ui-monospace, monospace;
}
```

- Preserve existing token aliases (`--bg`, `--exact`, `--sampled`) during migration; use a compatibility layer first. Never sweep-replace `index.css` without visual regression review.
- Don't rely on semantic hue alone: badges have labels and tooltips; contrast must be checked with real surfaces and fonts, including disabled/text-on-accent.
- `--sl-exact` can visually identify **confirmed** exact analysis only. 'Success' and 'exact mathematical proof' may need distinct icons/labels even if related hues are used.

## C. Type scale

| Purpose | Size / line height | Weight | Font |
|---|---|---|---|
| App controls | 13 / 19 | 500 | Plex Sans |
| Primary body | 14 / 21 | 400 | Plex Sans |
| Support/help | 12 / 17 | 400 | Plex Sans |
| Panel heading | 17 / 24 | 600 | Plex Sans |
| Workspace heading | 27 / 34 | 600 | Plex Sans |
| Proven metric | 25 / 30 | 600 | Plex Mono / tabular |
| Editor IDs/seeds | 12 / 18 | 450 | Plex Mono |

Never put tiny 11px faint hints in the main working path. Labels should describe user actions, e.g. `Open free-spins logic`, not `inspect graph fragment`.

## D. Shell & component geometry

- Header: 54px, minimal decoration. Keep one visible primary action.
- Navigation: 232px expanded; collapse to 58px icons with accessible labels; state persisted in session/user preference.
- Content minimum: flex, never a hardcoded desktop-only `width: calc(...)` that clips on small windows.
- Inspector: 344px typical, **hidden until selection or explicit pin**. Resizable if practical; selection state survives panel toggle.
- Background: content panels defined by spacing and subtle planes. Borders only where segmentation is meaningful.
- Table row: 38px normal / 32px compact (user option). Use sticky headers, fixed primary column and keyboard editing.
- Text inputs: >=36px minimum target desktop; clear hover/focus/error states and in-context helper text.
- Graph nodes: header/body contrast, port names legible at standard zoom; edge diagnostics should not depend on color alone.

## E. Math primitives are first-class

### EvidenceBadge
**Exact** — exact rational method confirmed.  
**Bounded** — ε-pruned or rigorously bounded, actual interval visible.  
**Sampled** — include `n`, CI/uncertainty when known; avoid authoritative green.  
**Pending** — calculating, no fake historical measurement.  
**Needs full run** — explain computational budget limitation.  
**Unavailable** — no result or backend offline, actionable next step.  
**Failed** — human-readable short cause and retry link.

### MetricWithProvenance
- Human label (e.g. `Return to player`), actual value (or `—`), state badge, metadata line.
- Never show target as actual. Never show CI if it was not returned. Never assign Exact by default.
- Use enough precision to distinguish changes while not suggesting false certainty.

### Mathematical table
- Right-align numerics, align decimals, use copyable raw values on hover/action.
- Changes highlight impacted inputs and the active validation state; do not auto-relabel output as proven.
- Indicate absent editor capability explicitly, e.g. `This model uses weighted draws instead of reel strips`.

### Game preview
- Derive preview geometry and symbols from the selected model; use compatible actual assets when available. **Nine Lives reference is 6×5**, while other models can differ. Do not fabricate a universal 5×3 or 6×5 board. Label coordinate diagrams and the separate reference demo truthfully.
- Explain when the game is paused, seeded or in demo mode. Support `Test spin` with deterministic evidence only when endpoint supports it.

## F. Motion and interaction

- Motion 120–180ms for panel opening, selection and focus transition; none for status correctness; no perpetual pulsing of metrics.
- Browser/system `prefers-reduced-motion` honored.
- Keyboard: top-level sidebar navigation, command palette, graph selection, data table navigation, ESC dismiss, focus restoration.
- Empty state includes 1 useful CTA; failure states explain recovery; destructive actions confirm.

## G. Viewport strategy

| Width | Behavior |
|---|---|
| ≥1440 | Navigation + primary canvas; optional contextual inspector |
| 1024–1439 | Collapsible navigation; inspector overlay/drawer, never fixed full-time |
| 768–1023 | Condensed header, nav drawer; table horizontally scrolls within its region only |
| <768 | Usable overview/results/play; graph advanced editing can explicitly become read-only/limited with `Open on desktop` message. Never silently clip controls. |

**Visual QA screenshots:** 1440×900, 1280×800, 1024×768, 390×844, one focus-visible state and one no-backend state.

## H. The signature visual moment

In the selected project’s compatible Debug/Play presentation, clicking a win selects its underlying **event**. A restrained cyan/teal outline follows the source symbols and associated mechanic, while an evidence panel identifies calculation provenance. If the trace backend cannot provide links yet, do not fake interactivity: defer the feature and keep the preview clean.

## I. Design review rubric (each 0–2; min 14/16 to accept a view)

1. **Task clarity:** user can identify primary action immediately.
2. **Domain fidelity:** correctly expresses slot-specific structures (6×5 cluster for Nine Lives).
3. **Trust:** provenance accurate, no fabricated metrics.
4. **Information hierarchy:** controls and model data never drowned by chrome.
5. **Interaction:** keyboard and mouse produce predictable state changes.
6. **Accessibility:** focus/contrast/labels responsive to user settings.
7. **Design identity:** not a generic bento SaaS template.
8. **Integration:** state/route/test behavior remains correct across multiple projects and verified user identities; another model’s evidence is never shown as current.

Record reviewer notes and iterate screenshots before merge.
