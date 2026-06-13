namespace SlotMath.Core;

// ═══════════════════════════════════════════════════════════════════════════
//  SlotMathConstants — the single strongly-typed home for every normative
//  constant in the PRD (CLAUDE.md v3.1).  Changing a value here is a PRD
//  change: the Definitions D1–D25 are the contract.  The frontend mirror
//  lives at frontend/src/constants.ts and MUST be kept in lock-step.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Normative constants from the PRD (v3.1). Mirrored in
/// <c>frontend/src/constants.ts</c>; the two must agree.
/// </summary>
public static class SlotMathConstants
{
    /// <summary>The PRD revision these constants correspond to.</summary>
    public const string PrdVersion = "3.1";

    // ── D3 — PRNG &amp; parallel determinism ──────────────────────────────
    public static class Prng
    {
        /// <summary>Pinned core generator (seeded via SplitMix64).</summary>
        public const string Algorithm = "xoshiro256**";

        /// <summary>Fixed Monte-Carlo chunk size (rounds per stream chunk).</summary>
        public const int Chunk = 65_536;
    }

    // ── D6 — Loop policy &amp; caps ───────────────────────────────────────
    public static class Loop
    {
        /// <summary>Default author-set iteration cap when none is supplied.</summary>
        public const long CapDefault = 1_000;

        /// <summary>Hard system maximum iteration cap.</summary>
        public const long CapMax = 100_000;
    }

    // ── D7 — Budgets, "MVP-class", and latency ───────────────────────────
    public static class Budget
    {
        /// <summary>Max authored graph nodes (MVP-class).</summary>
        public const int MaxAuthoredNodes = 200;

        /// <summary>Max nodes after subgraph inlining.</summary>
        public const int MaxInlinedNodes = 2_000;

        /// <summary>Max exact-path explored recurrence states (MVP-class).</summary>
        public const long MaxExactStates = 1_000_000;

        /// <summary>Max expression operations per leaf evaluation (see D16).</summary>
        public const int MaxExpressionOpsPerLeaf = 10_000;

        /// <summary>Max subgraph nesting depth.</summary>
        public const int MaxSubgraphNestingDepth = 16;

        // ── /evaluate/light policy ───────────────────────────────────────

        /// <summary>Run exact only when the estimated state count is at or below this.</summary>
        public const long LightEvalExactStateThreshold = 250_000;

        /// <summary>Exact-attempt time budget for /evaluate/light (ms).</summary>
        public const int LightEvalExactTimeBudgetMs = 150;

        /// <summary>Sampled-fallback N for /evaluate/light.</summary>
        public const int LightEvalSampledN = 20_000;

        /// <summary>p95 latency target for /evaluate/light over fixtures (ms).</summary>
        public const int LightEvalP95TargetMs = 300;

        /// <summary>Live-metric refresh debounce (ms).</summary>
        public const int CanvasDebounceMs = 300;

        /// <summary>Max main-thread block on node/edge changes (ms).</summary>
        public const int CanvasMaxBlockMs = 50;
    }

    // ── D8 — Statistical test policy ─────────────────────────────────────
    public static class Statistics
    {
        /// <summary>Per-comparison cross-check tolerance, in standard errors.</summary>
        public const double CrossCheckSigma = 4.0;

        /// <summary>Max number of comparisons (out of 1,000) permitted to exceed 4σ.</summary>
        public const int CrossCheckMaxBreaches = 2;

        /// <summary>No comparison may exceed this many standard errors.</summary>
        public const double CrossCheckHardSigma = 6.0;

        /// <summary>Chi-squared significance level for the alias sampler harness.</summary>
        public const double ChiSquaredAlpha = 0.01;

        /// <summary>Target per-suite theoretical false-fail probability.</summary>
        public const double MaxFalseFailProbability = 1e-3;
    }

    // ── D9 — Reference fixtures: pinned seeds ────────────────────────────
    public static class Seeds
    {
        /// <summary>Primary pinned seed.</summary>
        public const ulong Main = 0xC0FFEE;

        /// <summary>Cross-check seed list (0x5EED0001 … 0x5EED0014 = 20 seeds).</summary>
        public static readonly ulong[] CrossCheck =
        [
            0x5EED0001, 0x5EED0002, 0x5EED0003, 0x5EED0004, 0x5EED0005,
            0x5EED0006, 0x5EED0007, 0x5EED0008, 0x5EED0009, 0x5EED000A,
            0x5EED000B, 0x5EED000C, 0x5EED000D, 0x5EED000E, 0x5EED000F,
            0x5EED0010, 0x5EED0011, 0x5EED0012, 0x5EED0013, 0x5EED0014,
        ];
    }

    // ── D16 — Expression cost model ──────────────────────────────────────
    public static class Expression
    {
        /// <summary>Default per-leaf operation budget (compiler error if exceeded).</summary>
        public const int MaxOps = 10_000;
    }

    // ── D18 — Plugin governance ──────────────────────────────────────────
    public static class Plugins
    {
        /// <summary>Warn when plugin dependency exceeds this fraction of graph complexity.</summary>
        public const double CoverageWarnThreshold = 0.30;
    }

    // ── G28 — Compliance lint band ───────────────────────────────────────
    public static class Lint
    {
        /// <summary>Default RTP band lower bound for the lint rule.</summary>
        public const double RtpBandMin = 0.85;

        /// <summary>Default RTP band upper bound for the lint rule.</summary>
        public const double RtpBandMax = 0.98;
    }

    // ── G29 — Rate limits / concurrency ──────────────────────────────────
    public static class Limits
    {
        /// <summary>Light-eval rate limit (requests per second per IP).</summary>
        public const int LightEvalRequestsPerSecond = 10;

        /// <summary>Max concurrent heavy runs per user.</summary>
        public const int MaxConcurrentRunsPerUser = 5;
    }
}
