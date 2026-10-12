# Data/evidence safety and actual plugin capabilities

Source f4cce7f. No backend, kernel, serializer, PRNG or graph-atom changes.

Generic data-row deletion requires confirmation and explicitly warns about shifted indices. Adding a data field or expression cannot overwrite an existing value/AST. Evidence-input removal confirms the loss of identity/content. File hashing applies only to the same surviving input object, project, nested context and latest file task while its editor is mounted. Removed/reindexed inputs, closed editors, another project and newer uploads discard obsolete work. Aggregate byte checks read the current retained inputs; 64 KiB per file and 128 KiB total are enforced before replacement.

Plugin registration always rejects unsupported assembly loading in the actual API, and there is no DELETE endpoint. The previous removal hook only hid browser data while claiming a backend operation. Removed that false mutation and replaced the panel with the real read-only registry, explicit unsupported operations, keyboard-accessible detail selection and retryable errors. The real registry provides ID/contract/version/conformance, not evaluation-method compatibility; no fabricated Exact/Sampled label is derived from it. Untyped OpenAPI response data is checked at the boundary; empty-body errors and malformed registry data do not become an empty success.

## Verification

- Frontend build and all touched ESLint passed. Initial build exposed the existing untyped plugin response (`never`); corrected by validating unknown response data rather than inventing generated contract fields.
- Chromium: 11/11 new/affected cases (eight new data/evidence/registry workflows, two feature-flag checks and existing structured-state preservation).
- Firefox/WebKit: 18/18 new/affected cases (eight new workflows plus structured-state case in both engines).
- Strengthened actual embedded-byte/export check afterward: only that changed test reran, Chromium 1/1 and Firefox/WebKit 2/2. It independently checks Node SHA-256, exact decoded embedded bytes and retained unrelated state/inputs.
- Logs: `data-input-safety-build.log`, `data-input-safety-lint.log`, `data-input-safety-browser.log`, `data-input-safety-cross.log`, `data-input-safety-export.log`, `data-input-safety-export-cross.log`.
- Sixteen captures: duplicate-field error, actual file-backed evidence, actual empty server registry and injected HTTP503 recovery, each at 1440×900, 1280×800, 1024×768 and 390×844. Metadata `data-input-safety-audit.json` records zero document/tab overflow and zero plugin-panel proof badges. Phone evidence and desktop registry-failure images inspected. Native confirmations are verified through actual browser dialog messages/accept/cancel, not claimed as app screenshot contents.

No complete test suite, backend suite or unrelated units ran. Cross-browser counts are scoped, not a new full release gate. Registry success fixtures in tests are injected API contracts; screenshots use the actual empty registry and clearly recorded failure injection.

Required Rider Reformat attempts exit 3 (`solution can't be null`), including changed API hook and strengthened test; Full Cleanup remains unavailable outside the solution. Actual logs `data-input-safety-rider.log`, `data-input-safety-hook-rider.log`, `data-input-safety-export-rider.log`. No successful cleanup is claimed.

## Remaining work

Physical strips are present in three bundled game families; the earlier top-level-only detector missed them. See MODEL_CAPABILITY_INVENTORY and REQUIREMENTS_AUDIT. Implementing their guarded source mappings and complete keyboard/table behavior remains required. Core/Rider/practitioner/release gates remain open.
