import type { AncientDragonResult } from '@tgslots/ancient-dragon'
import type { AncientDragonSerializedState } from '@tgslots/shared-contracts/states'
import type { IGameClient } from '../../engine/game-client.js'
import type { EmptyPayload } from '@tgslots/shared-contracts'
import { manifest } from './manifest.js'
import { assets } from './assets.js'
import { AncientDragonRuntime } from './runtime.js'

declare module '@tgslots/shared-contracts/game-registry' {
  interface GameRegistry {
    'ancient-dragon': {
      state: AncientDragonSerializedState
      result: AncientDragonResult
      actions: {
        spin: { multiplier: number }
        freespin: EmptyPayload
        state: EmptyPayload
      }
    }
  }
}

export const ancientDragonClient: IGameClient<'ancient-dragon'> = {
  manifest,
  assets,
  soundMapping: {
    'spin:started': { name: 'spin-start', options: { volume: 0.3 } },
    'reel:stopped': 'reel-stop',
    'win:awarded': { name: 'win-small', options: { volume: 0.8 } },
    'feature:announced': 'win-big',
  },
  async mount(ctx) {
    const runtime = new AncientDragonRuntime()
    await runtime.init(ctx)
    return runtime
  },
}
