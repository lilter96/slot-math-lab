# Circular reel-strip editor review

Source 87c95d8. Existing generic shared reelState/spinReels authoring shapes are recognized structurally; no game ID, kernel dispatch, new atom, interpreter or PRNG change.

## Delivered

Ordinary Ancient Dragon and Woodland Whisper reels and both Le Militare paid/free sets now have an actual strip editor. It edits the circular period separately from the repeated tail, regenerates every associated uniform stop domain, and preserves unrelated fields/IDs/expressions and the other set. Shared stop definitions used by Le Militare refill remain shared. Customized/conditioned domains fail closed instead of receiving an incomplete raw-array mutation.

The UI shows columns, indices, symbols/IDs, raw uniform stop probability and cumulative probability for the selected index, full-period symbol counts/frequencies, keyboard add/remove/reorder, copyable IDs, sticky headers/index, and 25-index pagination. Paging limits rendered cells, not the source/export. CSV validates every reel/index/symbol before staging. Changes remain unapplied until actual compiler validation succeeds; Undo refuses newer canonical edits. Buffers/Undo belong to independent project IDs within the verified browser session. Obsolete CSV reads, unmounted validation and delayed focus cannot alter another project or steal a newer selection.

Overview derives the count of proven editable sets and links to /model/data?view=reels. Model-data tabs support keyboard roving/navigation and URI state; the current project is visible on phone too. Apply/Reset/Undo sit above the table; phone targets are 44px. Probabilities are explicitly raw-strip frequencies, not RTP or final-board win probabilities.

## Evidence and failures retained

- New mapping units: 5/5. All three actual model factories and both Le Militare sets; nonmutation, repeated tails, complete stop domains, unknown metadata, exact inverse, separate sets and invalid/customized/conditioned rejection.
- New/affected CSV units: 4/4 (two strip, two existing weights). Quoted IDs, arbitrary row order, gaps/duplicates/unknown IDs/malformed quoting.
- Initial Chromium new/affected: 8/8. Small independent arithmetic fixture proves actual API expectation 0.5 before edit and 1 after, plus complete seeded response equality after Undo. Existing weighted/free-spin/payout flows pass.
- After denser table/Overview/deep-link integration: Chromium 6/7. A genuine delayed-focus race overrode a newer cell selection; fixed the application with scope/trigger guards. Only failed/new/affected checks reran: 4/4, including the strengthened real-game case and late CSV read.
- Final new Firefox/WebKit cases: 12/12. Includes actual API arithmetic/replay, compiler abort, all ordinary game set Apply/Undo, pagination/viewport, conditional-source preservation and late CSV guard.
- Build and touched ESLint pass. Final project-name copy change received build/lint and refreshed rendering, not repeated suites.
- Sixteen final captures cover Ancient Dragon, staged edit, Le Militare free set and an imported conditioned bought feature at four requested sizes. Zero document overflow/probability proof badges; table scrolling stays inside its region. Desktop rows measured 39px; phone fields 44px. 125/150 cells are rendered for a 25-index page, while the full source remains intact. Desktop/phone screenshots inspected; Apply placement, table density, touch targets and project context corrected from actual review.

Logs: reel-commands-unit.log, reel-csv-unit.log, reel-editor-browser.log, reel-editor-final-browser.log (failure retained), reel-editor-followup.log, reel-editor-cross.log, reel-*-build.log. Capture metadata: reel-editor-audit.json; local images reels-{ancient,staged,free-set,conditioned}-{1440,1280,1024,390}.png. No complete suites or backend tests ran for these frontend changes; actual compiler/evaluation/round endpoints are exercised directly.

Required Rider Reformat attempts still exit 3 with missing solution; frontend Full Cleanup remains unavailable. Logs reel-*-rider.log retain per-change attempts. Scoped checks do not replace required cleanup.

## Still required

A Woodland Whisper bought-feature graph contains materialized showing patterns, weighted pattern selection and conditional stop subsets. Its source is preserved and explicitly unsupported by this ordinary mapping; this is not implementation of the required coupled conditional editor. Next build a proven generic mapping that updates all those dependencies together, guarding unknown/modified source and integer weight budgets. Do not regenerate an entire game from its name or silently change its rules.

Further source/keyboard/table audits, scored design acceptance, practitioner feedback and Core/Rider release gates remain open. This review does not claim goal completion or certified performance.
