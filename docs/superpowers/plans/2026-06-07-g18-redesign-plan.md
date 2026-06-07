# G18 Redesign — Match Prototype Visuals — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port the SlotMathLab.zip HTML prototype to React 19 + TypeScript + Vite, preserving every visual detail (dark-only oklch theme, IBM Plex fonts, brand mark, pill-segment icon tabs, provenance badge system, metric strip, palette/workspace/inspector layout).

**Architecture:** Single-pass CSS rewrite establishing the oklch design token system, then component-by-component port from JSX prototype to TSX. Each component maps 1:1 to a prototype visual element. Zustand holds tab + tweaks state. React Router handles URL routing synced with tab state.

**Tech Stack:** React 19, TypeScript 6, Vite 8, Zustand 5, TanStack Query 5, React Router 7, openapi-fetch

---

### Task 1: Clean up Vite boilerplate + update index.html

**Files:**
- Delete: `src/assets/hero.png`, `src/assets/react.svg`, `src/assets/vite.svg`
- Modify: `index.html`
- Delete: `src/App.css` (already deleted in prior work)
- Delete: `src/generated/` (if exists — unused by G18)
- Modify: `src/vite-env.d.ts`

- [ ] **Step 1: Delete Vite boilerplate assets**

```bash
rm -f src/assets/hero.png src/assets/react.svg src/assets/vite.svg
```

- [ ] **Step 2: Update index.html with Google Fonts preconnect and correct title**

Replace the `<head>` content in `index.html`:

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Slot Math Lab</title>
    <link rel="icon" type="image/svg+xml" href="/favicon.svg" />
    <link rel="preconnect" href="https://fonts.googleapis.com" />
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
    <link
      href="https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600;700&family=IBM+Plex+Mono:wght@400;500;600&display=swap"
      rel="stylesheet"
    />
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat: G18 cleanup — remove Vite boilerplate, add IBM Plex fonts"
```

---

### Task 2: Full CSS rewrite — design tokens + all component styles

**Files:**
- Modify: `src/index.css` (full rewrite)

- [ ] **Step 1: Write the complete index.css**

Replace `src/index.css` with the full prototype-derived stylesheet:

```css
/* ── Design tokens (from SlotMathLab prototype) ───────────────────── */
:root {
  --hue: 255;
  /* surfaces (cool charcoal, blue-leaning) */
  --bg: oklch(0.165 0.012 var(--hue));
  --bg-1: oklch(0.195 0.012 var(--hue));
  --bg-2: oklch(0.225 0.014 var(--hue));
  --bg-3: oklch(0.265 0.015 var(--hue));
  --bg-canvas: oklch(0.150 0.010 var(--hue));
  --line: oklch(0.31 0.014 var(--hue));
  --line-2: oklch(0.40 0.016 var(--hue));
  --text: oklch(0.95 0.006 var(--hue));
  --muted: oklch(0.72 0.012 var(--hue));
  --faint: oklch(0.56 0.012 var(--hue));

  /* provenance / accent language (shared chroma) */
  --exact: oklch(0.80 0.15 158);     /* green  */
  --exact-dim: oklch(0.80 0.15 158 / 0.16);
  --sampled: oklch(0.74 0.135 250);  /* blue   */
  --sampled-dim: oklch(0.74 0.135 250 / 0.16);
  --epsilon: oklch(0.82 0.135 78);   /* amber  */
  --epsilon-dim: oklch(0.82 0.135 78 / 0.16);
  --danger: oklch(0.68 0.18 22);
  --danger-dim: oklch(0.68 0.18 22 / 0.16);

  /* node accents */
  --n-draw: oklch(0.74 0.135 250);
  --n-eval: oklch(0.80 0.15 158);
  --n-loop: oklch(0.78 0.15 305);
  --n-expr: oklch(0.82 0.135 78);
  --n-sink: oklch(0.78 0.05 255);

  --radius: 7px;
  --radius-lg: 12px;
  --shadow: 0 1px 2px rgba(0,0,0,.4), 0 8px 28px -8px rgba(0,0,0,.55);
  --shadow-pop: 0 12px 40px -8px rgba(0,0,0,.7);
  --sans: "IBM Plex Sans", system-ui, sans-serif;
  --mono: "IBM Plex Mono", ui-monospace, monospace;
}

* { box-sizing: border-box; }
html, body { margin: 0; height: 100%; }
body {
  font-family: var(--sans);
  background: var(--bg);
  color: var(--text);
  font-size: 13px;
  line-height: 1.4;
  -webkit-font-smoothing: antialiased;
  overflow: hidden;
}
#root { height: 100vh; }
button { font-family: inherit; color: inherit; cursor: pointer; }
::selection { background: oklch(0.74 0.135 250 / 0.35); }

/* scrollbars */
::-webkit-scrollbar { width: 10px; height: 10px; }
::-webkit-scrollbar-thumb { background: var(--bg-3); border-radius: 6px; border: 2px solid transparent; background-clip: padding-box; }
::-webkit-scrollbar-thumb:hover { background: var(--line-2); background-clip: padding-box; }
::-webkit-scrollbar-track { background: transparent; }

.mono { font-family: var(--mono); }
.tnum { font-variant-numeric: tabular-nums; font-family: var(--mono); }

/* ── Skip link (a11y) ───────────────────────────────────────────── */
.skip-link {
  position: absolute; top: -100%; left: 8px;
  padding: 8px 16px; background: var(--sampled); color: #04101f;
  border-radius: var(--radius); z-index: 1000; font-weight: 600; text-decoration: none;
}
.skip-link:focus { top: 8px; }

/* ── App shell ──────────────────────────────────────────────────── */
.app { display: flex; flex-direction: column; height: 100vh; }

