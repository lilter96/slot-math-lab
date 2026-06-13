// ═══════════════════════════════════════════════════════════════════════════
//  Normative constants from the PRD (CLAUDE.md v3.1).
//
//  This is the frontend mirror of backend/SlotMath.Core/SlotMathConstants.cs.
//  The two MUST be kept in lock-step — changing a value is a PRD change.
// ═══════════════════════════════════════════════════════════════════════════

/** The PRD revision these constants correspond to. */
export const PRD_VERSION = '3.1' as const;

/** D3 — PRNG & parallel determinism. */
export const PRNG = {
  /** Pinned core generator (seeded via SplitMix64). */
  algorithm: 'xoshiro256**',
  /** Fixed Monte-Carlo chunk size (rounds per stream chunk). */
  chunk: 65_536,
} as const;

/** D6 — Loop policy & caps. */
export const LOOP = {
  /** Default author-set iteration cap when none is supplied. */
  capDefault: 1_000,
  /** Hard system maximum iteration cap. */
  capMax: 100_000,
} as const;

/** D7 — Budgets, "MVP-class", and latency. */
export const BUDGET = {
  maxAuthoredNodes: 200,
  maxInlinedNodes: 2_000,
  maxExactStates: 1_000_000,
  maxExpressionOpsPerLeaf: 10_000,
  maxSubgraphNestingDepth: 16,
  lightEvalExactStateThreshold: 250_000,
  lightEvalExactTimeBudgetMs: 150,
  lightEvalSampledN: 20_000,
  lightEvalP95TargetMs: 300,
  /** Live-metric refresh debounce (ms). */
  canvasDebounceMs: 300,
  /** Max main-thread block on node/edge changes (ms). */
  canvasMaxBlockMs: 50,
} as const;

/** D8 — Statistical test policy. */
export const STATISTICS = {
  crossCheckSigma: 4.0,
  crossCheckMaxBreaches: 2,
  crossCheckHardSigma: 6.0,
  chiSquaredAlpha: 0.01,
  maxFalseFailProbability: 1e-3,
} as const;

/** D9 — Reference fixtures: pinned seeds. */
export const SEEDS = {
  /** Primary pinned seed (0xC0FFEE). */
  main: 0xc0ffee,
  /** Cross-check seed list (0x5EED0001 … 0x5EED0014 = 20 seeds). */
  crossCheck: [
    0x5eed0001, 0x5eed0002, 0x5eed0003, 0x5eed0004, 0x5eed0005, 0x5eed0006, 0x5eed0007,
    0x5eed0008, 0x5eed0009, 0x5eed000a, 0x5eed000b, 0x5eed000c, 0x5eed000d, 0x5eed000e,
    0x5eed000f, 0x5eed0010, 0x5eed0011, 0x5eed0012, 0x5eed0013, 0x5eed0014,
  ],
} as const;

/** D16 — Expression cost model. */
export const EXPRESSION = {
  /** Default per-leaf operation budget (compiler error if exceeded). */
  maxOps: 10_000,
} as const;

/** D18 — Plugin governance. */
export const PLUGINS = {
  /** Warn when plugin dependency exceeds this fraction of graph complexity. */
  coverageWarnThreshold: 0.3,
} as const;

/** G28 — Compliance lint band. */
export const LINT = {
  rtpBandMin: 0.85,
  rtpBandMax: 0.98,
} as const;

/** G29 — Rate limits / concurrency. */
export const LIMITS = {
  lightEvalRequestsPerSecond: 10,
  maxConcurrentRunsPerUser: 5,
} as const;

/** Provenance taxonomy (D5) — the kinds a metric value may carry. */
export const PROVENANCE_KINDS = [
  'Exact',
  'ExactInterval',
  'ExactWithMassLoss',
  'Sampled',
] as const;

export type ProvenanceKind = (typeof PROVENANCE_KINDS)[number];
