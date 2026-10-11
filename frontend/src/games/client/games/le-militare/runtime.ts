import { Graphics, Sprite } from 'pixi.js'
import type { Texture } from 'pixi.js'
import type { GameRuntime, GameUIContext } from '../../engine/game-client.js'
import type { UILayoutSnapshot } from '../../engine/layout.js'
import { ReelSet } from '../../engine/reel-set.js'
import { WinOverlay } from '../../engine/win-overlay.js'
import { Symbols } from '@tgslots/le-militare'
import type { LeMilitareResult, AirRaidPresentation } from '@tgslots/le-militare'
import type { LeMilitareSerializedState } from '@tgslots/shared-contracts/states'
import type { ActionType } from '@tgslots/shared-contracts'
import { manifest } from './manifest.js'
import { ANIMATION_CONFIG } from './animation-config.js'
import { transposeGrid, decodePosition } from './helpers/grid-transform.js'
import { groupHitsBySymbol } from './helpers/cluster-grouping.js'
import { deriveFreeCarryOverMultiplier } from './helpers/free-spins-math.js'
import { projectMascotPointsToCombatLocal } from './helpers/mascot-projection.js'
import { derivePresentPlan, type PresentPlan } from './helpers/present-plan.js'
import { getSpinSpeedProfile } from '../../engine/spin-speed.js'
import type { SpinSpeedProfile } from '../../engine/spin-speed.js'
import { CombatOperationView } from './combat/index.js'
import './events.js'
import type { CombatLayout } from './combat/combat-layout.js'
import { MultiplierHud } from './multiplier-hud.js'
import { BuyFeatureControl } from './buy-feature-control.js'
import { BuyFeatureModal } from './buy-feature-modal.js'
import { OperationBriefing } from './operation-briefing.js'
import { S300Mascot } from './mascot/index.js'
import { ReelFrame } from './reel-frame/reel-frame.js'

const CLUSTER_WIN_COLOR = 0xd4af37
const SCATTER_WIN_COLOR = 0xff4444
const SCATTER_ID = Symbols['SCATTER']!
const WILD_ID = Symbols['WILD']!

const GRID_CONFIG = {
  reels: manifest.grid.reels,
  rows: manifest.grid.rows,
  reelSpacing: 4,
}
const REEL_CONFIG = {
  symbolWidth: manifest.symbolSize,
  symbolHeight: manifest.symbolSize,
  visibleSymbols: manifest.grid.rows,
  totalSymbols: 7,
}

export class LeMilitareRuntime implements GameRuntime<'le-militare'> {
  private _ctx!: GameUIContext<'le-militare'>
  private _reelSet!: ReelSet
  private _overlay!: WinOverlay
  private _bgSprite!: Sprite
  private _bgTex!: Texture
  private _boardPlate!: Graphics
  private _mask!: Graphics
  private _frame!: ReelFrame
  private _layout?: UILayoutSnapshot
  private _spinSpeedProfile: SpinSpeedProfile = getSpinSpeedProfile('normal')
  private _combatOpView!: CombatOperationView
  private _multiplierHud!: MultiplierHud
  private _buyFeatureControl?: BuyFeatureControl
  private _buyFeatureModal?: BuyFeatureModal
  private _operationBriefing!: OperationBriefing
  private _freeRemaining = 0
  private _freeTotal = 0
  private _mascot!: S300Mascot
  private _destroyed = false
  private readonly _unsubs: Array<() => void> = []