/* ── Top bar ────────────────────────────────────────────────────── */
.topbar {
  display: flex; align-items: center; gap: 16px;
  height: 50px; padding: 0 16px; flex-shrink: 0;
  background: linear-gradient(var(--bg-1), var(--bg-1));
  border-bottom: 1px solid var(--line);
  z-index: 30;
}
.brand { display: flex; align-items: center; gap: 10px; }
.brand-mark {
  width: 26px; height: 26px; border-radius: 7px;
  background: conic-gradient(from 200deg, var(--exact), var(--sampled), var(--n-loop), var(--exact));
  position: relative; flex-shrink: 0;
}
.brand-mark::after { content:""; position:absolute; inset:5px; border-radius:4px; background: var(--bg-1); }
.brand-mark::before { content:""; position:absolute; inset:9px; border-radius:50%; background: var(--text); z-index:1; }
.brand-name { font-weight: 600; letter-spacing: -0.01em; font-size: 14px; white-space: nowrap; }
.brand-name b { color: var(--exact); font-weight: 600; }
.project-pill {
  display:flex; align-items:center; gap:8px; padding: 4px 10px; border-radius: 6px;
  background: var(--bg-2); border: 1px solid var(--line); font-size: 12px; color: var(--muted);
}
.project-pill .dot { width:6px; height:6px; border-radius:50%; background: var(--exact); }

/* ── Tabs ───────────────────────────────────────────────────────── */
.tabs { display: flex; gap: 2px; margin-left: 8px; background: var(--bg-2); padding: 3px; border-radius: 9px; border: 1px solid var(--line); }
.tab {
  border: 0; background: transparent; color: var(--muted);
  padding: 6px 14px; border-radius: 6px; font-size: 12.5px; font-weight: 500; white-space: nowrap;
  display:flex; align-items:center; gap:7px; transition: background .12s, color .12s; cursor: pointer;
  text-decoration: none;
}
.tab:hover { color: var(--text); }
.tab.active { background: var(--bg-3); color: var(--text); box-shadow: inset 0 0 0 1px var(--line-2); }
.tab:focus-visible { outline: 2px solid var(--sampled); outline-offset: 2px; }

/* ── Topbar right ───────────────────────────────────────────────── */
.topbar-right { margin-left: auto; display:flex; align-items:center; gap: 10px; }

/* provenance legend */
.legend { display:flex; align-items:center; gap: 12px; font-size: 11px; color: var(--faint); }
.legend .item { display:flex; align-items:center; gap:5px; }
.legend .sw { width:9px;height:9px;border-radius:3px; }

