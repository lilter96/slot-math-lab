import type { LeMilitareResult } from '@tgslots/le-militare'
import type { LeMilitareSerializedState } from '@tgslots/shared-contracts/states'
import type { IGameClient } from '../../engine/game-client.js'
import type { EmptyPayload } from '@tgslots/shared-contracts'
import { manifest } from './manifest.js'
import { assets } from './assets.js'
import { LeMilitareRuntime } from './runtime.js'
import './events.js'

declare module '@tgslots/shared-contracts/game-registry' {
  interface GameRegistry {
    'le-militare': {
      state: LeMilitareSerializedState
      result: LeMilitareResult
      actions: {
        spin: { multiplier: number; mode?: 'recon' | 'assault' | 'siege' }
        buybonus: {
          multiplier: number
          option?: 'standard' | 'elite' | 'super'
          mode?: 'recon' | 'assault' | 'siege'
        }
        chancespin: { multiplier: number; mode?: 'recon' | 'assault' | 'siege' }
        airraidspin: { multiplier: number; mode?: 'recon' | 'assault' | 'siege' }
        freespin: EmptyPayload
        state: EmptyPayload
      }
    }
  }
}

export const leMilitareClient: IGameClient<'le-militare'> = {
  manifest,
  assets,
  soundMapping: {
    'le-militare:missile:launched': { name: 'launch-air', options: { volume: 0.5 } },
    'le-militare:impact': { name: 'impact-soft', options: { volume: 0.65 } },
    'spin:started': { name: 'spin-start', options: { volume: 0.3 } },
    'reel:stopped': { name: 'reel-stop', options: { volume: 0.5 } },
    'win:awarded': {
      name: 'win-small',
      options: { volume: 0.8 },
    },
    'le-militare:win:tier:crossed': 'win-big',
    'feature:announced': 'win-big',
  },
  async mount(ctx) {
    const runtime = new LeMilitareRuntime()
    await runtime.init(ctx)
    return runtime
  },
}