  async init(ctx: GameUIContext<'le-militare'>): Promise<void> {
    this._ctx = ctx
    ctx.sound.playBGM('bgm-combat')

    this._bgTex = ctx.assets.getTexture('BACKGROUND_16_9')
    this._bgSprite = new Sprite(this._bgTex)
    this._bgSprite.anchor.set(0.5)
    ctx.scene.background.addChild(this._bgSprite)

    const emptyGrid = Array.from({ length: GRID_CONFIG.reels }, (_col, col) =>
      Array.from({ length: GRID_CONFIG.rows }, (_row, row) => 1 + ((col * 3 + row) % 9)),
    )
    this._reelSet = new ReelSet(
      GRID_CONFIG,
      REEL_CONFIG,
      emptyGrid,
      ctx.assets,
      manifest.symbols.length,
    )

    this._boardPlate = new Graphics()
    ctx.scene.reels.addChild(this._boardPlate)

    this._mask = new Graphics()
    ctx.scene.reels.addChild(this._mask)
    this._reelSet.mask = this._mask
    ctx.scene.reels.addChild(this._reelSet)

    this._frame = new ReelFrame({
      reels: GRID_CONFIG.reels,
      rows: GRID_CONFIG.rows,
      symbolSize: REEL_CONFIG.symbolWidth,
      reelSpacing: GRID_CONFIG.reelSpacing,
    })
    ctx.scene.reels.addChild(this._frame)

    this._combatOpView = new CombatOperationView(ctx.eventBus, ctx.assets)
    ctx.scene.reels.addChild(this._combatOpView)

    this._unsubs.push(
      ctx.eventBus.on('le-militare:symbol:transform', (e) => {
        this._reelSet.setSymbolAt(e.reel, e.row, e.newSymbolId)
      }),
      ctx.eventBus.on('le-militare:multiplier:stick', (e) => {
        const sym = this._reelSet.getReel(e.reel).getSymbolAt(e.row)
        if (sym) {
          // The badge was animated in the combat-view's design space; once it
          // belongs to the symbol it must sit at the symbol-local origin (the
          // multiplierContainer is already centred on the cell), otherwise it
          // keeps its old coordinates and lands at a random offset / behind cells.
          e.badge.position.set(0, 0)
          e.badge.scale.set(1)
          sym.multiplierContainer.addChild(e.badge)
        } else {
          e.badge.destroy({ children: true })
        }
      }),
    )

    this._multiplierHud = new MultiplierHud()
    ctx.scene.overlays.addChild(this._multiplierHud)

    this._buyFeatureControl = new BuyFeatureControl(ctx.eventBus, ctx.fsm)
    ctx.hud.slot('control-right').addChild(this._buyFeatureControl)

    this._buyFeatureModal = new BuyFeatureModal(
      ctx.eventBus,
      () => ctx.session.betMultiplier,
      () => ctx.session.balance,
    )
    ctx.scene.overlays.addChild(this._buyFeatureModal)
    this._unsubs.push(ctx.eventBus.on('feature-modal:open', () => this._buyFeatureModal?.show()))

    this._mascot = new S300Mascot(ctx.assets)
    ctx.scene.background.addChild(this._mascot)

    this._overlay = new WinOverlay()
    this._overlay.setGame(ctx.assets, [...manifest.winTiers])
    ctx.scene.overlays.addChild(this._overlay)
    this._operationBriefing = new OperationBriefing(ctx.assets)
    ctx.scene.overlays.addChild(this._operationBriefing)
  }

  applyState(state: LeMilitareSerializedState): void {
    const remaining = state.freeSpins?.spinsRemaining ?? 0
    this._freeRemaining = remaining
    this._freeTotal = state.freeSpins?.totalWin ?? 0
    this._ctx.eventBus.emit('free-spins:updated', { remaining })
    this._multiplierHud.setValue(state.freeSpins?.multiplierSum ?? 1)
  }

  /** Restore the visual grid from a persisted lastGrid (session restore). */
  restoreGrid(grid: number[][]): void {
    if (!grid.length || !grid[0]?.length) return
    this._reelSet.setSymbols(transposeGrid(grid))
  }

  async presentResult(_action: ActionType<'le-militare'>, result: LeMilitareResult): Promise<void> {
    this._combatOpView.clearPersistentMultipliers()
    await this._present(result, derivePresentPlan(result))
  }

