# Overview saved-evidence review

Feature source `51fe9ad`; recovery-copy follow-up `81af758`, 2026-10-12. Only new/affected checks were run. This does not replace the earlier complete `b9d92a1` regression.

## Behavior and authority

Overview reads the newest completed owned run whose canonical model fingerprint matches the submitted current model. The fingerprint uses the existing server serializer and comparison convention: document ID is excluded; every other persisted model field participates, including parameters, definitions, expressions, data and name. Selection uses completion date, then creation date and ID. The original pinned config/version must still exist and its recomputed hash must equal the saved run's config hash. Pending, foreign, unrelated and unverifiable input records are excluded. The read-only POST `/api/runs/latest-model` creates no config, version, job or calculation; unused diagnostic artifacts are not included in this lookup response.

The UI additionally validates the existing evidence/snapshot contract, terminal metrics, sample count and completion date. It shows actual sampled RTP, paid-round count, seed, date, original run ID, saved input version/hash and an Inspect saved run link. The approximate 95% round interval is shown only when the existing execution/sample uncertainty rules allow it. N=1 and zero-variance/dependent/early-stop cases do not acquire unsupported intervals. An actual zero remains numeric zero. A saved Monte Carlo result never receives an Exact badge, configured targets are not substituted, and sampling is not certification.

An owned lossless copy may reference a matching immutable calculation, following the existing ID-independent archive comparison contract. The panel says Matching pinned model and names the original run/version; it does not assign that run to the new copy's simulation session or copy mutable analysis state. Different parameters, names or known model fields require different evidence. This is distinct from sharing between users, which remains unavailable.

Query identity includes the verified owner namespace, personal project identity and canonical authored input. A new source has a new query; obsolete requests are cancelled. Pending refreshes and failed reads withhold cached values/badges. Network/timeouts have concrete connection/retry messages. Unknown/malformed or numerically inconsistent saved evidence gets an error with recovery, rather than a manufactured point estimate. No evaluation is forced on first paint.

## Verification

| Check | Result / coverage |
|---|---|
| Backend build | .NET solution passes; existing analyzer/pruning warnings remain |
| Scoped API tests | 3/3 pass: newest matching pinned source, no mutation/admission, different payout cap with the same name, pending/unrelated/unverified rejection, ID-independent comparison, old pinned version and real two-user/anonymous boundary |
| New unit tests | 3/3 pass: independent Bernoulli mean/variance/interval, invalid/stale/partial/inconsistent source rejection, N=1 actual zero without interval |
| Scoped Chromium | 3/3 pass: actual saved calculation, parameter change/project switch, delayed response, failed refresh/retry, existing four-viewport Nine Lives geometry/source logic |
| New Firefox/WebKit cases | 4/4 pass: real result/edit/isolation and delayed/offline recovery in both browsers |
| Frontend type/build and touched ESLint | Pass; OpenAPI/schema types regenerated through the existing script |
| Final copy/style rendering | Final production bundle rebuilt and inspected with real saved results and unavailable reads; no suite repeated for the wording/style-only follow-up |
| Viewports | Eight actual captures at four sizes; no document overflow; one supported sampled badge in saved states and zero in failed states |
| Required Rider passes | Incomplete; scoped backend Reformat/Full Cleanup time out with missing composition assembly, frontend Reformat exits 3; exact files remain in RIDER_CLEANUP_REQUIRED.md |

The first frontend type build found an unreachable error branch in the generated client’s success-only response type. Checking the HTTP response status before validating the payload corrected it; the final TypeScript/build gate passed. Failure evidence remains in ignored local logs. Mathematical assertions and isolation checks were retained.

## Visual evidence

`overview-evidence-{saved,unavailable}-{1440,1280,1024,390}.png` are actual local screenshots, excluded from Git. A validated dedicated QA account and a completed 100-round REF-A calculation supply the visible numbers, seed and hash. Unavailable-state requests are aborted; no data is substituted. The no-board model explicitly reports missing geometry rather than inventing reels. Source hashes wrap on mobile and the evidence actions remain usable. Desktop/mobile saved views and mobile recovery were visually inspected; final captures include the clearer recovery text. [Viewport audit](overview-evidence-viewport-audit.json) records the observations.

Remaining roadmap work includes visible-node rendering, legibility/accessibility and usability acceptance, along with required cleanup and full Core release verification. The generic weighted model’s no-geometry panel can be made denser during the legibility review. There is no release/deployment or complete roadmap claim.
