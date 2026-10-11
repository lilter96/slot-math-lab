# Final viewport review

Final production-preview captures were refreshed after private-project support and the symbol-label wrapping fix. `verified-{projects-empty,projects-populated,ancient-dragon-overview,overview,model-data,model-features,simulate,results,export,build,build-inspector,nested-free-spin,cluster-payouts,debug-hit,results-selected,offline-build}-{1440,1280,1024,390}.png` are 64 actual captures at 1440×900, 1280×800, 1024×768 and 390×844. Additional captures cover browser save rejection on mobile and unavailable session verification. Images are excluded from Git; viewport and contrast evidence are versioned separately.

| Expected | Observed / action |
|---|---|
| Several private games instead of one default sample | Empty collection and three independent projects: Ancient Dragon, X7 Club and Nine Lives reference; no automatic initial game |
| Model-specific geometry | Actual Ancient Dragon 5×3 metadata and Nine Lives 6×5; geometry view is explicitly not a generated spin |
| Long authored labels remain legible | Mobile Dragon symbol names initially collided; wrapping corrected them without changing authored names |
| Large graph without unused inspector | Inspector unmounted before selection; Reset view frames the root. Tablet/phone inspector is an overlay with a visible close action; controls under the overlay are used after closing it |
| Useful mobile evidence | Actual owned REF-A run, 100 paid rounds, is visible in the selected Results panel after scrolling it into view |
| Real Debug evidence | Seed-1 BONE hit highlights five returned positions; viewport shows part of the scrollable board, with no fabricated event-to-node mapping |
| Explicit save failure | Mobile alert keeps the selected model visible and offers JSON/server backup; browser test downloads the actual preserved graph |
| Offline trust | Verified identity is read before aborting math APIs; graph shows unavailable values and zero proof badges. Fully unavailable identity gets a retry screen before stores load |
| Legible supporting text | Minimum audited text-token contrast is 5.0069:1 across three text and three surface tokens |
| Keyboard access | First Tab exposes Skip; Enter focuses main; arrow navigation opens the next section; Escape closes Appearance and restores trigger focus |
| No document overflow | 64/64 measurements pass at all four requested sizes |

Visually inspected populated Projects on desktop/phone, Ancient Dragon phone overview after wrapping, framed root graph, desktop and phone inspector, desktop actual Debug hit, phone selected Results, and phone save-error state. Internal scroll regions and local horizontal toolbar/navigation scrolling remain intentional. Phone canvas movement/connections are read only, while editing forms are available. A real verified QA owner and actual short coin calculation supply saved-evidence captures. No forced pointer clicks or substituted measurements are used. Color-token checks and this local review are not complete WCAG certification or a practitioner usability study.

[Final keyboard audit](final-keyboard-audit.json) records six passing checks on the final bundle with real session verification and unavailable math APIs.
