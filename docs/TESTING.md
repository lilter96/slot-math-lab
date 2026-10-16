# Verification strategy

CI checks exported Nine Lives graphs and the independent Fraction rule oracle, then runs Core, API, frontend logic and critical browser regressions. The full game has no Exact RTP proof; the small component fixture has a separately derived exact expectation.

## Browser execution

Local iterations follow **AGENTS.md**: run only new or affected tests; run complete suites after a substantial integrated feature or an explicit user request. Fixes after a failed full run receive failed/affected checks; documentation-only edits require no tests. The commands below are available suite entry points, not a per-iteration checklist.

From `frontend`:

```sh
npm run build
npm run test:e2e:critical   # CI critical integration contracts
npm run test:e2e           # Complete browser regression
npm run test:unit          # Browser-free frontend logic
```

The ordinary CI browser job selects the explicit `@critical` Playwright tags. All browser scenarios remain available in the complete suite; selection does not mark the others skipped or remove their assertions. The `Full browser regression` workflow runs daily at 02:00 UTC and can be dispatched manually on the candidate branch before release. Check the complete regression against the actual release commit; a green nightly run on an older commit does not verify a new release. Both workflows use the same reusable browser job, real API/database, build, fixtures and Chromium configuration.

The mandatory contracts cover:

| Area | Required browser evidence |
|---|---|
| Constructor | My projects is the entry point; explicitly opening a project paints its graph; nested mathematics can be edited and changes actual payouts; Nine Lives runs and replays through the UI; the small component fixture has an independent Exact expectation, while full-game Exact may be unavailable. |
| Private workspaces | Multiple games and same-template copies retain independent edits/nested context; verified accounts have separate browser state and server archives; foreign config/run/experiment/realtime access is denied. |
| Real-time execution | One socket survives navigation; repeated disconnects recover the same run and cumulative counts; authentication rejection requires explicit authentication wake. |
| Measurement workspace | A scoped free-spin metric can be authored, displayed, exported, restored and replayed against pinned input. |
| Verification experiments | Constructor settlement and embedded rule inputs, native finite sampling/resource references, automatic named arms, retained results and reload. |
| Results | Reference calculation, JSON/CSV/HTML exports and opening the graph preserve the original saved version and current draft. |
| Export failures | Quota recovery, wrong run identity, server errors and cancelled reads withhold invalid downloads. |
| Acceptance criteria | The native editor validates a predeclared confidence family and retains the original profile during replay. |
| Typed state | Record filtering and numeric record-field selection preserve fractions/nested arrays; invalid paths fail visibly; both engines retain identical evidence. |
| Final evidence recovery | Cached scalar session counts are distinguished from pending detailed evidence; snapshot quota recovery restores the complete session analysis. |
| Interrupted features | Failed execution preserves diagnostic lifecycle boundaries without publishing partially settled numeric values. |
| Responsive UI | The simulation workspace fits the mobile viewport with accessible controls and charts. |

The full suite additionally covers all seven catalogue examples, detailed WS fault combinations, archive paging, comparison, feature accounting, expression/UI boundary cases and the remaining game controls. Add mathematical edge cases to the lowest layer that can independently prove them. Promote a browser regression to `@critical` when it protects a distinct release-blocking user contract; avoid promoting every statistic or rendering variant.

## Isolation and failure evidence

Browser runs use one worker because their real API shares resource and quota budgets. Tests retain production retry deadlines and cancel jobs they created; parallelizing them against the same server would trade time for quota waits and worker contention. Larger parallel runs need independently provisioned API/database instances.

CI rejects `test.only`, keeps the HTML report, failure context/screenshots and retry traces for 14 days, and uploads artifacts even when a job fails. A retry-assisted pass is reported separately from a first-attempt pass in verification records. An affected workflow can be run directly with `npx playwright test e2e/<file>.spec.ts`, narrowed with `--grep` when appropriate. Shared runtime changes also use affected tests during ordinary iterations; a complete local regression is reserved for a substantial integrated feature or an explicit request. Required release verification applies at release time.

Playwright's [official tag/filter mechanism](https://playwright.dev/docs/test-annotations#tag-tests) provides the selection without a second set of test implementations.
