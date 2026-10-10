# Slot Math Lab: operating the audited build

The supported deployment is one API instance for one studio/operator, with HTTPS,
encrypted config/run snapshots and PostgreSQL/Redis on a private container network.
This is not a multi-tenant SaaS deployment or a certification claim.

## Start

```sh
python3 scripts/init-production.py
# Set DOMAIN in production-secrets/deployment.env to your DNS hostname.
docker compose --env-file production-secrets/deployment.env -f compose.production.yml up -d --build
```

Credentials are created with restrictive file permissions. The username is `operator`;
the password is in `production-secrets/operator-password.txt`. Keep that directory
private. Never regenerate the storage key for existing data. Back up both credentials
and the `mathdata` volume. For `DOMAIN=localhost`, Caddy uses its local certificate
issuer; install/trust the generated CA before browser use. A public domain obtains
its certificate automatically when DNS and ports 80/443 reach the ingress.

Production startup requires a unique JWT secret, a PBKDF2 password hash and a
32-byte encryption key. Anonymous API/SignalR access is rejected. The development
login, metadata-only plugin registration and Hangfire dashboard are confined to
local/test environments. Untrusted plugins are disabled in production, including
plugin references inside library subgraphs. The current thread-based PluginSandbox
is not a security boundary; process isolation remains required before enabling
user-supplied plugin code.

The config/run API persists AES-GCM encrypted atomic snapshots. Finished results
survive restart. Interrupted runs become failed, retain their seed and can be
resubmitted; the memory-backed job queue does not automatically resume them.
Runs pin the exact config version used when queued. This storage path supports one
API writer. Do not scale it to multiple replicas. Legacy PostgreSQL config/cache
endpoints are available only locally; production uses the encrypted snapshot path.

## Feature flags

The API reads the `Features` configuration section once at startup; changing a flag
requires a restart. A disabled flag is not a soft hide — its endpoints are never
registered and answer 404, and plugins are not loaded at all.

| Flag | Production default | Gates |
|------|--------------------|-------|
| `Features:Ai` | off | `POST /api/ai/generate-graph`, `/api/ai/lint`, `/api/ai/explain`; the AI generate modal and the topbar AI assist button |
| `Features:AutoTune` | off | `POST /api/ai/auto-tune`; the AutoTune panel |
| `Features:Plugins` | off | `/api/plugins`; the plugin manager |
| `Features:Play` | on | `POST /api/play/round`; the Play navigation entry |

`appsettings.Production.json` ships these defaults. Environment variables override
them, including the string form (`Features__Ai=false`); outside Production
(Development, CI, Testing) every flag defaults to enabled. An unparsable value
fails startup and names the offending key.

`GET /api/features` reports the effective flags without a login:

```json
{ "ai": false, "autoTune": false, "plugins": false, "play": true }
```

The UI hides disabled menu items and panels according to this endpoint; if the
request fails, everything that can be disabled stays hidden. Flags never bypass
the production secret checks — startup still requires a unique `JWT:Secret`,
`Auth:User`, `Auth:PasswordHash` and `Storage:Key` even when plugins or AI are
enabled.

## Verify a model

Open Build, load REF-A or one of the seven catalog examples, inspect the graph, then
open Simulate and start a run. REF-A has exact payout PMF `0:1/2, 1:3/8, 3:1/8`,
RTP `3/4`, hit frequency `1/2`, variance `15/16`. Seed is explicit and returned by
the API. Export saves backend-compatible JSON and a SHA-256 fingerprint of the
recursively canonicalized authored JSON. That fingerprint is not the hash of the
compiler's expanded graph. JSON import restores tables, expressions and graph edges.
Browser drafts are kept locally; use server save and JSON export for backups.

The editor parses arithmetic, comparisons, boolean operators, state references,
parentheses and ternary expressions into AST. Unsupported syntax fails rather than
becoming a missing reference. Import a typed backend AST for bounded folds/maps and
other advanced expressions. Catalog examples show the supported mechanic atoms;
cascade means positional remove/refill, not gravity. Host-game bonus orchestration
must still be authored in the graph.

