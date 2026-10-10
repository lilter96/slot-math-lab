/**
 * Client-side validation limits (G29).
 * These mirror the backend ValidationLimits to catch issues before API calls.
 */

export const LIMITS = {
  maxNodes: 100,
  maxEdges: 200,
  maxSpinBudget: 10_000_000,
  /** Largest sampled run, in complete rounds (backend MaxRunRounds). */
  maxRunRounds: 10_000_000_000,
  /** Most sampling workers one run may use (backend MaxRunWorkers). */
  maxRunWorkers: 8,
  lightEvalMaxSamples: 50_000,
  lightEvalMaxBranches: 100_000,
  maxExpressionLength: 1024,
} as const;

/** The worker counts a run may be launched with. */
export const RUN_WORKERS = Array.from({ length: LIMITS.maxRunWorkers }, (_, i) => i + 1);

/** Rounds between progress snapshots. A worker also reports after 250 ms
 * without one, so a long run asks for one snapshot per PRNG stream. */
export const progressBatchSize = (rounds: number) => rounds > 10_000_000 ? 65_536 : 1000;

export interface LimitError {
  field: string;
  message: string;
}

/** Check graph size against limits. Returns null if valid. */
export function validateGraphSize(
  nodeCount: number,
  edgeCount: number,
): LimitError | null {
  if (nodeCount > LIMITS.maxNodes) {
    return {
      field: 'nodes',
      message: `Graph has ${nodeCount} nodes (max ${LIMITS.maxNodes}). Simplify the graph to continue.`,
    };
  }
  if (edgeCount > LIMITS.maxEdges) {
    return {
      field: 'edges',
      message: `Graph has ${edgeCount} edges (max ${LIMITS.maxEdges}). Reduce connections.`,
    };
  }
  return null;
}

/** Check simulation budget. Returns null if valid. */
export function validateSimBudget(
  sampleSize: number,
  maxBranches?: number,
): LimitError | null {
  if (sampleSize > LIMITS.lightEvalMaxSamples) {
    return {
      field: 'sampleSize',
      message: `Sample size ${sampleSize.toLocaleString()} exceeds light eval limit of ${LIMITS.lightEvalMaxSamples.toLocaleString()}. Use a full run.`,
    };
  }
  if (maxBranches && maxBranches > LIMITS.lightEvalMaxBranches) {
    return {
      field: 'maxBranches',
      message: `Branch budget ${maxBranches.toLocaleString()} exceeds limit of ${LIMITS.lightEvalMaxBranches.toLocaleString()}.`,
    };
  }
  return null;
}

/** Check expression complexity. Returns null if valid. */
export function validateExpression(expr: string): LimitError | null {
  if (expr.length > LIMITS.maxExpressionLength) {
    return {
      field: 'expression',
      message: `Expression is ${expr.length} characters (max ${LIMITS.maxExpressionLength}). Shorten to continue.`,
    };
  }
  return null;
}

/** Check plugin approval status. */
export function isPluginApproved(pluginId: string): boolean {
  const APPROVED = ['megaways-evaluator', 'exotic-evaluator', 'custom-cascade'];
  return APPROVED.includes(pluginId);
}

export function pluginApprovalError(pluginId: string): string | null {
  return isPluginApproved(pluginId)
    ? null
    : `Plugin '${pluginId}' is not in the approved list. Only trusted plugins can execute.`;
}
