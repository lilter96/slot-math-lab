import { signal } from './signal.js'
import type { Unsubscribe } from './signal.js'

export type SpinSpeedMode = 'normal' | 'fast' | 'turbo'

export interface SpinSpeedProfile {
  readonly mode: SpinSpeedMode
  readonly reelSpinMs: number
  readonly reelStartStaggerMs: number
  readonly reelStopStaggerMs: number
  readonly reelVelocityMultiplier: number
  readonly reelSettleDurationMs: number
  readonly lineHighlightBudgetMs: number
  readonly lineHighlightMinMs: number
  readonly lineHighlightMaxMs: number
  readonly scatterHighlightMs: number
  readonly overlayDurationMultiplier: number
  readonly overlayMinMs: number
  readonly overlayFadeInMs: number
  readonly overlayFadeOutMs: number
  readonly pickRevealMs: number
  readonly pickMatchPauseMs: number
  readonly pickMissPauseMs: number
  readonly autoSpinDelayMs: number
}

export interface SpinSpeedState {
  readonly mode: SpinSpeedMode
  readonly fastEnabled: boolean
  readonly turboEnabled: boolean
  readonly profile: SpinSpeedProfile
}

const SPIN_SPEED_PROFILES: Record<SpinSpeedMode, SpinSpeedProfile> = {
  normal: {
    mode: 'normal',
    reelSpinMs: 1000,
    reelStartStaggerMs: 50,
    reelStopStaggerMs: 150,
    reelVelocityMultiplier: 1,
    reelSettleDurationMs: 500,
    lineHighlightBudgetMs: 2000,
    lineHighlightMinMs: 700,
    lineHighlightMaxMs: 1500,
    scatterHighlightMs: 2000,
    overlayDurationMultiplier: 1,
    overlayMinMs: 900,
    overlayFadeInMs: 250,
    overlayFadeOutMs: 300,
    pickRevealMs: 400,
    pickMatchPauseMs: 1500,
    pickMissPauseMs: 400,
    autoSpinDelayMs: 500,
  },
  fast: {
    mode: 'fast',
    reelSpinMs: 550,
    reelStartStaggerMs: 25,
    reelStopStaggerMs: 80,
    reelVelocityMultiplier: 1.7,
    reelSettleDurationMs: 280,
    lineHighlightBudgetMs: 1100,
    lineHighlightMinMs: 320,
    lineHighlightMaxMs: 700,
    scatterHighlightMs: 900,
    overlayDurationMultiplier: 0.55,
    overlayMinMs: 500,
    overlayFadeInMs: 180,
    overlayFadeOutMs: 220,
    pickRevealMs: 220,
    pickMatchPauseMs: 700,
    pickMissPauseMs: 180,
    autoSpinDelayMs: 180,
  },
  turbo: {
    mode: 'turbo',
    reelSpinMs: 180,
    reelStartStaggerMs: 0,
    reelStopStaggerMs: 30,
    reelVelocityMultiplier: 2.8,
    reelSettleDurationMs: 180,
    lineHighlightBudgetMs: 420,
    lineHighlightMinMs: 120,
    lineHighlightMaxMs: 220,
    scatterHighlightMs: 350,
    overlayDurationMultiplier: 0.22,
    overlayMinMs: 220,
    overlayFadeInMs: 90,
    overlayFadeOutMs: 120,
    pickRevealMs: 120,
    pickMatchPauseMs: 260,
    pickMissPauseMs: 90,
    autoSpinDelayMs: 40,
  },
}

export function getSpinSpeedProfile(mode: SpinSpeedMode): SpinSpeedProfile {
  return SPIN_SPEED_PROFILES[mode]
}

export class SpinSpeedController {
  private readonly _mode = signal<SpinSpeedMode>('normal')

  get mode(): SpinSpeedMode {
    return this._mode.value
  }

  get state(): SpinSpeedState {
    return this._buildState(this._mode.value)
  }

  get profile(): SpinSpeedProfile {
    return getSpinSpeedProfile(this._mode.value)
  }

  subscribe(fn: (state: SpinSpeedState) => void): Unsubscribe {
    return this._mode.subscribe((mode) => {
      fn(this._buildState(mode))
    })
  }

  setMode(mode: SpinSpeedMode): void {
    this._mode.set(mode)
  }

  toggleFast(): void {
    this._mode.set(this._mode.value === 'fast' ? 'normal' : 'fast')
  }

  toggleTurbo(): void {
    this._mode.set(this._mode.value === 'turbo' ? 'normal' : 'turbo')
  }

  private _buildState(mode: SpinSpeedMode): SpinSpeedState {
    return {
      mode,
      fastEnabled: mode === 'fast',
      turboEnabled: mode === 'turbo',
      profile: getSpinSpeedProfile(mode),
    }
  }
}