/* ── Buttons ────────────────────────────────────────────────────── */
.btn {
  display:inline-flex; align-items:center; gap:7px; padding: 7px 13px; border-radius: 7px;
  background: var(--bg-2); border: 1px solid var(--line); color: var(--text); white-space: nowrap;
  font-size: 12.5px; font-weight: 500; transition: background .12s, border-color .12s, transform .06s;
}
.btn:hover { background: var(--bg-3); border-color: var(--line-2); }
.btn:active { transform: translateY(1px); }
.btn.primary { background: var(--exact); color: #06140d; border-color: transparent; font-weight: 600; }
.btn.primary:hover { background: oklch(0.84 0.15 158); }
.btn.ghost { background: transparent; }
.btn.sm { padding: 5px 9px; font-size: 11.5px; }
.btn.icon { padding: 7px; }
.btn:disabled { opacity: .45; pointer-events: none; }

/* ── Layout body ────────────────────────────────────────────────── */
.body { flex: 1; min-height: 0; display: flex; }
.workspace { flex: 1; min-width: 0; display: flex; flex-direction: column; position: relative; }

/* ── Provenance tag ─────────────────────────────────────────────── */
.prov {
  display:inline-flex; align-items:center; gap:5px; padding: 2px 7px; border-radius: 20px;
  font-family: var(--mono); font-size: 10px; font-weight: 500; letter-spacing: .02em;
  white-space: nowrap; border: 1px solid transparent;
}
.prov .pdot { width:6px; height:6px; border-radius:50%; }
.prov.exact { background: var(--exact-dim); color: var(--exact); border-color: oklch(0.80 0.15 158 / 0.3); }
.prov.exact .pdot { background: var(--exact); }
.prov.sampled { background: var(--sampled-dim); color: var(--sampled); border-color: oklch(0.74 0.135 250 / 0.3); }
.prov.sampled .pdot { background: var(--sampled); }
.prov.epsilon { background: var(--epsilon-dim); color: var(--epsilon); border-color: oklch(0.82 0.135 78 / 0.3); }
.prov.epsilon .pdot { background: var(--epsilon); }

/* ── Metric strip ───────────────────────────────────────────────── */
.metric-strip {
  display: flex; align-items: stretch; gap: 0; flex-shrink: 0;
  background: var(--bg-1); border-top: 1px solid var(--line); height: 78px;
}
.metric-cell {
  flex: 1; padding: 11px 16px; display:flex; flex-direction: column; gap: 4px;
  border-right: 1px solid var(--line); position: relative; min-width: 0;
}
.metric-cell:last-child { border-right: 0; }
.metric-label { font-size: 10px; text-transform: uppercase; letter-spacing: .06em; color: var(--faint); display:flex; align-items:center; gap:6px; justify-content: space-between; width: 100%; }
.metric-label > span:first-child { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.metric-value { font-family: var(--mono); font-size: 22px; font-weight: 500; letter-spacing: -0.02em; line-height: 1; display:flex; align-items:baseline; gap:3px; }
.metric-value .unit { font-size: 13px; color: var(--muted); }
.metric-sub { font-family: var(--mono); font-size: 10px; color: var(--faint); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.metric-cell.flash { animation: flash .5s ease; }
@keyframes flash { 0% { background: var(--exact-dim); } 100% { background: transparent; } }

/* ── Panels ─────────────────────────────────────────────────────── */
.panel { background: var(--bg-1); border-left: 1px solid var(--line); display:flex; flex-direction: column; }
.panel-h { padding: 12px 14px; border-bottom: 1px solid var(--line); display:flex; align-items:center; gap:8px; flex-shrink:0; }
.panel-h .t { font-weight: 600; font-size: 13px; }
.panel-h .s { font-size: 11px; color: var(--faint); margin-left: auto; font-family: var(--mono); }
.panel-body { overflow-y: auto; padding: 14px; flex: 1; }
.section-label { font-size: 10.5px; text-transform: uppercase; letter-spacing: .08em; color: var(--faint); margin: 4px 0 9px; font-weight: 600; }

/* form fields */
.field { margin-bottom: 14px; }
.field > label { display:block; font-size: 11.5px; color: var(--muted); margin-bottom: 6px; }
.inp {
  width: 100%; background: var(--bg); border: 1px solid var(--line); color: var(--text);
  border-radius: 6px; padding: 7px 9px; font-size: 12.5px; font-family: var(--mono);
  transition: border-color .12s, box-shadow .12s;
}
.inp:focus { outline: none; border-color: var(--sampled); box-shadow: 0 0 0 3px var(--sampled-dim); }
.row { display: flex; gap: 8px; align-items: center; }
.row > * { flex: 1; }

.hint { font-size: 11px; color: var(--faint); line-height: 1.5; margin-top: 6px; }
.divider { height:1px; background: var(--line); margin: 16px -14px; }

.empty-inspector { display:flex; flex-direction: column; align-items:center; justify-content:center; height:100%; text-align:center; color: var(--faint); gap: 10px; padding: 24px; }

/* ── Palette (left) ─────────────────────────────────────────────── */
.palette { width: 200px; flex-shrink:0; background: var(--bg-1); border-right: 1px solid var(--line); display:flex; flex-direction:column; overflow-y:auto; }
.palette .grp { padding: 12px 12px 4px; }
.pal-item { display:flex; align-items:center; gap:9px; padding: 8px 12px; cursor: grab; border-radius: 7px; margin: 0 6px 2px; transition: background .1s; }
.pal-item:hover { background: var(--bg-2); }
.pal-item .pic { width: 24px; height: 24px; border-radius: 6px; display:grid; place-items:center; background: var(--bg-3); color: var(--nc); flex-shrink:0; }
.pal-item .pic svg { width: 14px; height: 14px; }
.pal-item .pt { font-size: 12px; font-weight: 500; }
.pal-item .ps { font-size: 9.5px; color: var(--faint); font-family: var(--mono); }

/* ── Canvas (placeholder for G19) ───────────────────────────────── */
.canvas-wrap { flex: 1; position: relative; overflow: hidden; background: var(--bg-canvas); cursor: default; }
.canvas-grid { position: absolute; inset: 0;
  background-image: radial-gradient(circle, oklch(0.32 0.014 255) 1px, transparent 1.2px);
  background-size: 26px 26px; pointer-events: none;
}
.canvas-placeholder { display:flex; align-items:center; justify-content:center; height:100%; color: var(--faint); font-size: 13px; }

/* ── Tweak states ───────────────────────────────────────────────── */
body.no-grid .canvas-grid { display: none; }
body.no-flow .edge-flow { animation: none !important; }
body.compact .node-body { padding: 6px 12px; }
body.compact .node-head { padding: 7px 12px; }
body.compact .metric-strip { height: 66px; }
body.compact .metric-value { font-size: 19px; }
body.compact .panel-body { padding: 11px; }

/* ── Workspace tab content ──────────────────────────────────────── */
.workspace-tab { flex:1; display:flex; align-items:center; justify-content:center; color: var(--faint); font-size: 13px; }

/* ── A11y ───────────────────────────────────────────────────────── */
:focus-visible { outline: 2px solid var(--sampled); outline-offset: 2px; }
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after { animation-duration: 0.01ms !important; transition-duration: 0.01ms !important; }
}
```

- [ ] **Step 2: Verify build after CSS rewrite**

```bash
npm run build
```
Expected: `tsc -b && vite build` exits 0.

- [ ] **Step 3: Commit**

```bash
git add src/index.css && git commit -m "feat: G18 CSS rewrite — oklch design tokens, prototype component styles"
```

---

### Task 3: Create Icons component

**Files:**
- Create: `src/components/Icons.tsx`

- [ ] **Step 1: Write Icons.tsx**

All 20 SVG icons from the prototype, typed as React functional components accepting `React.SVGProps<SVGSVGElement>`:

```tsx
import type { SVGProps } from 'react';

type SvgProps = SVGProps<SVGSVGElement>;

export const Ic = {
  draw: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <path d="M4 7h16M4 12h16M4 17h16" strokeLinecap="round" />
      <circle cx="8" cy="7" r="1.6" fill="currentColor" stroke="none" />
      <circle cx="15" cy="12" r="1.6" fill="currentColor" stroke="none" />
      <circle cx="10" cy="17" r="1.6" fill="currentColor" stroke="none" />
    </svg>
  ),
  evaluator: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <rect x="3.5" y="4.5" width="17" height="15" rx="2" />
      <path d="M3.5 9.5h17M9 9.5v10M15 9.5v10" />
    </svg>
  ),
  predicate: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <path d="M12 3l8 9-8 9-8-9 8-9z" />
      <path d="M9.5 12l1.8 1.8 3.2-3.6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  ),
  expr: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <path
        d="M7 5c-1.5 0-2 1-2 3v2c0 1.3-.6 2-1.5 2 .9 0 1.5.7 1.5 2v2c0 2 .5 3 2 3M17 5c1.5 0 2 1 2 3v2c0 1.3.6 2 1.5 2-.9 0-1.5.7-1.5 2v2c0 2-.5 3-2 3"
        strokeLinecap="round"
      />
    </svg>
  ),
  loop: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <path d="M4 9a7 7 0 0111.5-3.5L19 8M20 15a7 7 0 01-11.5 3.5L5 16" strokeLinecap="round" />
      <path d="M19 4v4h-4M5 20v-4h4" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  ),
  sink: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <path d="M5 19V9M10 19V5M15 19v-7M20 19v-4" strokeLinecap="round" />
      <path d="M3 19h18" strokeLinecap="round" />
    </svg>
  ),
  build: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <circle cx="6" cy="6" r="2.5" />
      <circle cx="18" cy="6" r="2.5" />
      <circle cx="12" cy="18" r="2.5" />
      <path d="M8.5 6H18M6 8.5v4a3 3 0 003 3h0M18 8.5v4a3 3 0 01-3 3h-2" strokeLinecap="round" />
    </svg>
  ),
  sim: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <path
        d="M3 16c2-6 4-9 5-9s1.5 4 2.5 4 2-9 3.5-9 2.5 7 3.5 9 2 1 3.5 1"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  ),
  results: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <rect x="4" y="3" width="16" height="18" rx="2" />
      <path d="M8 8h8M8 12h8M8 16h5" strokeLinecap="round" />
    </svg>
  ),
  export: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <path d="M12 3v12M12 3L8 7M12 3l4 4" strokeLinecap="round" strokeLinejoin="round" />
      <path d="M5 14v4a2 2 0 002 2h10a2 2 0 002-2v-4" strokeLinecap="round" />
    </svg>
  ),
  ai: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <path d="M12 3l1.8 4.2L18 9l-4.2 1.8L12 15l-1.8-4.2L6 9l4.2-1.8L12 3z" strokeLinejoin="round" />
      <path d="M18 14l.9 2.1 2.1.9-2.1.9-.9 2.1-.9-2.1-2.1-.9 2.1-.9.9-2.1z" strokeLinejoin="round" />
    </svg>
  ),
  plus: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" {...p}>
      <path d="M12 5v14M5 12h14" strokeLinecap="round" />
    </svg>
  ),
  minus: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" {...p}>
      <path d="M5 12h14" strokeLinecap="round" />
    </svg>
  ),
  fit: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <path d="M4 9V5a1 1 0 011-1h4M20 9V5a1 1 0 00-1-1h-4M4 15v4a1 1 0 001 1h4M20 15v4a1 1 0 01-1 1h-4" strokeLinecap="round" />
    </svg>
  ),
  play: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="currentColor" stroke="none" {...p}>
      <path d="M7 5l12 7-12 7V5z" />
    </svg>
  ),
  stop: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="currentColor" stroke="none" {...p}>
      <rect x="6" y="6" width="12" height="12" rx="2" />
    </svg>
  ),
  copy: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <rect x="9" y="9" width="11" height="11" rx="2" />
      <path d="M5 15V5a2 2 0 012-2h8" strokeLinecap="round" />
    </svg>
  ),
  check: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" {...p}>
      <path d="M5 12.5l4.5 4.5L19 7" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  ),
  x: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" {...p}>
      <path d="M6 6l12 12M18 6L6 18" strokeLinecap="round" />
    </svg>
  ),
  target: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <circle cx="12" cy="12" r="8" />
      <circle cx="12" cy="12" r="3.5" />
    </svg>
  ),
};

