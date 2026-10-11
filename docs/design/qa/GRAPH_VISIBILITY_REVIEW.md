# Visible graph elements

Source `1973c4d`, 2026-10-12. React Flow's native `onlyRenderVisibleElements` is enabled. The installed implementation initially renders unmeasured nodes to obtain dimensions/handle bounds, retains their internal records when DOM elements unmount, and then selects visible nodes and edges from viewport geometry. No second model, custom spatial index or approximate node dimensions were introduced.

The full Zustand model remains authoritative for editing, node search, nested projections and export. Search can focus an unmounted node using its retained geometry; Fit selection/Reset view and project/mechanic cameras continue to operate on the complete graph. Partially visible and actively dragged nodes remain supported by the library.

## Scoped evidence

- Production TypeScript/build and touched-file ESLint pass.
- Initial affected Chromium run: four pass, one old empty-subgraph test fails because its deletion loop treated visible DOM nodes as the full model. The loop now enumerates the complete source-backed node picker, waits for each deletion, and retains the empty model/DOM and parent-return assertions. Only the failed scenario was rerun: it passes.
- Passing Chromium coverage includes the new 84-node visibility/search/camera/export scenario, node selection/inspector, compiler error focus, atom extraction/reuse and empty-subgraph return.
- Firefox/WebKit: four scoped cases pass, covering the new complete-model preservation workflow and corrected empty-subgraph workflow in both browsers.
- The new export check compares the entire canonical model before/after focus, including all 84 free-spin nodes, edges, expressions and unknown source fields. It does not infer preservation from visible node counts.
- No backend, API, unit or complete browser suite was run for this presentation-only change. Mathematical execution was not changed.

## Actual rendering observation

A full fit renders 84 nodes and 83 edges. Focusing the final node at 100% renders five nodes/seven edges while the full source-backed picker still contains all 84 nodes. Mobile renders two nodes in its smaller window. These are DOM counts, not a measured speedup or FPS guarantee.

Five actual images are `graph-visible-full-1440.png` and `graph-visible-focused-{1440,1280,1024,390}.png`, excluded from Git. The desktop focused graph was visually inspected; clipped edge/node portions at viewport boundaries are intentional. All four focused sizes have no document overflow. [Rendering audit](graph-visibility-audit.json) records source and viewport counts.

## Remaining acceptance

Required Rider cleanup remains unresolved: scoped standalone Reformat exits 3 with `solution can't be null`, and Full Cleanup cannot execute for frontend files outside the solution. No required-pass success is claimed. Legibility/accessibility, practitioner acceptance and full Core release verification remain separate open requirements. Native initial measurement briefly renders the whole graph; offscreen filtering follows accurate measurement rather than fabricated bounds.
