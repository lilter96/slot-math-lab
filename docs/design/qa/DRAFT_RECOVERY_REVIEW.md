# Browser draft precision and recovery — 2026-10-12

## Implemented boundaries

Stored workspace JSON now passes the same original-number-token validation as imported models before hydration. Unsafe numbers anywhere in the project collection, nested drafts or tables reject the workspace rather than silently rounding it. Valid wide integers and string metadata retain existing supported semantics.

A rejected workspace is preserved byte-for-byte in a generated recovery key inside the verified owner's browser namespace. The current-recovery pointer also belongs to that namespace; foreign pointers are ignored. The original copy and pointer are read back before ordinary autosave is allowed to replace the active draft. Already retained identical originals are reused on reload; earlier distinct recovery keys are never deleted by this change. A recovery copy is an entire workspace backup, not an executable model JSON import.

The shell offers **Download stored workspace** using the untouched original string. Its notice remains available after new projects save and after reload. New projects can autosave separately once the original copy is safely retained. If backing up or reading storage fails, writes to the active draft are blocked for that page session. Current in-memory authoring and ordinary project backup remain available. Reload retries restoration/backup; no data deletion or hosted migration is performed.

Outgoing browser workspace snapshots are checked for unsupported numeric magnitudes/nonfinite values before serialization. A failing snapshot keeps the last saved draft and reports the existing backup warning. This does not complete every manual-input, server/export, AI or measurement boundary.

## Verification

- Build and exact-file ESLint pass (`draft-recovery-build.log`, `draft-recovery-lint.log`).
- Chromium new/affected 7/7 (`draft-recovery-chromium.log`): exact original download, independent new-draft persistence/reload, failed-backup write freeze across debounce/reload, malformed JSON deduplication, real safe-draft rename/autosave preserving numeric/string fields, foreign recovery-pointer rejection, existing verified-account changes and existing quota-error backup flow.
- Firefox/WebKit initial new cases 10/10 (`draft-recovery-cross.log`). An additional outgoing-snapshot test edits the actual Data/AST numeric field beyond the supported range, observes last-safe storage retained, then restores a valid value and verifies resumed autosave. Chromium 1/1 passes (`draft-recovery-write-chromium.log`); Firefox/WebKit 2/2 pass (`draft-recovery-write-cross.log`). No full suites.
- Four-size actual WebKit observations (1440×900, 1280×800, 1024×768, 390×844) show no document overflow. The recovery notice measures 61/61/94/144 px high and download control 36/36/36/44 px. Desktop and phone screenshots `draft-recovery-{width}.png` were visually reviewed; the phone message/actions wrap legibly with 44 px controls. These captures inspect the recovery Projects state, not every route.
- Required scoped Rider attempts exit 3 with missing solution context (`draft-recovery-rider.log`, `draft-recovery-test-rider.log`). Reformat/Full Cleanup are not claimed complete.

## Limits and next work

Recovery consumes browser quota. If quota cannot retain the copy/pointer, original storage stays protected and new edits remain in memory; the download is the usable backup path. Copies are retained rather than automatically deleted. The UI exposes the current retained original; archive browsing/deletion and full-Int64 authoring are not implemented. Access to the same browser/OS account is outside application namespace isolation. Digits already rounded and stored by an older version cannot be reconstructed.

Finish raw numeric editing/outgoing model/server export, AI and measurement AST inputs and typed-client/Results unsupported-response integration evidence. Then continue command-palette, real compositor performance and practitioner/Core/Rider requirements. This slice does not complete the design plan or establish release readiness.