export const NODE_ACCENT: Record<string, string> = {
  draw: 'var(--n-draw)',
  evaluator: 'var(--n-eval)',
  predicate: 'var(--n-eval)',
  expr: 'var(--n-expr)',
  loop: 'var(--n-loop)',
  sink: 'var(--n-sink)',
};
```

- [ ] **Step 2: Verify build**

```bash
npm run build
```
Expected: exits 0.

- [ ] **Step 3: Commit**

```bash
git add src/components/Icons.tsx && git commit -m "feat: G18 Icons component — 20 SVG icons from prototype"
```

---

### Task 4: Create ProvBadge component

**Files:**
- Create: `src/components/ProvBadge.tsx`

- [ ] **Step 1: Write ProvBadge.tsx**

```tsx
export interface Provenance {
  kind: 'Exact' | 'ExactWithinEpsilon' | 'Sampled';
  n?: number;
  stdErr?: number;
  ci95?: number;
  bound?: number;
  note?: string;
}

interface ProvBadgeProps {
  p?: Provenance | null;
  mini?: boolean;
}

function fmtN(n: number): string {
  if (n >= 1e6) return (n / 1e6).toFixed(n >= 1e7 ? 0 : 1) + 'M';
  if (n >= 1e3) return (n / 1e3).toFixed(n >= 1e4 ? 0 : 1) + 'k';
  return String(Math.round(n));
}

function provTitle(p: Provenance): string {
  if (p.kind === 'Sampled')
    return `Monte-Carlo estimate · n=${fmtN(p.n || 0)} · stdErr ${(p.stdErr || 0).toFixed(5)} · 95% CI ±${(p.ci95 || 0).toFixed(5)}`;
  if (p.kind === 'ExactWithinEpsilon')
    return `Exact up to ε-pruning · ${p.note || ''} · bound ±${p.bound || 0}`;
  return 'Exact rational — closed form, no sampling';
}

export default function ProvBadge({ p, mini }: ProvBadgeProps) {
  if (!p) return null;

  const map = {
    Exact: { cls: 'exact', label: 'Exact' },
    ExactWithinEpsilon: { cls: 'epsilon', label: mini ? 'ε-pruned' : 'Exact ± ε' },
    Sampled: { cls: 'sampled', label: 'Sampled' },
  };

  const m = map[p.kind];
  let detail = '';
  if (p.kind === 'Sampled' && p.n) detail = ' n=' + fmtN(p.n);

  return (
    <span className={`prov ${m.cls}`} title={provTitle(p)}>
      <span className="pdot" />
      {m.label}
      {detail}
    </span>
  );
}
```

- [ ] **Step 2: Verify build**

```bash
npm run build
```
Expected: exits 0.

- [ ] **Step 3: Commit**

```bash
git add src/components/ProvBadge.tsx && git commit -m "feat: G18 ProvBadge component — Exact/ε-pruned/Sampled provenance pills"
```

---

### Task 5: Extend Zustand store with tweaks state

**Files:**
- Modify: `src/store/index.ts`

- [ ] **Step 1: Rewrite store/index.ts**

Replace the current store with the extended version:

```ts
import { create } from 'zustand';

export type TabId = 'build' | 'simulate' | 'results' | 'export';
export type Mood = 'slate' | 'ocean' | 'violet' | 'steel';
export type Density = 'compact' | 'regular';

export interface Tweaks {
  accent: string;
  mood: Mood;
  grid: boolean;
  flow: boolean;
  density: Density;
}

