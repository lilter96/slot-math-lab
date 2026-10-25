import type { Unsubscribe } from './signal.js'
import type { EmptyPayload } from '@tgslots/shared-contracts'

export interface GameEventMap {
  'win:awarded': { amount: number; multiplierX: number }
  'feature:enter': { type: string }
  'feature:exit': { type: string }
  'balance:changed': { balance: number }
  'free-spins:updated': { remaining: number; awarded?: number }
  'auto-spin:updated': { active: boolean; remaining: number }
  'pick-card-selected': { index: number }
  'buy-bonus:requested': EmptyPayload
  'feature-modal:open': EmptyPayload
  'feature-buy:requested': { optionId: string }
  'volatility:selected': { mode: string }
  'bet:changed': { multiplier: number }
  'error:api': { message: string }

  // Audio specific events
  'spin:started': EmptyPayload
  'reel:stopped': { reelIndex: number; isLast: boolean }
  'feature:announced': { type: 'bonus' | 'free-spins' }
  'pick:card:revealed': { index: number; value: number }
}

export type GameEventType = keyof GameEventMap

export class GameEventBus {
  private readonly _listeners = new Map<
    string,
    Set<(payload: GameEventMap[GameEventType]) => void>
  >()

  on<K extends GameEventType>(event: K, fn: (payload: GameEventMap[K]) => void): Unsubscribe {
    let set = this._listeners.get(event)
    if (!set) {
      set = new Set()
      this._listeners.set(event, set)
    }
    set.add(fn as (payload: GameEventMap[GameEventType]) => void)
    return () => set!.delete(fn as (payload: GameEventMap[GameEventType]) => void)
  }

  emit<K extends GameEventType>(event: K, payload: GameEventMap[K]): void {
    const set = this._listeners.get(event)
    if (!set) return
    for (const fn of set) {
      try {
        fn(payload)
      } catch (err) {
        console.error(`[EventBus] Listener for "${event}" threw:`, err)
      }
    }
  }

  clearAll(): void {
    this._listeners.clear()
  }
}
