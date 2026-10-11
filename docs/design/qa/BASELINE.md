# Baseline evidence

Base: b81e71c, fetched origin/main. Worktree: precision-workbench-redesign.

Frontend build and lint pass. 114 unit tests pass. Critical browser checks run against dedicated API port 5166 / frontend 4186 / Postgres 5546. First default-database .NET run failed API tests because localhost:5432 hosts a different application; rerun against dedicated Postgres passes all 135 API tests. Core suite continues; duplicate unconfigured run was terminated to avoid competing workloads.

Actual screenshots (local, excluded from git per repository convention): before-build-1440.png, before-build-1280.png, before-build-1024.png, before-build-390.png. Desktop review: 40 subgraph chips, default round inspector, ~73% graph zoom, collapsed/overlapping toolbar, tiny supporting labels, Exact badges beside absent values. Brief described 21 definitions; current source has 40 after recent atomic graph improvements.

No performance/usability success target is claimed as a measurement. No backend, seeded RNG, compiled graph or fixture changes are proposed.

Cleanup limitation: Rider exists. Initial format command rejected a concurrent instance; isolated Rider config allowed CLI invocation but source formatting failed with `solution can't be null`. Full Cleanup through the pinned JetBrains CLI was attempted with frontend-only include paths; frontend is outside the .NET solution, and Roslyn worker also reports missing System.Composition.AttributedModel. These are incomplete cleanup passes. No backend source was changed. All touched frontend files require owner-side Rider Reformat Code and Built-in: Full Cleanup before release; scoped lint and build are still required locally.