export interface AppState {
  tab: TabId;
  setTab: (tab: TabId) => void;
  configName: string | null;
  setConfigName: (name: string | null) => void;
  tweaks: Tweaks;
  setTweak: <K extends keyof Tweaks>(key: K, value: Tweaks[K]) => void;
}

export const MOOD_HUE: Record<Mood, number> = {
  slate: 255,
  ocean: 230,
  violet: 290,
  steel: 210,
};

export const useAppStore = create<AppState>((set) => ({
  tab: 'build',
  setTab: (tab) => set({ tab }),
  configName: 'Untitled',
  setConfigName: (name) => set({ configName: name }),
  tweaks: {
    accent: '#46d39a',
    mood: 'slate',
    grid: true,
    flow: true,
    density: 'regular',
  },
  setTweak: (key, value) =>
    set((s) => ({ tweaks: { ...s.tweaks, [key]: value } })),
}));
```

- [ ] **Step 2: Verify build**

```bash
npm run build
```
Expected: exits 0.

- [ ] **Step 3: Commit**

```bash
git add src/store/index.ts && git commit -m "feat: G18 extend Zustand store — tweaks state (accent, mood, grid, flow, density)"
```

---

### Task 6: Create MetricStrip component

**Files:**
- Create: `src/components/MetricStrip.tsx`

- [ ] **Step 1: Write MetricStrip.tsx**

```tsx
import ProvBadge from './ProvBadge';
import type { Provenance } from './ProvBadge';

interface MetricCell {
  label: string;
  value: string;
  unit?: string;
  sub?: string;
  prov?: Provenance;
  accent?: string;
}

interface MetricStripProps {
  metrics?: MetricCell[] | null;
  rationalStr?: string;
}

function pct(x: number, dp?: number): string {
  return (x * 100).toFixed(dp == null ? 2 : dp);
}

/** Default empty state showing placeholder dashes — matches prototype layout. */
const EMPTY_METRICS: MetricCell[] = [
  {
    label: 'Return to player',
    value: '—',
    unit: '%',
    sub: '—',
    prov: { kind: 'Exact' },
    accent: 'var(--exact)',
  },
  {
    label: 'Hit frequency',
    value: '—',
    unit: '%',
    sub: '1 win / — spins',
    prov: { kind: 'Exact' },
  },
  {
    label: 'Base volatility',
    value: '—',
    unit: 'σ',
    sub: '— · base game',
    prov: { kind: 'Exact' },
  },
  {
    label: 'Feature trigger',
    value: '—',
    unit: '%',
    sub: '~1 in — spins',
    prov: { kind: 'Exact' },
  },
  {
    label: 'Max line win',
    value: '—',
    unit: '×',
    sub: '×— in free spins',
    prov: { kind: 'Exact' },
  },
];

export default function MetricStrip({ metrics }: MetricStripProps) {
  const cells = metrics ?? EMPTY_METRICS;

  return (
    <div className="metric-strip">
      {cells.map((cell, i) => (
        <div key={i} className="metric-cell">
          <div className="metric-label">
            <span>{cell.label}</span>
            {cell.prov && <ProvBadge p={cell.prov} mini />}
          </div>
          <div className="metric-value" style={cell.accent ? { color: cell.accent } : undefined}>
            {cell.value}
            {cell.unit && <span className="unit">{cell.unit}</span>}
          </div>
          {cell.sub && <div className="metric-sub">{cell.sub}</div>}
        </div>
      ))}
    </div>
  );
}
```

- [ ] **Step 2: Verify build**

```bash
npm run build
```
Expected: exits 0.

- [ ] **Step 3: Commit**

```bash
git add src/components/MetricStrip.tsx && git commit -m "feat: G18 MetricStrip component — 5-cell metric bar with provenance badges"
```

---

### Task 7: Create Palette component (left panel, placeholder)

**Files:**
- Create: `src/components/Palette.tsx`

- [ ] **Step 1: Write Palette.tsx**

```tsx
import { Ic, NODE_ACCENT } from './Icons';

interface PaletteItem {
  id: string;
  label: string;
  sub: string;
  icon: keyof typeof Ic;
  accent: string;
}

const PRIMITIVES: PaletteItem[] = [
  { id: 'draw', label: 'Draw', sub: 'Weighted choice', icon: 'draw', accent: NODE_ACCENT.draw },
  { id: 'state', label: 'State', sub: 'Get / Put / Modify', icon: 'loop', accent: NODE_ACCENT.sink },
  { id: 'loop', label: 'Loop', sub: 'Fixpoint + stop', icon: 'loop', accent: NODE_ACCENT.loop },
  { id: 'branch', label: 'Branch', sub: 'Bind + conditional', icon: 'predicate', accent: NODE_ACCENT.evaluator },
  { id: 'map', label: 'Map', sub: 'Transform result', icon: 'expr', accent: NODE_ACCENT.expr },
];

const LIBRARY: PaletteItem[] = [
  { id: 'lines', label: 'Line Evaluator', sub: 'IEvaluator', icon: 'evaluator', accent: NODE_ACCENT.evaluator },
  { id: 'ways', label: 'Ways Evaluator', sub: 'IEvaluator', icon: 'evaluator', accent: NODE_ACCENT.evaluator },
  { id: 'cluster', label: 'Cluster Evaluator', sub: 'IEvaluator', icon: 'evaluator', accent: NODE_ACCENT.evaluator },
  { id: 'scatter', label: 'Scatter', sub: 'IEvaluator', icon: 'target', accent: NODE_ACCENT.evaluator },
  { id: 'reveal', label: 'Reveal', sub: 'ITransform', icon: 'draw', accent: NODE_ACCENT.draw },
  { id: 'expand', label: 'Expand / Explode', sub: 'ITransform', icon: 'plus', accent: NODE_ACCENT.draw },
  { id: 'lock', label: 'Lock / Sticky', sub: 'ITransform', icon: 'check', accent: NODE_ACCENT.expr },
  { id: 'morph', label: 'Morph / Upgrade', sub: 'ITransform', icon: 'loop', accent: NODE_ACCENT.loop },
  { id: 'tumble', label: 'Refill / Tumble', sub: 'ITransform', icon: 'fit', accent: NODE_ACCENT.sink },
];

