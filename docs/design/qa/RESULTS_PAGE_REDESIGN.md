# Results page redesign and whole-page inspection

## Direction and delivered composition

The user's 2026-10-12 correction requires substantial page-level visual changes and inspection of the entire page, including lower forms. AGENTS.md now records both requirements. The first summary-only review missed unstyled verification forms; this was a real review gap, corrected before committing this page.

Retain the project's charcoal/Plex identity: canvas #0c1117, base #0f141b, panels #18212c/#22303d, muted #afc0d0, primary text #edf4fb, with semantic sampled blue/exact green. Existing tokens remain authoritative. The consulted design skills recommended a dense analytical surface; their generated enterprise marketing/contact-sales pattern and replacement fonts/palette were rejected as unrelated to this workspace.

Results now uses a quieter page heading, private archive rail and one selected calculation as the primary report context. The analytical board places observed RTP beside its interval plot, follows with four supporting measures, then a separate precision assessment. Reference and next-measurement planning share a divided surface. Integrity checks precede detailed analysis. Distribution, comparison and reproducibility retain their operational source and pinned identities. Phone archive access is explicit rather than placing a large archive before every report; the phone heading is compact, controls remain touch-sized and wide tables scroll internally.

The lower page is included in the redesign: exposure statistics, expanded enumeration, verification tools, retained diagnostics and footer. Verification tools have component-owned CSS, labelled spaced dark controls, grouped fieldsets, filled-table styles, disabled states and selected tabs. Arrow/Home/End navigation is implemented for their tablist. Empty comparison metrics explain what to configure instead of displaying an empty native border. The shared component styling also applies when reached through Simulate.

Unsupported original numbers in pinned `/runs/:id/evidence` responses are now rejected before JSON conversion. Results presents a retryable error without a rounded pinned graph or numeric result. This does not certify every typed-client read path.

## Actual validation

- `results-scroll-build.log`, `results-scroll-lint.log`, `results-expanded-lint.log`: build and scoped lint pass.
- `results-final-chromium.log`: 3/3 page scenarios (whole report, empty/unavailable, unsafe pinned input); `results-final-cross.log`: the same scenarios passed Firefox/WebKit, 6/6, before the lower-tool follow-up.
- `results-full-page-chromium.log`: the existing native-settlement/external-input/verification-experiment scenario passed, along with the empty/unsafe cases. The new whole-page case initially failed because a generic tabpanel selector matched both the report and its new nested tool panel. It was corrected to the named report panel; failure evidence is retained.
- `results-expanded-chromium.log`: final whole-page workflow 1/1; `results-expanded-cross.log`: final expanded workflow 2/2 Firefox/WebKit. It visits all four Results sections at 1440×900, 1280×800, 1024×768 and 390×844, all four verification tools with populated variants/atoms/states, expanded enumeration, exposure and footer. It verifies document overflow, mobile archive selection, pinned graph opening and main-tab keyboard navigation.
- `results-composition-chromium.log`: affected saved-version reference/export/original-draft preservation scenario passed using the real server.
- Local fixture: actual 10,000-round seeded run; real exact reference is 75%. Displayed sampled results come from that run, not authored screenshot numbers. Empty/unavailable/unsafe responses are stubbed deliberately. No full repository suites ran.

## Visual inspection

Inspected rendered desktop and phone screenshots directly, rather than treating screenshot creation as review. The initial summary was reorganized after inspection. Desktop mobile-toggle leakage, standard blue recovery links and the user-reported native verification fields were corrected.

Reviewed `results-page-chromium-*` across Overview/Distribution/Compare/Reproducibility plus empty/unavailable. Below-fold review includes `results-exposure-*`, `results-enumeration-*`, `results-tools-*` for all four tabs, phone `results-tool-fields-*` for each form group, and `results-bottom-*` through the footer. Some tall element screenshots are clipped by the app's scroll container and include fixed-header artifacts; separate viewport captures of the form groups supply the usable phone inspection. They are not claimed as complete single-image phone captures.

Self-review against the design rubric: task clarity 2, domain fidelity 2, trust 2, hierarchy 2, interaction 2, accessibility 1, identity 2, integration 2 = **15/16** for the inspected states. Accessibility remains qualified: this is no formal contrast/screen-reader audit. The basic round fixture and existing verification scenario do not certify every possible tracked-measurement or session-result state; inspect those when redesigning Simulate. Human usability and throughput claims remain unproven.

## Cleanup and remaining work

Rider standalone attempts exited 3 with `solution can't be null`; required Reformat/Full Cleanup remains outstanding. See RIDER_CLEANUP_REQUIRED.md for the exact source list and logs. The page is a local change, not a deployment or overall production-readiness claim.

Continue with coherent **My projects** and **Simulate** page passes, including all their tabs/forms and full-scroll visual review. Existing malformed/nested imports, typed-client evidence, measurement/options boundaries, command palette, compositor performance and practitioner/Core/Rider release gates remain in the requirements audit. Do not return to isolated cosmetic slices or reset already completed functionality.
