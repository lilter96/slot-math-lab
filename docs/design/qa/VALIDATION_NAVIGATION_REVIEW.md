# Compiler diagnostic navigation review

Source: 286c257. No math compiler, graph serialization or execution changes.

Compiler diagnostics resolve their existing generic library-node namespace to an authored node or edge. Ambiguous IDs and recursive definitions do not invent a location. Focus opens the corresponding nested mechanic; edge focus selects the connection and frames both endpoints. Validation state is keyed by independent project identity and canonical source; unmount aborts the pending request, including identical-model copies.

## Evidence

- Frontend production build and scoped ESLint passed.
- New source-resolution units: 2/2 passed (`validation-navigation-unit.log`).
- Initial affected Chromium: 3/3 passed, including existing node focus and keyboard workflow (`validation-navigation-browser.log`).
- Added real nested diagnostic: 1/1 Chromium passed (`validation-navigation-nested-browser.log`).
- Firefox/WebKit: 6/6 passed, all three new workflows (`validation-navigation-cross.log`).
- Four real compiler-error captures: `validation-edge-{1440,1280,1024,390}.png`; `validation-navigation-audit.json` records selected local edge, both source nodes and zero document overflow. Phone screenshot inspected: controls wrap, advanced graph remains explicitly limited/read-only and the graph lies below the working controls.

These checks do not claim a new complete browser/API/Core pass. Native compiler validation is not an RTP proof. No event-to-node trace attribution is added.

Required Rider Reformat attempts exit 3 (`solution can't be null`); Full Cleanup remains unavailable outside the solution. See `validation-navigation-rider.log`, `validation-navigation-test-rider.log`, `validation-navigation-nested-rider.log` and the required cleanup source list. Cleanup remains a release blocker.
