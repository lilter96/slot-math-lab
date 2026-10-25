import type { Container } from 'pixi.js'
import type { ActivationEvent, ShootdownEvent, LeMilitareResult } from '@tgslots/le-militare'

declare module '../../engine/event-bus.js' {
  interface GameEventMap {
    'le-militare:missile:launched': Record<string, void>
    'le-militare:impact': Record<string, void>
    'le-militare:symbol:transform': { reel: number; row: number; newSymbolId: number }
    'le-militare:multiplier:stick': {
      reel: number
      row: number
      multiplier: number
      badge: Container
    }
    'le-militare:mascot:deployed': { stepIndex: number }
    'le-militare:mascot:retracted': { stepIndex: number }
    'le-militare:cascade:step:started': { index: number }
    'le-militare:cascade:step:completed': { index: number }
    'le-militare:combat:activations:started': { activations: readonly ActivationEvent[] }
    'le-militare:combat:activations:completed': { activations: readonly ActivationEvent[] }
    'le-militare:combat:shootdowns:started': { shootdowns: readonly ShootdownEvent[] }
    'le-militare:combat:shootdowns:completed': { shootdowns: readonly ShootdownEvent[] }
    'le-militare:spin:resolving:started': { resultType: LeMilitareResult['type'] }
    'le-militare:spin:resolving:completed': { resultType: LeMilitareResult['type']; win: number }
    'le-militare:win:tier:crossed': { thresholdX: number; win: number }
  }
}

export {}
