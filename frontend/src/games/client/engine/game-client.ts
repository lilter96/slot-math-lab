import type { GameId, ActionType, GameState, GameResult } from '@tgslots/shared-contracts'
import type { GameManifest, AssetManifest } from '@tgslots/shared-contracts'
import type { Scene } from './scene.js'
import type { GameEventBus } from './event-bus.js'
import type { GameDispatcher } from './dispatcher.js'
import type { GameAssets } from './asset-registry.js'
import type { UILayoutSnapshot } from './layout.js'
import type { GameStateMachine } from './state-machine.js'
import type { SessionManager } from './session-manager.js'
import type { HUD } from './hud.js'
import type { SpinSpeedProfile } from './spin-speed.js'
import type { SoundMapping, SoundManager } from './sound-manager.js'

export type { GameManifest, AssetManifest, GameAssets }

export interface GameUIContext<G extends GameId> {
  readonly scene: Scene
  readonly eventBus: GameEventBus
  readonly dispatcher: GameDispatcher<G>
  readonly assets: GameAssets
  readonly fsm: GameStateMachine
  readonly session: SessionManager
  readonly hud: HUD
  readonly sound: SoundManager
}

export interface GameRuntime<G extends GameId> {
  applyState(state: GameState<G>): void
  presentResult<A extends ActionType<G>>(action: A, result: GameResult<G>): Promise<void>
  resize(layout: UILayoutSnapshot): void
  syncSpinSpeed?(profile: SpinSpeedProfile): void
  destroy(): void
  /** Optional: resume any active feature (e.g. pick bonus) after a session restore. */
  resumeFeatures?(): Promise<void>
  /** Optional: restore the visual grid from a persisted lastGrid (session restore). */
  restoreGrid?(grid: number[][]): void
}

export interface IGameClient<G extends GameId> {
  readonly manifest: GameManifest
  readonly assets: AssetManifest
  readonly soundMapping?: SoundMapping
  mount(ctx: GameUIContext<G>): Promise<GameRuntime<G>>
}

/** A game with its own action lifecycle can still share the library and asset manifest. */
export interface StandaloneGameClient {
  readonly manifest: GameManifest
  readonly assets: AssetManifest
  launch(): Promise<void>
}
