import { GameUIState } from '../../../types.js'

export interface BuyBonusControlState {
  readonly uiState: GameUIState
  readonly isAutoSpin: boolean
  readonly freeSpinsRemaining: number
}

export function formatBuyBonusCostLabel(costMultiplier: number): string {
  return `${costMultiplier}x BET`
}

export function isBuyBonusEnabled(state: BuyBonusControlState): boolean {
  return state.uiState === GameUIState.IDLE && !state.isAutoSpin && state.freeSpinsRemaining <= 0
}
