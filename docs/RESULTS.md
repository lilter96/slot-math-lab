# Saved Results workspace

Results answers three questions: what was actually measured, how strong is the
evidence, and can another run reproduce it? It reads server records rather than
re-evaluating the current editor. The authenticated deployment uses the same
authorization boundary as simulations; credentials are never part of exports.

## Working with evidence

The archive spans browser sessions and supports model/ID/hash/seed search,
terminal-status filters, and stable cursor pagination. Its lightweight summaries
exclude graphs, histograms, and result JSON. A selected run has a shareable URL:
`/results?run=<id>&view=overview`. Optional `compare=<id>` preserves a baseline.

A missing browser-cached run does not hide the available server archive. An
explicit link to a missing run remains an error rather than opening another run.
The archive refreshes while matching jobs are active. Queries honor rate-limit
pauses; reference calculation retries a rejected HTTP 429 once and shows the
requested wait. Failed or timed-out accepted calculations are not repeated.

- **Overview:** observed RTP, authored target, explicitly calculated reference,
  interval versus tolerance, hit frequency, sample variance, precision planning,
  and checks on pinned input, round coverage, and result consistency.
- **Distribution:** original payout bins, logarithmic or linear counts,
  cumulative probability, bounded percentile estimates, and tail-frequency
  bounds. Percentiles within a bin are never interpolated into invented values.
- **Compare:** side-by-side metrics and fingerprint-aware interpretation. Equal
  seeds, lengths, and stream schemes compare every aggregate statistic and bin
  exactly, allowing different worker counts. Equal seeds with different lengths
  are correlated; different inputs do not receive an agreement verdict.
- **Reproducibility:** pinned document version, full input and model hashes,
  seed, stream scheme, worker count, terminal result, graph, and portable exports.

Replay and new-seed runs explicitly pass the original `configVersion` to the
server. They do not save or serialize the editor. A newer document version cannot
silently change replay. Config deletion returns HTTP 409 while any saved run
references it. Pinning and deletion share the config-store gate to close the race.
Opening a saved graph or editable proof preserves the earlier editor draft,
including open subgraphs; Build offers **Restore previous editor draft**. The
backup is also retained in browser storage when storage is available.

The app shell continues to own the active WebSocket subscription while Results
is open. Selected active-run observations come from that owner. Other selected
active records use bounded, cancellable HTTP queries; terminal records stop
polling. Epoch/revision rules reject regressions and reconcile interrupted runs.
The Results module and stylesheet load separately from the initial editor bundle.

## Mathematical interpretation

Authored RTP is design intent. It is distinct from both an observed estimate and
a calculated mathematical reference. Reference calculation is explicit:

- Supported Dog House configurations produce a second editable constructor AST
  that computes the rational expectation and base/scatter/free-spin contributions
  under checked reel, payout, multiplier, loop, and nonbinding-cap assumptions.
  It is labeled **ExactExpectation**, not an exact payout distribution.
- Other models attempt rational full enumeration with a 10,000-branch budget.
  Complete enumeration is **ExactDistribution**; pruning can produce a conservative
  **ExactInterval**. Exceeding the budget is explicitly unavailable.
- Engine calculations are not independent external certification. The existing
  Python Fraction oracle separately checks the standard Dog House fixture; it is
  not falsely attached to arbitrary saved or edited models as their certificate.

The approximate RTP interval is `mean ± 1.96 × sample standard error`.
Acceptance requires verified input, internally consistent evidence, every
requested round completed, at least two rounds, positive observed variance,
and a positive tolerance. The entire interval must fit within the target band.
Partial, failed, unverified, and zero-variance samples do not receive acceptance.
Normal approximation has no finite-sample coverage guarantee, especially with
rare high payouts, and is not an anytime-valid confidence sequence. Hit frequency
uses a Wilson interval. The zero-tail bound is a one-sided binomial bound under
independent fixed-count sampling; it is withheld for partial runs.

Precision planning uses the observed variance:
`ceil((1.96 × sample SD / requested half-width)²)`. It estimates interval width,
not the probability of passing an acceptance check. Launches respect the existing
10,000,000-round limit. Separate same-seed runs must not be pooled. Independent
seed comparisons use `delta ± 1.96 × hypot(SE₁, SE₂)`, with the independence and
normal-approximation assumptions stated. Results never pools runs automatically.

Maximum observed is distinct from the theoretical cap. The sampler clipping
counter does not instrument cap enforcement upstream in the compiled graph and
must not be interpreted as a count of all cap events.

## Contracts and exports

`GET /api/runs?limit=20&cursor=...&search=...&status=...` returns `RunPage`.
`GET /api/runs/{id}/evidence` returns the saved run, model descriptor, original
config serialized with the Core AST contract, recomputed input hash, and
`inputVerified`. Missing legacy inputs remain explicitly unverified. The model
fingerprint excludes only the root document ID; names, annotations, and all other
serialized fields participate. It is conservative, not a general semantic
equivalence proof. The full config hash remains the provenance identifier.

Evidence JSON (`slotmath.results.v1`) includes the pinned graph, observations,
histogram, reference/proof when calculated, assessment, and optional comparison.
CSV quotes values and protects authored strings against spreadsheet formulas.
Printable HTML escapes authored content and states the statistical limitations.
The pinned graph can also be downloaded separately. Authentication is required
to open shared server links; downloaded evidence is standalone.

## Verification

Statistical unit tests use independently derived coin moments and bin counts.
API tests cover pagination, malformed filters, stable cursors under insertion,
lightweight summaries, executable AST round trips, version pinning, legacy gaps,
deletion guards, and concurrent pin/deletion. Browser tests cover archive discovery,
references, exports, editor restoration, seeded replay across worker counts,
different/correlated runs, the 98% constructor proof, accessibility keyboard tabs,
all four sections on mobile, errors, zero variance, and active WebSocket navigation.
These run in the existing unit/API/E2E CI jobs.

Authenticated Caddy/nginx deployment verification is reproducible with:

```sh
RESULTS_BASE_URL=https://localhost:8543 \
PLAYWRIGHT_CHROMIUM_EXECUTABLE=/path/to/chromium \
node scripts/verification/results-production.mjs
```

The script uses `RESULTS_USER` (default `operator`) and
`RESULTS_PASSWORD_FILE` (default ignored production credential file). Set
`RESULTS_RUN_ID` to another completed standard 100,000-round, seed-42 Dog House
run; otherwise it uses the retained run recorded by realtime verification. It
launches a replay and records the real checks in
`docs/verification/results-production.json`, with desktop/mobile screenshots.
It does not restart or kill the server. Deployment persistence remains the
documented encrypted, single-writer snapshot architecture.

## Pinned verification workspace

Selected saved runs expose parameter/policy experiments, finite verified sampling designs, resource-stop references and external input manifests. Early-stopped sessions can be complete with fewer paid rounds than planned slots; ordinary round confidence intervals remain withheld. See [scope and assumptions](VERIFICATION_EXPERIMENTS.md).
