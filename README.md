# Slot Math Lab

A .NET 10 / React 19 engineering prototype for composing slot-game mathematics on a visual graph and comparing exact probability calculations with Monte Carlo simulation.

**Author:** Terentiy Gatsukov · **Status:** research / portfolio prototype, not a certified gambling engine.

## Engineering highlights

- **Two interpreters, one program:** exact rational distributions and a stack-safe sampled interpreter, with explicit calculation provenance and budgets.
- **Reproducible simulation:** xoshiro256** / SplitMix64 and pinned stream splitting; seeded cross-checks and negative controls.
- **Graph compiler and extensibility:** reusable mechanics, typed expressions, evaluator/transform contracts, and plugin experiments.
- **Application stack:** ASP.NET Core, PostgreSQL/EF Core, optional Redis caching, Hangfire jobs, SignalR progress, and OpenTelemetry.
- **Visual editor:** React 19, TypeScript, React Flow, Zustand, and generated API/schema types.

Start with [the core](backend/SlotMath.Core), [cross-check tests](backend/SlotMath.Core.Tests/Mechanics/CrossCheck), and [the build contract](docs/PRD.md). The PRD describes intended behavior and acceptance goals; it is not proof that every goal is complete.

## Scope and verification

This is an experimental engineering showcase. It has no gambling certification, production-readiness claim, or proven plugin security boundary. The bundled database credentials and default JWT settings are development defaults; replace them before any hosted deployment. Plugin support must be treated as trusted-code experimentation.

Current local, remote CI and authenticated deployment evidence is recorded in the [verification ledger](docs/verification/METRICS_IMPLEMENTATION.md), including retry-assisted passes and remaining metric prerequisites. The [verification strategy](docs/TESTING.md) keeps all Core/API and frontend logic checks on every change, selects 14 critical browser workflows for ordinary CI, and retains the complete browser regression for nightly/manual runs and release verification.

## Development workflow

Development includes AI-assisted implementation. The versioned PRD, deterministic test fixtures, and reviewable commits expose the constraints and verification approach. Architecture, acceptance decisions, and final review remain the author's responsibility.

## Local bootstrap

### One-command start

```bash
# Backend
cd backend && dotnet build && cd ..

# Frontend
cd frontend && npm ci && cd ..
```

### Backend

Requires [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

```bash
cd backend
dotnet restore
dotnet build
dotnet format --verify-no-changes
dotnet test
```

### Frontend

Requires [Node.js 22](https://nodejs.org/).

```bash
cd frontend
npm ci
npm run build
npx tsc --noEmit
npm run lint
npm run dev        # start dev server
```

### Engine benchmarks

```bash
cd backend
dotnet run -c Release --project SlotMath.Benchmarks
```

Stopwatch-based throughput scenarios for both interpreters (Monte Carlo
loops, graph-compiled programs, reel draws, exact free-spin retrigger
memoisation).

### Determinism & PRNG (PRD v3.1, D3)

The pinned PRNG is **xoshiro256\*\*** seeded via **SplitMix64**
(`backend/SlotMath.Core/Random/SeededRandom.cs`). This is the contract and
must not change without a PRD edit.

- **Seeding.** A 64-bit *expansion seed* is run through SplitMix64 four times
  to fill the four 64-bit xoshiro256\*\* state words (constants
  `0x9E3779B97F4A7C15`, `0xBF58476D1CE4E5B9`, `0x94D049BB133111EB`). The plain
  constructor uses `(ulong)seed` as the expansion seed.
- **Stream splitting.** `SeededRandom.ForStream(masterSeed, i)` seeds stream
  `i` from `SplitMix64(masterSeed, i) = Mix64(masterSeed + (i+1)·GAMMA)` — an
  O(1), pinned mapping, so each parallel Monte-Carlo chunk gets an independent,
  reproducible sub-stream and a given `(seed, n)` yields bit-identical
  statistics regardless of thread count (chunk size `CHUNK = 65,536`).
- **Pinned seeds.** `SEED_MAIN = 0xC0FFEE`; cross-check list
  `0x5EED0001 … 0x5EED0014`. All randomized tests use these (D8), and each
  statistical harness ships a negative control that must fail on corrupted
  input.

All normative constants live in `backend/SlotMath.Core/SlotMathConstants.cs`
(mirrored in `frontend/src/constants.ts`). Changing a value is a PRD change.

### Project structure

```
slot-math-lab/
├── backend/
│   ├── SlotMath.Core/         # Pure math kernel
│   ├── SlotMath.Api/          # Web API host
│   ├── SlotMath.Core.Tests/   # xUnit tests
│   ├── SlotMath.Benchmarks/   # Engine throughput benchmarks
│   └── SlotMathLab.slnx
├── frontend/                  # React 19 + TypeScript + Vite
├── docs/
│   └── PRD.md
└── .github/workflows/
    └── ci.yml
```
