# Keyboard connections and project task guards

Source: `026e7e0`. This is scoped feature evidence, not a whole-goal completion claim.

The desktop graph toolbar opens a native dialog with labelled source/output/target/input selects. Enter creates a connection; Escape restores trigger focus. Actual canonical ports determine both rendered handles and pointer/keyboard validation. Unknown, missing or incompatible ports and exact duplicates are rejected. Compiler-supported Symbol → String and Boolean → Trigger conversions remain available. Separate branch outputs can connect to the same target with independent edge IDs. Export preserves the chosen ports; a real compiler request validates the exported two-branch fixture. Mobile and disabled interaction modes retain explicit limited authoring.

Toolbar node/edge/picker focus cancels scheduled frames on unmount or newer focus and checks project, mechanic path and current selection before fitting the native camera. File reads also check request generation, project/path and canonical input; an obsolete import cannot replace a newer independent project. The connection dialog unmounts on project/path changes, including when authored node IDs match.

## Validation

- Build and touched-file ESLint pass (`keyboard-connection-build.log`, `keyboard-connection-lint.log`).
- Initial new units: 1 pass / 1 failure (`keyboard-connection-unit.log`). The failed expectation incorrectly omitted legacy Draw's canonical `in: Wins` port. Corrected to the real serialization contract; only that failed test reran and passed (`keyboard-connection-unit-recheck.log`). Both new cases have passing evidence.
- Chromium: 8/8 new and affected connection, import, compiler-focus and visible-node/camera cases (`keyboard-connection-chromium.log`). Two subsequently added dialog-scope/viewport cases pass separately (`keyboard-connection-extra-chromium.log`).
- Firefox/WebKit: 14/14 affected cases, then only the two added cases in each browser, 4/4 (`keyboard-connection-cross.log`, `keyboard-connection-extra-cross.log`).
- Four actual viewport captures: `keyboard-connection-{1440,1280,1024,390}.png`. Capture assertions found no document overflow. Chromium desktop and phone images were inspected: readable labelled modal controls on desktop, explicit read-only graph notice and disabled Connect control on phone. The final files were refreshed by WebKit's scoped capture case.

No full suites, backend/kernel changes, deployment or publication. Required Rider Reformat attempts exit 3 (`solution can't be null`); Full Cleanup remains unavailable. Logs include `keyboard-connection-rider.log`, `keyboard-connection-test-rider.log`, `keyboard-connection-recheck-rider.log`, `keyboard-connection-final-test-rider.log`. ESLint does not replace these required passes.

Remaining async audit: generic data execution and other file/editor request paths. Existing root/nested focus checks prove usable navigation; a separately controlled delayed-frame test across same-ID project/path changes is still needed for direct race evidence. Required rubric, practitioner, Core and Rider gates remain open.
