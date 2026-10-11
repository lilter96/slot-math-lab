import type { LeMilitareResult } from '@tgslots/le-militare'

/**
 * Returns the carry-over multiplier that was accumulated in earlier spins of
 * the same free-spin session, before this spin added its own contribution.
 *
 * For BASE and BUY results there is no session carry-over, so this returns 0.
 */
export function deriveFreeCarryOverMultiplier(result: LeMilitareResult): number {
  if (result.type !== 'FREE') return 0
  if (!result.state) return 0
  const carry = result.state.sessionMultiplierSum - result.multiplierSum
  return Number.isFinite(carry) ? Math.max(0, carry) : 0
}
