import type { WoodlandWhisperSerializedState } from '@tgslots/shared-contracts/states'
import type { FreeSpinsStatus } from '../../types.js'

export function deriveAwardedFreeSpins(
  previousState: WoodlandWhisperSerializedState,
  nextState: WoodlandWhisperSerializedState,
): number | null {
  const previousRemaining = previousState.freeSpins?.spinsRemaining ?? 0
  const nextRemaining = nextState.freeSpins?.spinsRemaining ?? 0

  return nextRemaining > previousRemaining ? nextRemaining - previousRemaining : null
}

export function deriveFreeSpinsStatus(
  state: WoodlandWhisperSerializedState,
  awarded: number | null = null,
): FreeSpinsStatus {
  const remaining = state.freeSpins?.spinsRemaining ?? 0

  return {
    active: remaining > 0,
    remaining,
    awarded: awarded && awarded > 0 ? awarded : null,
  }
}

export function formatFreeSpinsAwardedMessage(awarded: number): string {
  return `${awarded} FREE SPINS WON`
}