  resize(layout: UILayoutSnapshot): void {
    this._layout = layout
    const { W, H } = { W: layout.screenWidth, H: layout.screenHeight }

    this._bgTex = this._ctx.assets.getTexture(
      layout.orientation === 'portrait' ? 'BACKGROUND_9_16' : 'BACKGROUND_16_9',
    )
    this._bgSprite.texture = this._bgTex
    const bgScale = Math.max(W / this._bgTex.width, H / this._bgTex.height)
    this._bgSprite.scale.set(bgScale)
    this._bgSprite.x = W / 2
    this._bgSprite.y = H / 2

    const reelScale = layout.reelBounds.width / manifest.reelNaturalWidth
    this._reelSet.scale.set(reelScale)
    this._reelSet.x = layout.reelBounds.x
    this._reelSet.y = layout.reelBounds.y

    this._combatOpView.scale.set(reelScale)
    this._combatOpView.x = layout.reelBounds.x
    this._combatOpView.y = layout.reelBounds.y

    this._multiplierHud.x = layout.reelBounds.x + layout.reelBounds.width - 60 * reelScale
    this._multiplierHud.y = Math.max(layout.reelBounds.y - 40 * reelScale, layout.safePadding)
    this._multiplierHud.scale.set(reelScale)

    this._boardPlate.clear()
    this._boardPlate
      .roundRect(
        layout.reelBounds.x - 8,
        layout.reelBounds.y - 8,
        layout.reelBounds.width + 16,
        layout.reelBounds.height + 16,
        14,
      )
      .fill({ color: 0x0b1720, alpha: 0.92 })
    this._mask.clear()
    this._mask
      .rect(
        layout.reelBounds.x,
        layout.reelBounds.y,
        layout.reelBounds.width,
        layout.reelBounds.height,
      )
      .fill(0xffffff)

    this._frame.update(layout, reelScale)

    this._overlay.resize(layout)
    this._operationBriefing.resize(layout)
    this._buyFeatureModal?.resize(layout)
    this._mascot.resize(layout)

    this._syncCombatOrigin()
    const combatLayout: CombatLayout = {
      symbolWidth: REEL_CONFIG.symbolWidth,
      symbolHeight: REEL_CONFIG.symbolHeight,
      reelSpacing: GRID_CONFIG.reelSpacing,
      totalHeight: REEL_CONFIG.visibleSymbols * REEL_CONFIG.symbolHeight,
      scale: 1, // draw in design space, container scale handles the rest
    }
    this._combatOpView.setLayout(combatLayout)
  }

  destroy(): void {
    this._destroyed = true
    for (const unsub of this._unsubs) unsub()
    this._unsubs.length = 0
    // FIX 2.4: destroy combat-op view before reel set so in-flight badge/missile tweens
    // targeting SymbolViews are killed before those views are torn down.
    this._combatOpView.destroy({ children: true })
    this._multiplierHud.destroy({ children: true })
    this._reelSet.destroy({ children: true })
    this._overlay.destroy({ children: true })
    this._operationBriefing?.destroy({ children: true })
    this._bgSprite.destroy()
    this._boardPlate.destroy()
    this._mask.destroy()
    this._frame.destroy()
    this._buyFeatureControl?.destroy({ children: true })
    this._buyFeatureModal?.destroy({ children: true })
    this._mascot.destroy({ children: true })
  }

  syncSpinSpeed(profile: SpinSpeedProfile): void {
    this._spinSpeedProfile = profile
    this._reelSet.syncSpinSpeed(profile)
    this._overlay.syncSpinSpeed(profile)
  }

  private async _present(result: LeMilitareResult, plan: PresentPlan): Promise<void> {
    const bus = this._ctx.eventBus
    bus.emit('le-militare:spin:resolving:started', { resultType: result.type })

    // Base-game Air Raid: stop the reels on the pre-raid grid so the planes fly
    // over the original symbols and reveal each multiplier-WILD as they crash.
    const airRaid = result.type === 'BASE' ? result.airRaid : null
    const stopGrid = transposeGrid(
      airRaid ? airRaid.preRaidGrid : (result.steps[0]?.preCombatGrid ?? []),
    )
    // Restore carried wilds before any reel starts. The previous cascade may
    // have left ordinary symbols here; skipping spin alone would leave those
    // stale textures visible until stop(), despite the reel already being held.
    const heldReels = this._heldReels(result, stopGrid)
    this._combatOpView.setGiantReels(heldReels ?? [])
    if (heldReels) {
      for (const reel of heldReels) {
        this._reelSet.getReel(reel).setSymbols(stopGrid[reel]!)
      }
      this._combatOpView.energizeReels(heldReels)
    }
    bus.emit('spin:started', {})
    this._reelSet.spin(heldReels)
    await this._wait(this._spinSpeedProfile.reelSpinMs)
    await this._reelSet.stop(stopGrid, heldReels, (index) => {
      bus.emit('reel:stopped', { reelIndex: index, isLast: index === stopGrid.length - 1 })
    })

    await this._playCascadeSteps(result, airRaid)

    if (plan.retriggerAnnounce) {
      this._ctx.sound.playSFX('feature-rise')
      await this._operationBriefing.play(result.freeSpinsAwarded, result.type === 'FREE')
    }

    if (result.win > 0) {
      const wager = this._ctx.session.lastWager
      for (const tier of manifest.winTiers) {
        if (result.win >= tier.thresholdX * wager) {
          bus.emit('le-militare:win:tier:crossed', { thresholdX: tier.thresholdX, win: result.win })
          break
        }
      }
      bus.emit('win:awarded', { amount: result.win, multiplierX: result.multiplierSum })
      await this._overlay.announceWin(result.win, wager)
    }

    if (result.type === 'FREE' && this._freeRemaining === 0 && !result.retriggered) {
      await this._operationBriefing.play(this._freeTotal, false, true)
    }

    bus.emit('le-militare:spin:resolving:completed', { resultType: result.type, win: result.win })
  }

