import { Container, Graphics } from 'pixi.js'
import type { ShootdownEvent, ActivationEvent } from '@tgslots/le-militare'
import type { GameAssets } from '../../../engine/asset-registry.js'
import type { GameEventBus } from '../../../engine/event-bus.js'
import { ANIMATION_CONFIG } from '../animation-config.js'
import { killAllTweens } from '../helpers/tween-utils.js'
import { drawArmedReels } from './armed-reel-renderer.js'
import { animateActivations } from './activation-animator.js'
import { fireMissile } from './missile.js'
import { playAirRaid } from './air-raid.js'
import type { AirRaidPlacement } from '@tgslots/le-militare'
import type { CombatLayout } from './combat-layout.js'
import { EngagementBoard } from './engagement-board.js'
import { GiantWildView } from './giant-wild-view.js'

export class CombatOperationView extends Container {
  private _bus: GameEventBus
  private _overlay: Graphics
  private _armedReels: Graphics
  private _current: Graphics
  private readonly _giants: GiantWildView
  private readonly _engagement = new EngagementBoard()

  private _mascotLaunchX = 0
  private _mascotLaunchY = 0
  private _armedReelStates: boolean[] = []

  private _layout: CombatLayout = {
    symbolWidth: 0,
    symbolHeight: 0,
    reelSpacing: 0,
    totalHeight: 0,
    scale: 1,
  }

  constructor(
    bus: GameEventBus,
    private readonly _assets: GameAssets,
  ) {
    super()
    this._bus = bus
    this._giants = new GiantWildView(_assets)
    this.addChild(this._giants)
    this._armedReels = new Graphics()
    this.addChild(this._armedReels)
    this._current = new Graphics()
    this.addChild(this._current)
    this._overlay = new Graphics()
    this.addChild(this._engagement)
    this.addChild(this._overlay)
  }

  setLaunchPoint(lx: number, ly: number, reelCount: number): void {
    this._mascotLaunchX = lx
    this._mascotLaunchY = ly
    if (this._armedReelStates.length !== reelCount) {
      this._armedReelStates = new Array(reelCount).fill(false)
    }
  }

  clearPersistentMultipliers(): void {
    this._clearArmedReels()
  }

  clearArmedReels(): void {
    this._clearArmedReels()
  }

  setLayout(layout: CombatLayout): void {
    if (layout.symbolWidth === 0) return
    this._layout = layout
    this._giants.setLayout(layout)
    this._redrawArmedReels()
  }

  async animateActivations(activations: readonly ActivationEvent[]): Promise<void> {
    await animateActivations(
      this,
      this._overlay,
      this._armedReels,
      this._current,
      activations,
      this._layout,
      this._armedReelStates,
    )
  }

  setGiantReels(reels: Iterable<number>): void {
    this._giants.setReels(reels)
  }

  highlightGiants(reels: Iterable<number>): void {
    this._giants.highlight(reels)
  }

  // Carry armed-state indicators into a free spin without replaying activation.
  energizeReels(reels: Iterable<number>): void {
    let changed = false
    for (const reel of reels) {
      if (reel >= 0 && reel < this._armedReelStates.length && !this._armedReelStates[reel]) {
        this._armedReelStates[reel] = true
        changed = true
      }
    }
    if (changed) this._redrawArmedReels()
  }

  async animateShootdowns(
    shootdowns: readonly ShootdownEvent[],
    _activations: readonly ActivationEvent[],
    wildId: number,
  ): Promise<void> {
    if (shootdowns.length === 0) return

    await this._engagement.acquire(
      shootdowns,
      this._layout,
      this._armedReelStates.length,
      'TARGETS ACQUIRED',
    )

    const { symbolWidth: sw, symbolHeight: sh, reelSpacing: rs } = this._layout

    for (let i = 0; i < shootdowns.length; i++) {
      const sd = shootdowns[i]!
      await fireMissile({
        parent: this,
        lx: this._mascotLaunchX,
        ly: this._mascotLaunchY,
        tx: sd.reel * (sw + rs) + sw / 2,
        ty: sd.row * sh + sh / 2,
        sw,
        sh,
        reel: sd.reel,
        row: sd.row,
        multiplier: sd.multiplier,
        wildId,
        bus: this._bus,
        texture: this._assets.getTexture('COMBAT_MISSILE'),
        impactFrames: [0, 1, 2, 3].map((index) => this._assets.getTexture(`IMPACT_${index}`)),
      })

      const reelRemaining = shootdowns.slice(i + 1).some((other) => other.reel === sd.reel)
      if (!reelRemaining) {
        this._armedReelStates[sd.reel] = false
        this._redrawArmedReels()
      }

      await new Promise((r) => setTimeout(r, ANIMATION_CONFIG.INTER_MISSILE_PAUSE_MS))
    }
    this._engagement.hide()
  }

  async animateAirRaid(
    placements: readonly AirRaidPlacement[],
    wildId: number,
    squadronSize: number,
  ): Promise<void> {
    if (squadronSize === 0 || this._layout.symbolWidth === 0) return
    await this._engagement.acquire(
      placements,
      this._layout,
      this._armedReelStates.length,
      'AIR RAID INBOUND',
    )
    const { symbolWidth: sw, symbolHeight: sh, reelSpacing: rs } = this._layout
    await playAirRaid({
      parent: this,
      lx: this._mascotLaunchX,
      ly: this._mascotLaunchY,
      sw,
      sh,
      rs,
      reelCount: this._armedReelStates.length,
      placements,
      squadronSize,
      wildId,
      bus: this._bus,
      texture: this._assets.getTexture('COMBAT_PLANE'),
      missileTexture: this._assets.getTexture('COMBAT_MISSILE'),
      impactFrames: [0, 1, 2, 3].map((index) => this._assets.getTexture(`IMPACT_${index}`)),
    })
    this._engagement.hide()
  }

  override destroy(options?: {
    children?: boolean
    texture?: boolean
    baseTexture?: boolean
  }): void {
    killAllTweens(this)
    super.destroy(options)
  }

  private _clearArmedReels(): void {
    this._armedReelStates.fill(false)
    this._current.clear()
    this._redrawArmedReels()
  }

  private _redrawArmedReels(): void {
    drawArmedReels(this._armedReels, this._layout, this._armedReelStates)
  }
}
