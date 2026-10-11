import type { StandaloneGameClient } from '../../engine/game-client'
import type { LivesState, LivesResult } from '@tgslots/nine-lives'
import { manifest, assets } from './manifest'
import { launchNineLives } from './launch'
export const nineLivesClient: StandaloneGameClient = { manifest, assets, launch: launchNineLives }
declare module '@tgslots/shared-contracts/game-registry' {
  interface GameRegistry {
    'nine-lives': {
      state: LivesState
      result: LivesResult
      actions: {
        state: Record<string, void>
        spin: { multiplier: number }
        buybonus: { multiplier: number }
        next: Record<string, void>
      }
    }
  }
}
