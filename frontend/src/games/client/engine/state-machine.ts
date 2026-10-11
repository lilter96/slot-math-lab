import { GameUIState } from '../types'

export type StateCallback = (state: GameUIState) => void

export class GameStateMachine {
  private _currentState: GameUIState = GameUIState.IDLE
  private _listeners: Set<StateCallback> = new Set()

  get state(): GameUIState {
    return this._currentState
  }

  constructor() {}

  addListener(callback: StateCallback): () => void {
    this._listeners.add(callback)
    return () => this._listeners.delete(callback)
  }

  transitionTo(nextState: GameUIState): void {
    if (this._currentState === nextState) return

    if (!this.isValidTransition(this._currentState, nextState)) {
      throw new Error(`Invalid transition from ${this._currentState} to ${nextState}`)
    }

    this._currentState = nextState
    this.notify()
  }

  private isValidTransition(from: GameUIState, to: GameUIState): boolean {
    switch (from) {
      case GameUIState.IDLE:
        return to === GameUIState.SPINNING || to === GameUIState.FEATURE_TRANSITION
      case GameUIState.SPINNING:
        return to === GameUIState.STOPPING
      case GameUIState.STOPPING:
        return (
          to === GameUIState.WIN_SHOW ||
          to === GameUIState.IDLE ||
          to === GameUIState.FEATURE_TRANSITION ||
          to === GameUIState.SPINNING
        )
      case GameUIState.WIN_SHOW:
        return (
          to === GameUIState.IDLE ||
          to === GameUIState.FEATURE_TRANSITION ||
          to === GameUIState.SPINNING
        )
      case GameUIState.FEATURE_TRANSITION:
        return to === GameUIState.SPINNING || to === GameUIState.IDLE
      default:
        return false
    }
  }

  private notify(): void {
    for (const listener of this._listeners) {
      listener(this._currentState)
    }
  }
}
