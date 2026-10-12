# Validation lifecycle and source-change cancellation

The shared applyValidatedModel helper observes project identity, canonical model and mechanic path throughout compiler validation. Any scope change aborts the transport, even if the user restores the original source before the delayed response arrives. Stale bases fail before sending a request. Observation ends before applying the accepted transaction, so its own source changes do not invalidate the commit. Subscription cleanup runs on success and failure. Free-spin and cluster-payout forms now abort on unmount and superseding requests; cancelled callbacks do not publish success, errors or Undo into a replacement form.

## Source and evidence

- Source `482c548`. Build and scoped ESLint pass (`validation-lifecycle-final-build.log`, `validation-lifecycle-lint.log`, `validation-lifecycle-final-lint.log`, final test-only lint logs).
- Initial Chromium 8/9 pass (`validation-lifecycle-chromium.log`). The payout return test did not reopen the Cluster payouts tab, so its field was absent. Corrected the navigation; only the failed test reran, 1/1 pass (`validation-lifecycle-chromium-recheck.log`).
- Initial Firefox/WebKit 15/16 pass (`validation-lifecycle-cross.log`). Existing late-strip cancellation used a requestfailed-event observation that WebKit did not emit while its intercepted response was held. Observing the real fetch AbortSignal preserves the native transport and released-response assertions. Only that failed WebKit scenario reran, 1/1 pass (`validation-lifecycle-webkit-recheck.log`). Initial failures are retained.
- New tests use actual successful compiler responses held until invalidation. They prove free-spin/payout unmount cancellation with retained unapplied drafts; payout source change and restoration while mounted; and returning from a nested mechanic while the complete model remains unchanged. Fresh Apply/Undo after cancellation succeeds.
- Affected existing scenarios verify real free-spin execution, cluster payout/state/metadata/Undo, ordinary strip independent expectation and restoration, conditional strip independent conditioning and seeded Undo, and project-switch strip cancellation. No mathematical/API/kernel changes or full suites.

No visual layout changed; no fresh screenshot claim. Required Rider Reformat attempts still exit 3 (`solution can't be null`), and Full Cleanup remains unavailable. Logs: validation-lifecycle-rider.log, validation-lifecycle-test-rider.log, validation-lifecycle-final-rider.log, validation-lifecycle-test-recheck-rider.log, validation-lifecycle-reel-test-rider.log. ESLint is supplementary.

Still outstanding: controlled delayed toolbar focus and nested generic data-execution race evidence, remaining table contracts, scored visual/practitioner review, and Core/Rider release gates. This slice does not establish whole-goal completion.
