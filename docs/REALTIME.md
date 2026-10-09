# Durable simulation connections

The app shell owns one `RunConnection` for the selected run. Opening Build,
Play, Results, or Simulate preserves that connection and its observations.
Reloading restores the browser checkpoint, then reconciles it with the server.

## Protocol and recovery

Every `ProgressUpdate` is a cumulative snapshot: status, completed round count,
payout statistics, and the complete observed histogram. Frames may be coalesced,
duplicated, or delivered after a newer HTTP response. The client validates the
payload, run identity, pinned input metadata, stream epoch, revision, and histogram
count before changing the dashboard. Lower revisions/counts and status regressions
are ignored. Terminal state cannot become running again.

`SubscribeToRun` acknowledges group membership with a complete `RunResponse`.
An open socket alone does not mean the subscription is live. A new broadcast can
precede its acknowledgement; the newer metrics are retained. `GetRunSnapshot`
provides an application heartbeat and an authoritative resync. A terminal snapshot
includes the persisted result, so missing the final broadcast does not lose results.
Cancellation also returns a complete versioned snapshot, avoiding a race that
previously overwrote newer metrics with captured pre-cancellation state.

One supervisor owns all retries, polling, deadlines, and lifecycle listeners:

| Operation | Policy |
| --- | --- |
| Socket handshake, subscription, application acknowledgement | 8-second client deadline |
| Application heartbeat | Every 10 seconds, single flight |
| SignalR keepalive / dead connection timeout | 5 / 20 seconds |
| HTTP audit of a live subscription | Every 15 seconds |
| HTTP recovery while disconnected | Initially every 2.5 seconds; failures back off to 30 seconds |
| Socket reconnect | Jittered exponential backoff, capped at 30 seconds, without a retry-count ceiling |
| Rate limiting | Respect `Retry-After` for HTTP and socket attempts |
| Browser offline | Stop transport/retry traffic; retain observations |
| Online, visible tab, page restoration | Immediate resync and connection/liveness check |
| Authentication expired | Stop retries, show sign-in, retain run identity; login restores the existing run |
| Run unavailable | Preserve evidence/seed, allow a new run; do not fabricate a failed server result |
| Terminal result received | Close socket, cancel timers/HTTP, retain one history entry |

The dashboard displays the last server confirmation, acknowledgement latency,
reconnection count, and next retry. Browser checkpoints are saved at most twice
per second during progress and flushed on terminal state, network/authentication
loss, tab suspension, and page departure.

## Worker and persistence boundaries

Each run has a bounded, one-slot delivery channel. The latest cumulative snapshot
replaces unsent progress; one sender serializes delivery and normally spaces updates
by 80 ms. Each send has a 2-second deadline. Cleanup waits for the in-flight send
and the last queued snapshot, rather than retaining a worker indefinitely for a
slow client. Delivery failures are logged; terminal state is persisted first and
remains available over HTTP or on subscription. Cancellation resources are always
released in `finally`. Application shutdown cancels active execution.

OpenTelemetry meter `SlotMath.Api.Realtime` exports send duration and send failure
counts, tagged by delivered/error/timeout outcome. These labels contain no run IDs.
The nginx hub route disables buffering and configures upgrade and connection
timeouts; periodic keepalives maintain the proxied WSS connection.

Run IDs are UUIDs and are never reused after a restart. Every state transition,
including cancellation and a failure before the first round, advances its revision.
Production persists encrypted progress checkpoints on progress updates at a
5-second interval, as well as creation and terminal results. After an abrupt API
restart, an unfinished run becomes `failed` with `RUN_INTERRUPTED`, a new epoch,
and the last durable prefix. The client accepts that explicit checkpoint even if
its revision/count is lower than the last delivered observation, trims chart points
beyond it, and explains how to replay using the pinned seed.

This deployment supports **one API writer**. It does not resume unfinished jobs or
claim exactly-once event delivery. Five seconds is the checkpoint interval while
progress is arriving, not a guarantee against an unbounded pause in computation or
storage. Horizontal scaling requires shared durable run/job storage and a SignalR
backplane; encrypted local snapshots and in-memory Hangfire are insufficient.

## Verification

Run the deterministic transport tests and the real-browser workflow tests:

```sh
cd frontend
npm run test:unit
npm run test:e2e
```

Transport tests use a controllable clock and adapters to exercise retry ceilings,
subscription rejection, blackholed acknowledgements, HTTP adapters that ignore
abort, rate limits, authentication suspension, cleanup, stale packets, and crash
epoch changes. API tests cover revision transitions, durable checkpoint recovery,
terminal snapshot acknowledgement, coalescing, and noncooperative send deadlines.
Browser tests cut established sockets repeatedly, suppress server frames, change
network availability, inject malformed/stale frames, navigate, reload, and recover
terminal results.

For a disposable authenticated deployment, this additional check deliberately
**kills and restarts the specified API container**:

```sh
REALTIME_BASE_URL=https://localhost:8543 \
REALTIME_CRASH_CONTAINER=slotmath-prod-audit-api-1 \
PLAYWRIGHT_CHROMIUM_EXECUTABLE=/path/to/chrome \
node scripts/verification/realtime-production.mjs
```

Optional `REALTIME_USER` and `REALTIME_PASSWORD_FILE` select credentials; the
password is never printed. Default username is `operator`, password file is
`production-secrets/operator-password.txt`. The script writes
`docs/verification/realtime-production.json` and a crash recovery screenshot.

Verified in this workspace: 13 transport tests, 63 API tests, and 34 browser tests
passed, plus frontend build/lint. The authenticated Caddy → nginx → API check
recovered a silent socket, repeated disconnection, expired session, and hard API
crash. The durable checkpoint was 1,399 ms behind the pre-crash observation.
The subsequent 100,000-round run completed in 4,190 ms and retained the same
seeded RTP (`0.9967189999999915`, seed 42, two workers); its complete terminal result
arrived inline over WSS. This observed RTP is distinct from the authored 98% target
and exact expectation. See [recorded evidence](verification/realtime-production.json).
