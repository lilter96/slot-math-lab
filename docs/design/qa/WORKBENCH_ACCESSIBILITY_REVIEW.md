# Workbench legibility and keyboard access

Source `b752c03`, 2026-10-12. This is a scoped UI follow-up, not full WCAG certification or a practitioner acceptance study.

## Changes

Working graph details, source IDs, AST labels, table fields, hints and provenance tags now use at least 12px CSS text in the audited surfaces. The primitive inspector exposes outcome/weight/value/remove control names and a 36px remove target. Its empty state points to Add nodes rather than describing only dragging. Weight parsing, edits, validation, source serialization and evidence methods are unchanged.

Models without board geometry use a compact, unboxed structure notice and a responsive facts table. No board or named mechanic is invented. A model with no definitions points to its complete graph; the empty definition-list divider is removed. Models with declared geometry retain their own board/symbol projection, including the reference Nine Lives 6×5.

The data/expression dialog uses the existing charcoal semantic tokens, readable headings and 36px controls. Cleanup after closing restores focus to the connected original trigger. Native modal focus and Escape behavior remain available. On narrow screens its section controls wrap, rather than clipping horizontally. Metric labels wrap around enlarged method tags; the no-mechanic graph link uses the sampled color with measured contrast.

## Targeted checks

| Evidence | Result |
|---|---|
| Frontend TypeScript/build | Pass; final CSS-only layout adjustments rebuilt through Vite |
| Touched-file ESLint | Pass |
| Chromium | 6/6 affected cases pass: two new access/compact-layout cases, visible graph/search/camera/export, real saved evidence/edit withholding, four-size Nine Lives geometry and structured state preservation |
| Firefox/WebKit | 4/4 new cases pass: compact no-board layout and keyboard dialog/labelled inspector in both browsers |
| Final rendered audit | 20 actual captures across five states and four sizes; no document overflow |
| Scoped accessibility names | No unnamed exposed button/textbox/combobox/checkbox/link/tab in the captured AX trees |
| Audited hints/graph details/tags | No computed CSS font size under 12px in those captured selectors |
| Modal section controls | No internal tab-row overflow at any of the four sizes |
| New inline link contrast | 8.9245:1 against the measured app background |
| Required Rider cleanup | Incomplete: scoped frontend Reformat exits 3; Full Cleanup remains unavailable outside the .NET solution |

The existing backend/Core/unit suites were not run for these presentation changes. No complete browser suite was repeated. Existing mathematical/preservation assertions remained in the selected integration checks. New keyboard coverage explicitly checks Enter, modal focus, Escape/focus return and reduced-motion transition duration.

## Visual review and limits

Images `legibility-{no-board,nine-lives,graph,empty-inspector,data-dialog}-{1440,1280,1024,390}.png` are actual production-preview captures, excluded from Git. Desktop no-board and graph plus mobile data dialog were visually inspected. The first review exposed a low-contrast default blue link and narrow modal tabs; final CSS fixes and actual rerendering corrected both. The graph capture now invokes Fit after each viewport resize; preserving a phone camera before desktop capture was not treated as a readable default fit.

The [rendered audit](workbench-legibility-audit.json) records real DOM/AX observations and source. Its font checks are computed CSS sizes, not physical text size at every graph zoom. Fits, 100% and node focus remain available for spatial legibility. Legal library attribution and non-working decorative labels were not included in the 12px assertion. Returned reference light-analysis numbers are actual API values, not a calibration or changed mathematical result.

These checks cover the listed surfaces and interactions; they do not prove all screen-reader combinations, every focus state, every drag alternative, all component contrast or the ≥4/5 usability target. A requirement-by-requirement completion audit and practitioner review remain necessary. Required source cleanup and full Core release verification remain unresolved; no release/deployment is claimed.
