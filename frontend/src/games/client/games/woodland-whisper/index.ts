import type { WoodlandWhisperResult } from '@tgslots/woodland-whisper'
import type { WoodlandWhisperSerializedState } from '@tgslots/shared-contracts/states'
import type { IGameClient } from '../../engine/game-client.js'
import type { EmptyPayload } from '@tgslots/shared-contracts'
import { manifest } from './manifest.js'
import { assets } from './assets.js'
import { WoodlandWhisperRuntime } from './runtime.js'

declare module '@tgslots/shared-contracts/game-registry' {
  interface GameRegistry {
    'woodland-whisper': {
      state: WoodlandWhisperSerializedState
      result: WoodlandWhisperResult
      actions: {
        spin: { multiplier: number }
        buybonus: { multiplier: number }
        freespin: EmptyPayload
        pick: { userIndex: number }
        state: EmptyPayload
      }
    }
  }
}

export const woodlandWhisperClient: IGameClient<'woodland-whisper'> = {
  manifest,
  assets,
  soundMapping: {
    'spin:started': {
      name: 'spin-start',
      options: { volume: 0.28 },
    },
    'reel:stopped': { name: 'reel-stop', options: { volume: 0.35 } },
    'win:awarded': {
      name: 'win-small',
      options: { volume: 0.8 },
    },
    'feature:announced': 'win-big',
    'pick:card:revealed': 'card-reveal',
  },
  async mount(ctx) {
    const runtime = new WoodlandWhisperRuntime()
    await runtime.init(ctx)
    return runtime
  },
}