export default function Palette() {
  return (
    <div className="palette">
      <div className="grp">
        <div className="section-label">Primitives</div>
      </div>
      {PRIMITIVES.map((item) => {
        const Icon = Ic[item.icon];
        return (
          <div key={item.id} className="pal-item" title={`${item.label} — ${item.sub}`}>
            <div className="pic" style={{ color: item.accent }}>
              <Icon />
            </div>
            <div>
              <div className="pt">{item.label}</div>
              <div className="ps">{item.sub}</div>
            </div>
          </div>
        );
      })}
      <div className="grp">
        <div className="section-label">Library</div>
      </div>
      {LIBRARY.map((item) => {
        const Icon = Ic[item.icon];
        return (
          <div key={item.id} className="pal-item" title={`${item.label} — ${item.sub}`}>
            <div className="pic" style={{ color: item.accent }}>
              <Icon />
            </div>
            <div>
              <div className="pt">{item.label}</div>
              <div className="ps">{item.sub}</div>
            </div>
          </div>
        );
      })}
    </div>
  );
}
```

- [ ] **Step 2: Verify build**

```bash
npm run build
```
Expected: exits 0.

- [ ] **Step 3: Commit**

```bash
git add src/components/Palette.tsx && git commit -m "feat: G18 Palette component — primitives + library node catalog"
```

---

### Task 8: Create Inspector component (placeholder for G19)

**Files:**
- Create: `src/components/Inspector.tsx`

- [ ] **Step 1: Write Inspector.tsx**

```tsx
import { Ic } from './Icons';

interface InspectorProps {
  node?: { id: string; label?: string; type?: string } | null;
}

