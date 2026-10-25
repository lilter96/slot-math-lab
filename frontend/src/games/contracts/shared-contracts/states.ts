export interface WWFreeSpinSerialized {
  triggeringMultiplier: number
  totalWin: number
  spinsRemaining: number
}

export interface WWPickBonusSerialized {
  board: number[]
  pickSequence: number[]
  currentPickIndex: number
  userPicks: number[]
  revealedValues: number[]
  winValue: number
  triggeringMultiplier: number
}

export interface WoodlandWhisperSerializedState {
  lastGrid: number[][] | null
  freeSpins: WWFreeSpinSerialized | null
  pickBonus: WWPickBonusSerialized | null
}

export interface ADFreeSpinSerialized {
  triggeringMultiplier: number
  totalWin: number
  spinsRemaining: number
}

export interface AncientDragonSerializedState {
  freeSpins: ADFreeSpinSerialized | null
}

export interface LMFreeSpinSerialized {
  triggeringMultiplier: number
  spinsRemaining: number
  totalWin: number
  armedReels: number[]
  multiplierSum: number
}

export interface LeMilitareSerializedState {
  lastGrid: number[][] | null
  freeSpins: LMFreeSpinSerialized | null
  /** Selected volatility mode for the session. */
  mode?: 'recon' | 'assault' | 'siege'
  /** Cumulative win for the current round (base + free spins), used for max-win cap. */
  roundWin: number
}
