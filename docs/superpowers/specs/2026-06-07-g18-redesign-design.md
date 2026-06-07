# G18 Redesign — Match Prototype Visuals

**Status:** Approved | **Date:** 2026-06-07

## Goal

Replace the current G18 light/dark shell with a faithful React 19 + TypeScript port of the SlotMathLab.zip HTML prototype, preserving every visual detail (dark-only oklch theme, IBM Plex fonts, brand mark, pill-segment tabs, provenance badge system, metric strip, palette/workspace/inspector layout). Keep the existing toolchain: Vite, TypeScript strict, ESLint, `openapi-fetch` typed client, Zustand, TanStack Query.

## Scope

This covers the **shell only** — top bar, tab routing, metric strip, layout skeleton. It does NOT include the functional canvas (G19), table editors (G20), expression editor (G21), custom mechanics (G22), live metrics (G23), simulate panel (G24), or export (G25). Those build on this shell.

## Design tokens (from prototype, unchanged)

- **Surfaces:** oklch with hue=255, bg (0.165), bg-1 (0.195), bg-2 (0.225), bg-3 (0.265), bg-canvas (0.150)
- **Text:** text (0.95), muted (0.72), faint (0.56)
- **Provenance:** exact green (0.80/0.15/158), sampled blue (0.74/0.135/250), epsilon amber (0.82/0.135/78), danger red (0.68/0.18/22)
- **Node accents:** draw blue, eval green, loop purple, expr amber, sink desaturated
- **Fonts:** IBM Plex Sans (UI), IBM Plex Mono (code/numbers)
- **Radii:** 7px default, 12px large
- **Shadows:** shadow (card), shadow-pop (dropdown/modal)

## Components

### TopBar
- Brand mark: 26px conic-gradient square with inner cutout + dot
- Brand name: "Slot Math **Lab**" — Lab in exact green
- Project pill: dot + name, bg-2 background
- Tabs: pill-segment control — Build, Simulate, Results, Export — each with SVG icon + label. Active = bg-3 + inset border. Inactive = transparent.
- Topbar-right: provenance legend (Exact/ε-pruned/Sampled swatches) + AI assist button (purple accent)
- All icons from prototype Ic set (24px viewbox, stroke 1.8)

### Layout body
- `.body`: flex row, flex:1, min-height:0
- Per-tab content rendered inside `.body`
- Build tab: palette (left 200px) + workspace (center flex) + inspector panel (right 320px) + metric strip (bottom)
- Other tabs: workspace fills body

### MetricStrip
- Fixed-height bar (78px) at bottom of workspace
- 5 cells: RTP, Hit frequency, Base volatility, Feature trigger, Max win
- Each cell: label (uppercase mono) + value (22px mono) + sub-label + provenance badge
- RTP cell shows rational string
- Flash animation on RTP change

### ProvBadge
- Three variants: Exact (green pill), ExactWithinEpsilon (amber pill, "ε-pruned"), Sampled (blue pill, "n=...")
- Dot + label + optional detail
- Title tooltip with full provenance info

### Icons (Ic)
- SVG components: draw, evaluator, predicate, expr, loop, sink, build, sim, results, export, ai, plus, minus, fit, play, stop, copy, check, x, target
- All 24px viewbox, stroke 1.8, currentColor

### Palette (left panel, Build tab only)
- 200px wide, bg-1, right border
- Group headers + draggable items with icon + title + subtitle
- Primitives section: Draw, State, Loop, Branch, Map
- Library section: evaluator nodes, transform nodes
- Each item: icon (24px, colored by node accent) + title (12px) + contract subtitle (9.5px mono)

### Inspector (right panel, Build tab only)
- 320px wide, bg-1, left border
- Panel header with title + optional subtitle
- Section labels (uppercase, faint)
- Form fields, inputs, toggles, steppers — all matching prototype styles
- Empty state: centered icon + "Select a node" message

### Tweaks panel
- Floating bottom-left or right panel
- Controls: accent color (preset swatches), surface mood (slate/ocean/violet/steel radio), canvas grid toggle, animated edges toggle, density toggle

## State (Zustand)

```ts
interface AppState {
  tab: 'build' | 'simulate' | 'results' | 'export';
  setTab: (t: Tab) => void;
  tweaks: {
    accent: string;        // e.g. "#46d39a"
    mood: 'slate' | 'ocean' | 'violet' | 'steel';
    grid: boolean;
    flow: boolean;
    density: 'compact' | 'regular';
  };
  setTweak: (key: string, value: unknown) => void;
}
```

## Routing

Keep React Router, but tab switching is handled via Zustand + router sync. `/build`, `/simulate`, `/results`, `/export` routes. Index redirects to `/build`.

## A11y

- Skip link preserved (adapt colors for dark theme)
- Tabs: `role="tablist"`, `role="tab"`, `aria-selected`
- Focus visible: 2px outline in sampled blue
- Prefers-reduced-motion: disable animations/transitions
- axe score ≥ 90 verified on dark theme

## Files

| File | Action |
|------|--------|
| `index.html` | Add Google Fonts preconnect links |
| `src/index.css` | Full rewrite — prototype tokens + all component styles |
| `src/main.tsx` | Minor update — keep providers |
| `src/App.tsx` | Rewrite — prototype layout structure |
| `src/components/Layout.tsx` | Rewrite — TopBar + Body shell |
| `src/components/MetricStrip.tsx` | New |
| `src/components/ProvBadge.tsx` | New |
| `src/components/Icons.tsx` | New — Ic SVG components |
| `src/components/Palette.tsx` | New — left panel |
| `src/components/Inspector.tsx` | New — right panel (placeholder for G19) |
| `src/components/TweaksPanel.tsx` | New |
| `src/store/index.ts` | Extend with tweaks state |
| `src/pages/Build.tsx` | Update — compose palette + canvas placeholder + inspector + metric strip |
| `src/pages/Simulate.tsx` | Update — placeholder in workspace |
| `src/pages/Results.tsx` | Update — placeholder in workspace |
| `src/pages/Export.tsx` | Update — placeholder in workspace |
| `src/api/client.ts` | Unchanged |
| Delete: `src/assets/hero.png`, `src/assets/react.svg`, `src/assets/vite.svg` | Remove Vite boilerplate |

## DoD

- [ ] `npm run build`, `tsc --noEmit`, `npm run lint` exit 0
- [ ] Shell renders dark theme with brand mark, top bar, 4 icon-tabs, provenance legend
- [ ] Tab routing works (Build/Simulate/Results/Export)
- [ ] Build tab shows palette + workspace + inspector + metric strip layout
- [ ] Metric strip renders placeholder values with provenance badges
- [ ] Tweaks panel: accent color and mood change CSS custom properties
- [ ] axe score ≥ 90 on the dark shell
- [ ] `npm run generate-api` still produces valid types
- [ ] Zustand store has tab + tweaks state
- [ ] TanStack Query provider is mounted
- [ ] No Vite boilerplate assets remain