export default function Inspector({ node }: InspectorProps) {
  if (!node) {
    return (
      <div className="panel" style={{ width: 320, flexShrink: 0 }}>
        <div className="empty-inspector">
          <Ic.target style={{ width: 32, height: 32, opacity: 0.3 }} />
          <span>Select a node to inspect</span>
        </div>
      </div>
    );
  }

  return (
    <div className="panel" style={{ width: 320, flexShrink: 0 }}>
      <div className="panel-h">
        <span className="t">{node.label ?? node.id}</span>
        {node.type && <span className="s">{node.type}</span>}
      </div>
      <div className="panel-body">
        <div className="section-label">Properties</div>
        <div className="field">
          <label>Label</label>
          <input className="inp" defaultValue={node.label ?? node.id} />
        </div>
        <div className="hint">Full inspector UI lands in G19.</div>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Verify build**

```bash
npm run build
```
Expected: exits 0.

- [ ] **Step 3: Commit**

```bash
git add src/components/Inspector.tsx && git commit -m "feat: G18 Inspector placeholder — select-node prompt, properties stub"
```

---

### Task 9: Rewrite Layout component (TopBar + Body shell)

**Files:**
- Modify: `src/components/Layout.tsx` (full rewrite)

- [ ] **Step 1: Write Layout.tsx**

```tsx
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useEffect } from 'react';
import { useAppStore, MOOD_HUE, type Mood } from '../store';
import { Ic } from './Icons';

const TABS = [
  { id: 'build' as const, label: 'Build', Icon: Ic.build },
  { id: 'simulate' as const, label: 'Simulate', Icon: Ic.sim },
  { id: 'results' as const, label: 'Results', Icon: Ic.results },
  { id: 'export' as const, label: 'Export', Icon: Ic.export },
];

export default function Layout() {
  const location = useLocation();
  const tab = useAppStore((s) => s.tab);
  const setTab = useAppStore((s) => s.setTab);
  const configName = useAppStore((s) => s.configName);
  const tweaks = useAppStore((s) => s.tweaks);

  // Sync tab from URL
  useEffect(() => {
    const matched = TABS.find((t) => location.pathname.startsWith('/' + t.id));
    if (matched && matched.id !== tab) setTab(matched.id);
  }, [location.pathname, tab, setTab]);

  // Apply tweaks to document
  useEffect(() => {
    const r = document.documentElement.style;
    r.setProperty('--exact', tweaks.accent);
    r.setProperty('--exact-dim', `color-mix(in srgb, ${tweaks.accent} 16%, transparent)`);
    r.setProperty('--n-eval', tweaks.accent);
    r.setProperty('--hue', String(MOOD_HUE[tweaks.mood as Mood] ?? 255));
    document.body.classList.toggle('no-grid', !tweaks.grid);
    document.body.classList.toggle('no-flow', !tweaks.flow);
    document.body.classList.toggle('compact', tweaks.density === 'compact');
  }, [tweaks]);

  return (
    <div className="app">
      <a href="#main-content" className="skip-link">
        Skip to main content
      </a>

      <div className="topbar">
        <div className="brand">
          <div className="brand-mark" />
          <div className="brand-name">
            Slot Math <b>Lab</b>
          </div>
        </div>

        <div className="project-pill">
          <span className="dot" />
          {configName}
        </div>

        <div className="tabs" role="tablist" aria-label="Main navigation">
          {TABS.map(({ id, label, Icon }) => (
            <NavLink
              key={id}
              to={`/${id}`}
              role="tab"
              aria-selected={tab === id}
              className={({ isActive }) => 'tab' + (isActive ? ' active' : '')}
            >
              <Icon style={{ width: 14, height: 14 }} />
              {label}
            </NavLink>
          ))}
        </div>

        <div className="topbar-right">
          <div className="legend">
            <span className="item">
              <span className="sw" style={{ background: 'var(--exact)' }} />
              Exact
            </span>
            <span className="item">
              <span className="sw" style={{ background: 'var(--epsilon)' }} />
              ε-pruned
            </span>
            <span className="item">
              <span className="sw" style={{ background: 'var(--sampled)' }} />
              Sampled
            </span>
          </div>
          <button
            className="btn"
            style={{ borderColor: 'oklch(0.78 0.15 305 / 0.4)' }}
          >
            <span style={{ color: 'var(--n-loop)' }}>
              <Ic.ai style={{ width: 15, height: 15 }} />
            </span>
            AI assist
          </button>
        </div>
      </div>

      <div className="body" id="main-content">
        <Outlet />
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Verify build**

```bash
npm run build
```
Expected: exits 0 (if it fails, check that the page components still export default functions — we'll fix in Task 12).

- [ ] **Step 3: Commit**

```bash
git add src/components/Layout.tsx && git commit -m "feat: G18 Layout rewrite — prototype topbar, tabs, provenance legend, tweaks sync"
```

---

### Task 10: Create TweaksPanel component

**Files:**
- Create: `src/components/TweaksPanel.tsx`

- [ ] **Step 1: Write TweaksPanel.tsx**

```tsx
import { useState } from 'react';
import { useAppStore, type Mood, type Density } from '../store';

const ACCENT_OPTIONS = ['#46d39a', '#34d3c2', '#7ed35a', '#37c6e0'];
const MOOD_OPTIONS: { value: Mood; label: string }[] = [
  { value: 'slate', label: 'Slate' },
  { value: 'ocean', label: 'Ocean' },
  { value: 'violet', label: 'Violet' },
  { value: 'steel', label: 'Steel' },
];
const DENSITY_OPTIONS: { value: Density; label: string }[] = [
  { value: 'regular', label: 'Regular' },
  { value: 'compact', label: 'Compact' },
];

export default function TweaksPanel() {
  const tweaks = useAppStore((s) => s.tweaks);
  const setTweak = useAppStore((s) => s.setTweak);
  const [collapsed, setCollapsed] = useState(false);

  return (
    <div
      style={{
        position: 'fixed',
        right: 16,
        bottom: 16,
        zIndex: 100,
        width: 240,
        background: 'var(--bg-1)',
        border: '1px solid var(--line-2)',
        borderRadius: 12,
        boxShadow: 'var(--shadow-pop)',
        fontSize: '11.5px',
        overflow: 'hidden',
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '10px 14px',
          cursor: 'pointer',
          userSelect: 'none',
          borderBottom: collapsed ? 'none' : '1px solid var(--line)',
        }}
        onClick={() => setCollapsed(!collapsed)}
      >
        <b style={{ fontSize: 12, fontWeight: 600 }}>Tweaks</b>
        <span style={{ color: 'var(--faint)', fontSize: 10 }}>
          {collapsed ? '▶' : '▼'}
        </span>
      </div>

      {!collapsed && (
        <div
          style={{
            padding: '2px 14px 14px',
            display: 'flex',
            flexDirection: 'column',
            gap: 10,
            maxHeight: 'calc(100vh - 120px)',
            overflowY: 'auto',
          }}
        >
          {/* Accent */}
          <div>
            <div
              style={{
                fontSize: 10,
                fontWeight: 600,
                textTransform: 'uppercase',
                letterSpacing: '.06em',
                color: 'var(--faint)',
                paddingTop: 8,
                marginBottom: 6,
              }}
            >
              Accent
            </div>
            <div style={{ display: 'flex', gap: 6 }}>
              {ACCENT_OPTIONS.map((color) => (
                <button
                  key={color}
                  onClick={() => setTweak('accent', color)}
                  style={{
                    width: 26,
                    height: 26,
                    borderRadius: 7,
                    background: color,
                    border:
                      tweaks.accent === color
                        ? '2px solid var(--text)'
                        : '2px solid transparent',
                    cursor: 'pointer',
                    padding: 0,
                  }}
                />
              ))}
            </div>
          </div>

          {/* Surface mood */}
          <div>
            <div
              style={{
                fontSize: 10,
                fontWeight: 600,
                textTransform: 'uppercase',
                letterSpacing: '.06em',
                color: 'var(--faint)',
                paddingTop: 8,
                marginBottom: 6,
              }}
            >
              Surface mood
            </div>
            <div
              style={{
                display: 'flex',
                background: 'var(--bg-2)',
                borderRadius: 7,
                border: '1px solid var(--line)',
                padding: 2,
              }}
            >
              {MOOD_OPTIONS.map(({ value, label }) => (
                <button
                  key={value}
                  onClick={() => setTweak('mood', value)}
                  style={{
                    flex: 1,
                    padding: '5px 0',
                    fontSize: 11,
                    fontWeight: 500,
                    border: 0,
                    borderRadius: 5,
                    background: tweaks.mood === value ? 'var(--bg-3)' : 'transparent',
                    color: tweaks.mood === value ? 'var(--text)' : 'var(--muted)',
                    boxShadow:
                      tweaks.mood === value
                        ? 'inset 0 0 0 1px var(--line-2)'
                        : 'none',
                    cursor: 'pointer',
                  }}
                >
                  {label}
                </button>
              ))}
            </div>
          </div>

          {/* Canvas */}
          <div>
            <div
              style={{
                fontSize: 10,
                fontWeight: 600,
                textTransform: 'uppercase',
                letterSpacing: '.06em',
                color: 'var(--faint)',
                paddingTop: 8,
                marginBottom: 6,
              }}
            >
              Canvas
            </div>
            <div
              style={{
                display: 'flex',
                flexDirection: 'column',
                gap: 6,
              }}
            >
              <label
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  color: 'var(--muted)',
                }}
              >
                Dot grid
                <ToggleButton
                  on={tweaks.grid}
                  onChange={(v) => setTweak('grid', v)}
                />
              </label>
              <label
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  color: 'var(--muted)',
                }}
              >
                Animated edges
                <ToggleButton
                  on={tweaks.flow}
                  onChange={(v) => setTweak('flow', v)}
                />
              </label>
            </div>
          </div>

          {/* Density */}
          <div>
            <div
              style={{
                fontSize: 10,
                fontWeight: 600,
                textTransform: 'uppercase',
                letterSpacing: '.06em',
                color: 'var(--faint)',
                paddingTop: 8,
                marginBottom: 6,
              }}
            >
              Layout
            </div>
            <div
              style={{
                display: 'flex',
                background: 'var(--bg-2)',
                borderRadius: 7,
                border: '1px solid var(--line)',
                padding: 2,
              }}
            >
              {DENSITY_OPTIONS.map(({ value, label }) => (
                <button
                  key={value}
                  onClick={() => setTweak('density', value)}
                  style={{
                    flex: 1,
                    padding: '5px 0',
                    fontSize: 11,
                    fontWeight: 500,
                    border: 0,
                    borderRadius: 5,
                    background:
                      tweaks.density === value ? 'var(--bg-3)' : 'transparent',
                    color:
                      tweaks.density === value ? 'var(--text)' : 'var(--muted)',
                    boxShadow:
                      tweaks.density === value
                        ? 'inset 0 0 0 1px var(--line-2)'
                        : 'none',
                    cursor: 'pointer',
                  }}
                >
                  {label}
                </button>
              ))}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function ToggleButton({ on, onChange }: { on: boolean; onChange: (v: boolean) => void }) {
  return (
    <button
      role="switch"
      aria-checked={on}
      onClick={() => onChange(!on)}
      style={{
        position: 'relative',
        width: 38,
        height: 22,
        borderRadius: 12,
        background: on ? 'var(--sampled)' : 'var(--bg-3)',
        border: on ? 'none' : '1px solid var(--line)',
        flex: '0 0 auto',
        transition: 'background .15s',
        cursor: 'pointer',
        padding: 0,
      }}
    >
      <span
        style={{
          position: 'absolute',
          top: 2,
          left: on ? undefined : 2,
          right: on ? 2 : undefined,
          width: 16,
          height: 16,
          borderRadius: '50%',
          background: '#fff',
          transition: 'all .15s',
        }}
      />
    </button>
  );
}
```

- [ ] **Step 2: Verify build**

```bash
npm run build
```
Expected: exits 0.

- [ ] **Step 3: Commit**

```bash
git add src/components/TweaksPanel.tsx && git commit -m "feat: G18 TweaksPanel — accent color, surface mood, canvas toggles, density"
```

---

### Task 11: Rewrite App.tsx

**Files:**
- Modify: `src/App.tsx` (full rewrite)

- [ ] **Step 1: Write App.tsx**

```tsx
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import Layout from './components/Layout';
import TweaksPanel from './components/TweaksPanel';
import Build from './pages/Build';
import Simulate from './pages/Simulate';
import Results from './pages/Results';
import Export from './pages/Export';

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route index element={<Navigate to="/build" replace />} />
          <Route path="build" element={<Build />} />
          <Route path="simulate" element={<Simulate />} />
          <Route path="results" element={<Results />} />
          <Route path="export" element={<Export />} />
        </Route>
      </Routes>
      <TweaksPanel />
    </BrowserRouter>
  );
}
```

- [ ] **Step 2: Verify build**

```bash
npm run build
```
Expected: exits 0.

- [ ] **Step 3: Commit**

```bash
git add src/App.tsx && git commit -m "feat: G18 App rewrite — prototype layout + TweaksPanel"
```

---

### Task 12: Update page components to match prototype layout

**Files:**
- Modify: `src/pages/Build.tsx`
- Modify: `src/pages/Simulate.tsx`
- Modify: `src/pages/Results.tsx`
- Modify: `src/pages/Export.tsx`

- [ ] **Step 1: Rewrite Build.tsx**

```tsx
import Palette from '../components/Palette';
import Inspector from '../components/Inspector';
import MetricStrip from '../components/MetricStrip';

