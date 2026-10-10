# Verification strategy

Every push and pull request runs the complete Core, API integration and frontend unit suites, the independent rational Dog House expectation, exported graph and metric-catalogue drift checks, frontend build/lint, and the critical browser contracts. Mathematical distributions, expression semantics, measurement statistics, seed/worker equivalence and transport state transitions belong in Core/API or browser-free frontend unit tests. Browser checks prove the actual user workflow and rendering across those boundaries.

## Browser execution

From `frontend`, run:

```sh
npm run build
npm run test:e2e:critical   # Mandatory integration contracts: 14 scenarios
npm run test:e2e            # Complete browser regression: 72 scenarios
npm run test:unit           # Browser-free frontend logic: 85 tests
```

The ordinary CI browser job selects the explicit `@critical` Playwright tags. All browser scenarios remain available in the complete suite; selection does not mark the others skipped or remove their assertions. The `Full browser regression` workflow runs daily at 02:00 UTC and can be dispatched manually on the candidate branch before release. Check the complete regression against the actual release commit; a green nightly run on an older commit does not verify a new release. Both workflows use the same reusable browser job, real API/database, build, fixtures and Chromium configuration.

The mandatory contracts cover:

| Area | Required browser evidence |
|---|---|
| Constructor | A fresh project paints the full graph; nested mathematics can be edited and changes actual payouts; authored 98% expectation and all analysis modes execute through the UI. |
| Real-time execution | One socket survives navigation; repeated disconnects recover the same run and cumulative counts; authentication rejection requires explicit authentication wake. |
| Measurement workspace | A scoped free-spin metric can be authored, displayed, exported, restored and replayed against pinned input. |
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

CI rejects `test.only`, keeps the HTML report, failure context/screenshots and retry traces for 14 days, and uploads artifacts even when a job fails. A retry-assisted pass is reported separately from a first-attempt pass in verification records. An affected workflow can be run directly with `npx playwright test e2e/<file>.spec.ts`; run the complete regression after changes to shared runtime contracts or before release.

Playwright's [official tag/filter mechanism](https://playwright.dev/docs/test-annotations#tag-tests) provides the selection without a second set of test implementations.
