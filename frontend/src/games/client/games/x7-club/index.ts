import { manifest, assets } from './manifest'
import { launchX7Club } from './launch'
import type { StandaloneGameClient } from '../../engine/game-client'

export const x7ClubClient: StandaloneGameClient = { manifest, assets, launch: launchX7Club }

import type { ClubState, ClubResult } from '@tgslots/x7-club'
declare module '@tgslots/shared-contracts/game-registry' {
  interface GameRegistry {
    'x7-club': {
      state: ClubState
      result: ClubResult
      actions: {
        state: Record<string, void>
        spin: { multiplier: number }
        buybonus: { multiplier: number }
        next: Record<string, void>
      }
    }
  }
}
