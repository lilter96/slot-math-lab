# Weighted editing and project import async scope

Source: `5913d07`.

WeightedDrawEditor now invalidates CSV reads on newer files, manual edits, Reset, Apply, source/project/path changes and unmount. Obsolete reads cannot create retained drafts or replace newer staged values/errors. Validation uses an actual AbortSignal and checks the surviving task, full canonical input, independent project identity and mechanic path before applying. Valid edits still retain the existing compiler, Apply/Undo, source preservation and independent expectation contract.

Projects uses a common operation generation for local JSON imports and server-version reads. A newer import or server open cancels prior work; creating/selecting another project or unmounting invalidates pending operations. File inputs reset before the awaited read, allowing a second selection while the first is pending. No access/sharing or mathematical contracts changed.

## Evidence

- Build and touched ESLint pass: `weighted-async-build.log`, `weighted-async-lint.log`; final test-only lint `weighted-async-test-recheck-lint.log`.
- Initial Chromium: 7/8 pass (`weighted-async-chromium.log`). The unmount case released its delayed file immediately after a navigation click, before React had actually removed the editor. Added an explicit editor-absent assertion before release. Only this failed case plus the new pending-Reset case reran: 2/2 pass (`weighted-async-chromium-recheck.log`). No source changes were needed for that test correction.
- Firefox/WebKit: 16/16 new and affected cases pass (`weighted-async-cross.log`). They cover six delayed CSV/validation/library-import cases, real server-version copy opening and weighted Apply/Undo/export with an independently checked API expectation. The existing personal-project nested edits/independent copies/geometry case also passed in the initial Chromium selection.
- Delayed validation uses the real compiler response, then releases it after switching to another project with identical authored JSON. The actual fetch signal is observed aborted; the new project retains weight 1 and has no inherited Undo. Successful validation and Undo remain verified by the affected existing scenario.

This is nonvisual lifecycle work; no new screenshot review is claimed. No full suites were launched. Required Rider Reformat attempts again exit 3 (`solution can't be null`); Full Cleanup remains unavailable. Logs: `weighted-async-rider.log`, `project-import-async-rider.log`, `weighted-async-test-rider.log`, `weighted-async-test-recheck-rider.log`.

Remaining work includes direct nested-path/frame race evidence, other validation surfaces and the table/rubric/practitioner/Core/Rider gates. Passing this slice does not prove whole-goal completion.