  // Free-spin armed reels are full-row wilds that persist across the session; a
  // held reel is one whose entire column is already WILD on entry. Base/buy
  // spins never lock reels (Air Raid wilds are single cells, not full columns).
  private _heldReels(
    result: LeMilitareResult,
    stopGrid: number[][],
  ): ReadonlySet<number> | undefined {
    if (result.type !== 'FREE') return undefined
    const held = new Set<number>()
    stopGrid.forEach((rows, reel) => {
      if (rows.length > 0 && rows.every((id) => id === WILD_ID)) held.add(reel)
    })
    return held.size > 0 ? held : undefined
  }

  private _syncCombatOrigin(): void {
    const lp = this._mascot.getLaunchPoint()
    const cp = this._mascot.getConnectionPoint()
    const proj = projectMascotPointsToCombatLocal({
      mascotX: this._mascot.x,
      mascotY: this._mascot.y,
      mascotScale: this._mascot.scale.x,
      launchPoint: lp,
      connectionPoint: cp,
      combatViewX: this._combatOpView.x,
      combatViewY: this._combatOpView.y,
      reelScale: this._combatOpView.scale.x,
    })

    this._combatOpView.setLaunchPoint(proj.launchLocalX, proj.launchLocalY, GRID_CONFIG.reels)
  }