export default function Build() {
  return (
    <>
      <Palette />
      <div className="workspace">
        <div className="canvas-wrap">
          <div className="canvas-grid" />
          <div className="canvas-placeholder">
            Canvas — drag nodes from the palette (G19)
          </div>
        </div>
        <MetricStrip />
      </div>
      <Inspector />
    </>
  );
}
```

- [ ] **Step 2: Rewrite Simulate.tsx**

```tsx
export default function Simulate() {
  return (
    <div className="workspace">
      <div className="workspace-tab">Simulate panel — G24</div>
    </div>
  );
}
```

- [ ] **Step 3: Rewrite Results.tsx**

```tsx
export default function Results() {
  return (
    <div className="workspace">
      <div className="workspace-tab">Results panel — coming in G24</div>
    </div>
  );
}
```

- [ ] **Step 4: Rewrite Export.tsx**

```tsx
export default function Export() {
  return (
    <div className="workspace">
      <div className="workspace-tab">Export panel — G25</div>
    </div>
  );
}
```

- [ ] **Step 5: Verify build**

```bash
npm run build
```
Expected: exits 0.

- [ ] **Step 6: Commit**

```bash
git add src/pages/ && git commit -m "feat: G18 page updates — Build tab gets palette+canvas+inspector+metrics, others placeholder"
```

---

### Task 13: Verify everything — build, lint, tsc, a11y

**Files:** (none — verification only)

- [ ] **Step 1: Full build**

```bash
npm run build
```
Expected: `tsc -b && vite build` exits 0.

- [ ] **Step 2: TypeScript check**

```bash
npx tsc --noEmit
```
Expected: no output (no errors).

- [ ] **Step 3: Lint**

```bash
npm run lint
```
Expected: exits 0.

- [ ] **Step 4: Verify generate-api still works**

```bash
npm run generate-api
```
Expected: generates types successfully.

- [ ] **Step 5: Start dev server and run a11y audit**

```bash
npm run dev -- --port 5199 &
sleep 3
```

Then use Playwright to:
1. Navigate to `http://localhost:5199/`
2. Verify redirect to `/build`
3. Verify dark theme renders (bg is oklch charcoal, not white)
4. Verify brand mark, topbar, tabs, provenance legend visible
5. Verify Build tab shows palette + canvas placeholder + inspector + metric strip
6. Click through Simulate, Results, Export tabs — verify each navigates
7. Run axe-core: verify 0 violations (score ≥ 90)

Expected a11y check: score ≥ 90 on dark theme.

- [ ] **Step 6: Kill dev server**

```bash
kill %1 2>/dev/null
```

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: G18 complete — prototype-identical shell, verified build/lint/a11y"
```
