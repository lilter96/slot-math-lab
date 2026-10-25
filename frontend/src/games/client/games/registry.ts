import type { GameId } from '@tgslots/shared-contracts'
import type { IGameClient, StandaloneGameClient } from '../engine/game-client.js'
import { woodlandWhisperClient } from './woodland-whisper/index.js'
import { ancientDragonClient } from './ancient-dragon/index.js'
import { leMilitareClient } from './le-militare/index.js'

import { nineLivesClient } from './nine-lives/index.js'
import { x7ClubClient } from './x7-club/index.js'

export const gameRegistry: Record<string, IGameClient<GameId> | StandaloneGameClient> = {
  'woodland-whisper': woodlandWhisperClient,
  'ancient-dragon': ancientDragonClient,
  'le-militare': leMilitareClient,
  'x7-club': x7ClubClient,
  'nine-lives': nineLivesClient,
}