Leading wilds substitute for the first ordinary symbol. An all-wild line has no
ordinary-symbol payout. Cluster wild ownership defaults to exclusive, row-major
assignment; `MapNode.shareWildAcrossSymbols=true` permits reuse across symbol kinds.
Neither rule should be assumed to match a studio's game without checking its spec.

Per-label Emit attribution with a binding round cap requires an explicit attribution
policy and is rejected by that core API rather than returning an inconsistent breakdown.

WinCap applies once to the full round in both exact and sampled execution. Payouts
are non-negative. Pruned RTP bounds use the declared round cap and unnormalized
known probability mass. Without a bound, provenance is `ExactWithMassLoss`.
Variance/max-win/histogram values with lost mass carry non-regulatory mass-loss
provenance; they do not advertise certified interval bounds. Arbitrary game models
are not independently certified by the reference scenarios.

Light evaluations have request limits and a concurrency limit. Heavy jobs have
bounded sample sizes, two concurrent jobs, 1–4 sampling workers per job and a
five-minute cancellation deadline.
Protect the deployment host, use HTTPS ingress, monitor disk space, and back up
volumes. The production compose file applies CPU/memory limits to the API.

## Simulation dashboard

Simulate runs the complete constructor graph, saving nested subgraph edits before
pinning the config version. RTP, normal-approximation 95% confidence intervals,
hit frequency, volatility, observed maximum and payout counts stream over SignalR
WebSockets during logical PRNG chunks. The convergence, payout, throughput and
precision charts use these server observations. Fewer than two completed rounds have no
reported confidence interval or sample volatility. Display updates do not change
the fixed 65,536-round PRNG stream partition or deterministic final reduction.

Reconnects subscribe again and recover the current sequenced snapshot; HTTP polls
also recover progress when WebSockets are unavailable. Run identity is saved
immediately so an early reload does not lose a job. Navigation and reload preserve
the run, while cancellation waits for the worker's terminal status and retains
actual partial counts. The dashboard exports the pinned graph, seed, stream
scheme, config hash, result and observed convergence history as JSON.

A target is separate from a verified reference. For the authored Dog House model,
the exact expectation is calculated by the editable AST proof through the generic
graph endpoint. Other models show an exact reference only when the backend
actually returns Exact provenance. Sampled preflight estimates are not exact
references. Full payout distributions still have the documented exact budget.

Browser checks cover live mid-chunk events, nested graph execution, reload,
fast completion, cancellation, evidence export, blocked socket recovery and mobile
scrolling. Production HTTPS/WebSocket evidence and screenshots are saved under
`docs/verification/simulation-*`.

Pure graph/AST models now use a compiled sampled state plan, with an independent
canonical program retained for exact calculation and verification. The API reuses
eligible plans by graph hash; mutable execution state belongs to each worker.
See [performance measurements and correctness gates](PERFORMANCE.md).

## Local verification

```sh
docker compose up -d postgres redis
export ASPNETCORE_ENVIRONMENT=CI
export ConnectionStrings__DefaultConnection='Host=localhost;Port=5433;Database=slotmath;Username=slotmath;Password=slotmath'
dotnet test backend/SlotMathLab.slnx
npm ci --prefix frontend --legacy-peer-deps
npm run build --prefix frontend
npm run lint --prefix frontend
cd frontend
npx playwright install chromium
npm run test:e2e
```

Use a dedicated test database without unrelated tables. EF EnsureCreated is for
fresh installations; schema migration/baselining of a pre-existing unrelated database
is not automatic. CI provisions PostgreSQL for the browser suite and starts a real
API, so UI workflow tests run without mocked calculation responses.

## Experiment persistence

Experiment manifests use the existing encrypted single-writer snapshots. Unfinished experiments become interrupted after restart; retained child results are kept and replay is explicit. Admission/cancellation and mathematical scope are documented in [native experiments](VERIFICATION_EXPERIMENTS.md).