  private async _playCascadeSteps(
    result: LeMilitareResult,
    airRaid: AirRaidPresentation | null = null,
  ): Promise<void> {
    const bus = this._ctx.eventBus
    this._reelSet.clearAllMultipliers()

    // For FREE spins, HUD shows the carry-over multiplier from earlier spins in the session
    let currentMultiplier = deriveFreeCarryOverMultiplier(result)
    // FIX 2.3: push carry value immediately so HUD is correct even when the first
    // step has no shootdowns (otherwise the HUD lags until the first multiplier event).
    this._multiplierHud.setValue(currentMultiplier)

    // Base Air Raid plays before the cascade highlights: planes crash onto cells
    // and convert them to multiplier-WILDs (matching steps[0].preCombatGrid).
    const raidActive = !!airRaid && airRaid.squadronSize > 0
    if (raidActive) {
      const deployPromise = this._mascot.triggerS300Feature()
      bus.emit('le-militare:mascot:deployed', { stepIndex: 0 })
      await deployPromise
      this._syncCombatOrigin()
      await this._combatOpView.animateAirRaid(airRaid!.placements, WILD_ID, airRaid!.squadronSize)
      for (const pl of airRaid!.placements) currentMultiplier += pl.multiplier
      this._multiplierHud.setValue(currentMultiplier)
    }

    if (result.steps.length === 0) {
      bus.emit('le-militare:spin:resolving:completed', { resultType: result.type, win: result.win })
      return
    }

    const hasAnyCombatOp = result.steps.some(
      (s) => s.activations.length > 0 || s.shootdowns.length > 0,
    )

    const giantReels = new Set(
      this._heldReels(result, transposeGrid(result.steps[0]?.preCombatGrid ?? [])),
    )
    for (let i = 0; i < result.steps.length; i++) {
      const step = result.steps[i]!
      bus.emit('le-militare:cascade:step:started', { index: i })

      // ── Combat Operation ──────────────────────────────────────────────────
      // Animate S300 column flash then plane shootdowns (in sequence per activation,
      // shootdowns fire in parallel for all planes on this step).
      if (step.activations.length > 0) {
        const deployPromise = this._mascot.triggerS300Feature()
        bus.emit('le-militare:mascot:deployed', { stepIndex: i })
        bus.emit('le-militare:combat:activations:started', { activations: step.activations })
        // Run activation overlays in parallel with deploy; await both before missiles
        const activationPromise = this._combatOpView.animateActivations(step.activations)
        await Promise.all([deployPromise, activationPromise])
        if (result.type === 'FREE') {
          for (const activation of step.activations) giantReels.add(activation.reel)
          this._combatOpView.setGiantReels(giantReels)
        }
        bus.emit('le-militare:combat:activations:completed', { activations: step.activations })
      }
      if (step.shootdowns.length > 0) {
        await this._mascot.triggerS300Feature()
        this._syncCombatOrigin()
        bus.emit('le-militare:combat:shootdowns:started', { shootdowns: step.shootdowns })
        await this._combatOpView.animateShootdowns(step.shootdowns, step.activations, WILD_ID)
        bus.emit('le-militare:combat:shootdowns:completed', { shootdowns: step.shootdowns })
        for (const sd of step.shootdowns) currentMultiplier += sd.multiplier
        this._multiplierHud.setValue(currentMultiplier)
      }
      // Snap grid to postCombatGrid (giant wilds + multiplier wilds now visible)
      if (step.activations.length > 0 || step.shootdowns.length > 0) {
        this._reelSet.setSymbols(transposeGrid(step.postCombatGrid))
        await this._wait(ANIMATION_CONFIG.POST_COMBAT_SETTLE_MS)
      }

      // ── No cluster hits → terminal step ───────────────────────────────────
      if (step.hits.length === 0) {
        bus.emit('le-militare:cascade:step:completed', { index: i })
        break
      }

      // ── Cluster win highlights ────────────────────────────────────────────
      // Group hits by symbol type so we highlight one symbol group at a time.
      const hitsBySymbol = groupHitsBySymbol(step.hits)

      for (const [, hits] of hitsBySymbol) {
        this._combatOpView.highlightGiants(
          hits.flatMap((hit) => hit.positions.map((pos) => Math.floor(pos / GRID_CONFIG.rows))),
        )
        for (const hit of hits) {
          for (const encoded of hit.positions) {
            const { reel, row } = decodePosition(encoded, GRID_CONFIG.rows)
            this._reelSet.highlightCell(reel, row, CLUSTER_WIN_COLOR)
          }
        }
        await this._wait(this._spinSpeedProfile.lineHighlightMinMs)
        this._reelSet.clearAllHighlights()
        this._combatOpView.highlightGiants([])
      }

      // ── Cascade animation ─────────────────────────────────────────────────
      // If a next step exists, animate vanished symbols falling out and new ones
      // falling in to reach the next step's preCombatGrid.
      const nextStep = result.steps[i + 1]
      if (nextStep) {
        await this._reelSet.cascadeGrid(
          step.vanishedPositions,
          transposeGrid(nextStep.preCombatGrid),
          GRID_CONFIG.rows,
        )
      }
      bus.emit('le-militare:cascade:step:completed', { index: i })
    }

    // ── Final grid ────────────────────────────────────────────────────────────
    // Only set if the last step had no combat ops (which already set the grid inside the loop)
    const lastStep = result.steps[result.steps.length - 1]
    const lastStepHadCombat = lastStep
      ? lastStep.activations.length > 0 || lastStep.shootdowns.length > 0
      : false
    if (lastStep && !lastStepHadCombat) {
      this._reelSet.setSymbols(transposeGrid(lastStep.postCombatGrid))
    }

    // ── Scatter highlight (if free spins triggered) ───────────────────────────
    if (result.scatterCount > 0) {
      const finalGrid = lastStep?.postCombatGrid ?? []
      for (let reel = 0; reel < GRID_CONFIG.reels; reel++) {
        for (let row = 0; row < GRID_CONFIG.rows; row++) {
          if (finalGrid[row]?.[reel] === SCATTER_ID) {
            this._reelSet.highlightCell(reel, row, SCATTER_WIN_COLOR)
          }
        }
      }
      await this._wait(this._spinSpeedProfile.scatterHighlightMs)
      this._reelSet.clearAllHighlights()
    }

    // Retract launcher after all animations if it was deployed this spin
    if (hasAnyCombatOp || raidActive) {
      this._combatOpView.clearArmedReels()
      // Retract during the next reel spin; the short movement is serialized
      // with deployment so the launcher cannot jump between poses.
      this._mascot.retractLauncher().catch(() => {
        // Ignore retraction errors; mascot state resets on next deploy
      })
      bus.emit('le-militare:mascot:retracted', { stepIndex: result.steps.length - 1 })
    }
  }

  private _wait(ms: number): Promise<void> {
    const ac = new AbortController()
    const id = setTimeout(() => {
      if (!ac.signal.aborted) ac.abort()
    }, ms)
    // Stop the timer if destroyed
    const poll = setInterval(() => {
      if (this._destroyed) {
        clearTimeout(id)
        ac.abort()
        clearInterval(poll)
      }
    }, 100)
    return new Promise<void>((resolve) => {
      ac.signal.addEventListener('abort', () => {
        clearInterval(poll)
        if (!this._destroyed) resolve()
      })
    })
  }
}
