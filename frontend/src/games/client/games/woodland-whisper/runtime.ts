import { PaylineTrail } from '../../engine/payline-trail.js'
import { FeatureScene, type FeatureSource } from '../../engine/feature-scene.js'
import { Graphics, Sprite } from 'pixi.js'
import type { GameRuntime } from '../../engine/game-client.js'
import type { GameUIContext } from '../../engine/game-client.js'
import type { UILayoutSnapshot } from '../../engine/layout.js'
import { BackgroundContainer } from './backgrounds/background-container.js'
import { pickVariant } from './backgrounds/scenes.js'
import { ReelSet } from '../../engine/reel-set.js'
import { WinOverlay } from '../../engine/win-overlay.js'
import { PickBonusView } from './pick-bonus-view.js'
import { BuyBonusControl } from './buy-bonus-control.js'
import { PAYLINE_DATA, Symbols } from '@tgslots/woodland-whisper'
import type {
  WoodlandWhisperBaseResult,
  WoodlandWhisperBuyResult,
  WoodlandWhisperFreeResult,
  WoodlandWhisperPickResult,
  WoodlandWhisperResult,
} from '@tgslots/woodland-whisper'
import { BUY_BONUS_COST_MULTIPLIER } from '@tgslots/woodland-whisper'
import type { WoodlandWhisperSerializedState } from '@tgslots/shared-contracts/states'
import type { WWPickBonusSerialized } from '@tgslots/shared-contracts/states'
import type { ActionType } from '@tgslots/shared-contracts'
import { manifest } from './manifest.js'
import { getSpinSpeedProfile } from '../../engine/spin-speed.js'
import type { SpinSpeedProfile } from '../../engine/spin-speed.js'

// Outer bitmap / inner transparent opening ratios — tune visually if artwork changes
const FRAME_OUTER_TO_INNER_X = 1.13
const FRAME_OUTER_TO_INNER_Y = 1.19
const PAYLINE_WIN_COLOR = 0xffd700
const SCATTER_WIN_COLOR = 0xff44cc
const SCATTER_ID = Symbols['COIN']!

const GRID_CONFIG = {
  reels: manifest.grid.reels,
  rows: manifest.grid.rows,
  reelSpacing: 0,
}
const REEL_CONFIG = {
  symbolWidth: manifest.symbolSize,
  symbolHeight: manifest.symbolSize,
  visibleSymbols: manifest.grid.rows,
  totalSymbols: 5,
}

function transposeGrid(grid: number[][]): number[][] {
  const rows = grid.length
  const cols = grid[0]?.length ?? 0
  return Array.from({ length: cols }, (_, c) =>
    Array.from({ length: rows }, (_, r) => grid[r]![c]!),
  )
}

export class WoodlandWhisperRuntime implements GameRuntime<'woodland-whisper'> {
  private _ctx!: GameUIContext<'woodland-whisper'>
  private _reelSet!: ReelSet
  private _featureScene!: FeatureScene
  private _paylineTrail!: PaylineTrail
  private _presentingFree = false
  private _freeRemaining = 0
  private _freeTotal = 0
  private _overlay!: WinOverlay
  private _pickUI!: PickBonusView
  private _buyBonusControl!: BuyBonusControl
  private _background!: BackgroundContainer
  private _boardPlate!: Graphics
  private _mask!: Graphics
  private _frame!: Sprite
  private _layout?: UILayoutSnapshot
  private _pendingPickBonusState: WWPickBonusSerialized | null = null
  private _spinSpeedProfile: SpinSpeedProfile = getSpinSpeedProfile('normal')

  async init(ctx: GameUIContext<'woodland-whisper'>): Promise<void> {
    this._ctx = ctx

    // Start BGM
    ctx.sound.playBGM('bgm-forest')

    // Background
    this._background = new BackgroundContainer({ assets: ctx.assets })
    ctx.scene.background.addChild(this._background)

    // Reel set
    const emptyGrid = Array.from({ length: GRID_CONFIG.reels }, () =>
      Array.from({ length: GRID_CONFIG.rows }, () => 0),
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

    // Mask (sibling to reelSet inside scene.reels layer)
    this._mask = new Graphics()
    ctx.scene.reels.addChild(this._mask)
    this._reelSet.mask = this._mask
    ctx.scene.reels.addChild(this._reelSet)

    // Frame sprite (drawn over the reels, still inside scene.reels layer)
    this._frame = new Sprite(ctx.assets.getTexture('REEL_FRAME'))
    this._frame.anchor.set(0.5)
    ctx.scene.reels.addChild(this._frame)

    // Pick bonus UI
    this._pickUI = new PickBonusView(ctx.assets)
    this._pickUI.on('pick', (index: number) => {
      ctx.eventBus.emit('pick-card-selected', { index })
    })
    ctx.scene.overlays.addChild(this._pickUI)

    // Win overlay
    this._overlay = new WinOverlay()
    this._overlay.setGame(ctx.assets, [...manifest.winTiers])
    ctx.scene.overlays.addChild(this._overlay)
    this._paylineTrail = new PaylineTrail('forest')
    ctx.scene.overlays.addChild(this._paylineTrail)
    this._featureScene = new FeatureScene(ctx.assets, 'forest')
    ctx.scene.overlays.addChild(this._featureScene)

    this._buyBonusControl = new BuyBonusControl(ctx.eventBus, ctx.fsm, BUY_BONUS_COST_MULTIPLIER)
    ctx.hud.slot('control-right').addChild(this._buyBonusControl)
  }

  applyState(state: WoodlandWhisperSerializedState): void {
    if (state.lastGrid) {
      this._reelSet.setSymbols(transposeGrid(state.lastGrid))
    }
    this._pendingPickBonusState = state.pickBonus

    const remaining = state.freeSpins?.spinsRemaining ?? 0
    this._freeRemaining = remaining
    this._freeTotal = state.freeSpins?.totalWin ?? 0
    this._ctx.eventBus.emit('free-spins:updated', { remaining })
  }

  async presentResult(
    _action: ActionType<'woodland-whisper'>,
    result: WoodlandWhisperResult,
  ): Promise<void> {
    this._presentingFree = result.type === 'FREE'
    switch (result.type) {
      case 'BASE':
        await this._presentBase(result)
        break
      case 'BUY':
        await this._presentBuy(result)
        break
      case 'FREE':
        await this._presentFree(result)
        break
      case 'PICK':
        await this._presentPick(result)
        break
    }
  }

  resize(layout: UILayoutSnapshot): void {
    this._layout = layout
    const { W, H } = { W: layout.screenWidth, H: layout.screenHeight }

    // Background
    const variant = pickVariant({
      viewportClass: layout.viewportClass,
      orientation: layout.orientation,
    })
    void this._background.setVariant(variant)
    this._background.setViewport(W, H)

    // Frame — fills reelBounds exactly; reelBounds is sized for the full PNG dimensions
    this._frame.x = layout.reelBounds.x + layout.reelBounds.width / 2
    this._frame.y = layout.reelBounds.y + layout.reelBounds.height / 2
    this._frame.width = layout.reelBounds.width
    this._frame.height = layout.reelBounds.height

    // Inner opening — transparent area of the frame PNG
    const innerWidth = layout.reelBounds.width / FRAME_OUTER_TO_INNER_X
    const innerHeight = layout.reelBounds.height / FRAME_OUTER_TO_INNER_Y
    const innerX = layout.reelBounds.x + (layout.reelBounds.width - innerWidth) / 2
    const innerY = layout.reelBounds.y + (layout.reelBounds.height - innerHeight) / 2

    this._boardPlate.clear()
    this._boardPlate.roundRect(innerX - 4, innerY - 4, innerWidth + 8, innerHeight + 8, 8)
    this._boardPlate.fill({ color: 0x071a17, alpha: 0.97 })

    // Reel set — fit inside inner opening, centered on both axes
    const reelContentW = GRID_CONFIG.reels * REEL_CONFIG.symbolWidth
    const reelContentH = GRID_CONFIG.rows * REEL_CONFIG.symbolHeight
    const reelScale = Math.min(innerWidth / reelContentW, innerHeight / reelContentH)
    const reelW = reelContentW * reelScale
    const reelH = reelContentH * reelScale
    this._reelSet.scale.set(reelScale)
    this._reelSet.x = innerX + (innerWidth - reelW) / 2
    this._reelSet.y = innerY + (innerHeight - reelH) / 2

    // Mask — clips to actual reel content area
    this._mask.clear()
    this._mask.rect(
      innerX + (innerWidth - reelW) / 2,
      innerY + (innerHeight - reelH) / 2,
      reelW,
      reelH,
    )
    this._mask.fill(0xffffff)

    this._overlay.resize(layout)
    this._featureScene.resize(layout)
    this._pickUI.resize(layout)
  }

  async resumeFeatures(): Promise<void> {
    if (this._pendingPickBonusState) {
      this._pickUI.show()
      this._pickUI.restoreState(
        this._pendingPickBonusState.userPicks,
        this._pendingPickBonusState.revealedValues,
      )
      await this._runPickBonus()
    }
  }

  destroy(): void {
    this._reelSet.destroy({ children: true })
    this._overlay.destroy({ children: true })
    this._featureScene?.destroy({ children: true })
    this._paylineTrail?.destroy({ children: true })
    this._pickUI.destroy({ children: true })
    this._buyBonusControl.destroy({ children: true })
    this._background.destroy()
    this._mask.destroy()
    this._boardPlate.destroy()
    this._frame.destroy()
  }

  syncSpinSpeed(profile: SpinSpeedProfile): void {
    this._spinSpeedProfile = profile
    this._reelSet.syncSpinSpeed(profile)
    this._overlay.syncSpinSpeed(profile)
  }

  // ── Private presentation helpers ───────────────────────────────────────────

  private async _presentBase(result: WoodlandWhisperBaseResult): Promise<void> {
    this._ctx.eventBus.emit('spin:started', {})
    this._reelSet.spin()
    await this._wait(this._spinSpeedProfile.reelSpinMs)

    const transposed = transposeGrid(result.grid)
    const stopPromises = transposed.map(async (symbols, i) => {
      await this._reelSet.stopReel(i, symbols)
      this._ctx.eventBus.emit('reel:stopped', { reelIndex: i, isLast: i === transposed.length - 1 })
    })
    await Promise.all(stopPromises)

    if (result.win > 0) {
      this._ctx.eventBus.emit('win:awarded', { amount: result.win, multiplierX: 0 })
    }
    if (result.win > 0 || result.sc >= 2) {
      await this._showWinAnimation(result.hits, transposed, result.win)
    }

    if (result.triggeredPickBonus) {
      await this._featureScene.play(
        'FOLLOW THE LIGHT',
        'Find two matching numbers',
        this._featureSources(transposed),
      )
      await this._runPickBonus()
    }

    if (result.win > 0) {
      await this._overlay.announceWin(result.win, this._ctx.session.lastWager)
    }
  }

  private async _presentBuy(result: WoodlandWhisperBuyResult): Promise<void> {
    this._ctx.eventBus.emit('spin:started', {})
    this._reelSet.spin()
    await this._wait(this._spinSpeedProfile.reelSpinMs)

    const transposed = transposeGrid(result.grid)
    const stopPromises = transposed.map(async (symbols, i) => {
      await this._reelSet.stopReel(i, symbols)
      this._ctx.eventBus.emit('reel:stopped', { reelIndex: i, isLast: i === transposed.length - 1 })
    })
    await Promise.all(stopPromises)

    if (result.win > 0) {
      this._ctx.eventBus.emit('win:awarded', { amount: result.win, multiplierX: 0 })
    }
    if (result.win > 0 || result.sc >= 2) {
      await this._showWinAnimation(result.hits, transposed, result.win)
    }

    this._ctx.eventBus.emit('feature:announced', { type: 'bonus' })
    await this._featureScene.play(
      'FOLLOW THE LIGHT',
      'Find two matching numbers',
      this._featureSources(transposed),
    )
    await this._runPickBonus()
  }

  private async _presentFree(result: WoodlandWhisperFreeResult): Promise<void> {
    this._ctx.eventBus.emit('spin:started', {})
    this._reelSet.spin()
    await this._wait(this._spinSpeedProfile.reelSpinMs)

    const transposed = transposeGrid(result.grid)
    const stopPromises = transposed.map(async (symbols, i) => {
      await this._reelSet.stopReel(i, symbols)
      this._ctx.eventBus.emit('reel:stopped', { reelIndex: i, isLast: i === transposed.length - 1 })
    })
    await Promise.all(stopPromises)

    if (result.win > 0) {
      this._ctx.eventBus.emit('win:awarded', { amount: result.win, multiplierX: 0 })
    }
    if (result.win > 0 || result.sc >= 2) {
      await this._showWinAnimation(result.hits, transposed, result.win)
    }

    if (result.retriggeredPickBonus) {
      this._ctx.eventBus.emit('feature:announced', { type: 'bonus' })
      await this._featureScene.play(
        'FOLLOW THE LIGHT',
        'Find two matching numbers',
        this._featureSources(transposed),
      )
      await this._runPickBonus()
    } else if (this._freeRemaining === 0) {
      await this._featureScene.play(
        'A GIFT FROM THE FOREST',
        `${this._freeTotal.toLocaleString('en-US')} CREDITS WON`,
      )
    }
  }

  private async _presentPick(result: WoodlandWhisperPickResult): Promise<void> {
    this._pickUI.revealCard(result.pick.userIndex, result.pick.value)
    await this._wait(this._spinSpeedProfile.pickRevealMs)

    if (result.pick.isMatch) {
      await this._wait(this._spinSpeedProfile.pickMatchPauseMs)
      this._pickUI.hide()
    }
  }

  private async _runPickBonus(): Promise<void> {
    this._pendingPickBonusState = null
    this._pickUI.show()
    let matched = false

    while (!matched) {
      const userIndex = await new Promise<number>((resolve) => {
        const unsub = this._ctx.eventBus.on('pick-card-selected', ({ index }) => {
          unsub()
          resolve(index)
        })
      })

      const response = await this._ctx.dispatcher.dispatch('pick', { userIndex })
      const pickResult = response.result as WoodlandWhisperPickResult | undefined
      if (!pickResult) break

      const awarded = pickResult.pick.isMatch ? pickResult.pick.value : null
      const nextState = response.state as WoodlandWhisperSerializedState
      this.applyState(nextState)

      this._pickUI.revealCard(pickResult.pick.userIndex, pickResult.pick.value)
      this._ctx.eventBus.emit('pick:card:revealed', {
        index: pickResult.pick.userIndex,
        value: pickResult.pick.value,
      })

      if (pickResult.pick.isMatch) {
        matched = true
        await this._wait(this._spinSpeedProfile.pickMatchPauseMs)
        this._pickUI.hide()

        if (awarded && awarded > 0) {
          this._ctx.eventBus.emit('feature:announced', { type: 'free-spins' })
          await this._featureScene.play('THE FOREST AWAKENS', `${awarded} FREE SPINS · ALL WINS ×2`)
          this._ctx.eventBus.emit('free-spins:updated', {
            remaining: nextState.freeSpins?.spinsRemaining ?? 0,
            awarded,
          })
        }
      } else {
        await this._wait(this._spinSpeedProfile.pickMissPauseMs)
      }
    }
  }

  private _featureSources(grid: number[][]): FeatureSource[] {
    return this._findScatterCells(grid).map(({ col, row }) => ({
      x: this._reelSet.x + (col + 0.5) * REEL_CONFIG.symbolWidth * this._reelSet.scale.x,
      y: this._reelSet.y + (row + 0.5) * REEL_CONFIG.symbolHeight * this._reelSet.scale.y,
      texture: this._ctx.assets.getTexture('COIN'),
    }))
  }

  private async _showWinAnimation(
    hits: Array<{ lineIndex: number; matchCount: number }>,
    transposedGrid: number[][],
    _totalWin: number,
  ): Promise<void> {
    const scatterCells = this._findScatterCells(transposedGrid)

    const highlightScatters = () => {
      for (const { col, row } of scatterCells) {
        this._reelSet.highlightCell(col, row, SCATTER_WIN_COLOR)
      }
    }

    if (hits.length === 0 && scatterCells.length >= 2) {
      highlightScatters()
      await this._wait(this._spinSpeedProfile.scatterHighlightMs)
      this._reelSet.clearAllHighlights()
      return
    }

    const msPerLine = Math.max(
      this._spinSpeedProfile.lineHighlightMinMs,
      Math.min(
        this._spinSpeedProfile.lineHighlightMaxMs,
        this._spinSpeedProfile.lineHighlightBudgetMs / Math.max(hits.length, 1),
      ),
    )

    for (const hit of hits) {
      this._reelSet.clearAllHighlights()
      highlightScatters()

      for (let col = 0; col < hit.matchCount; col++) {
        const row = PAYLINE_DATA[hit.lineIndex * 5 + col]
        if (row !== undefined) {
          this._reelSet.highlightCell(col, row, PAYLINE_WIN_COLOR)
        }
      }

      const points = Array.from({ length: hit.matchCount }, (_, col) => ({
        x:
          this._reelSet.x +
          (col * (REEL_CONFIG.symbolWidth + 0) + REEL_CONFIG.symbolWidth / 2) *
            this._reelSet.scale.x,
        y:
          this._reelSet.y +
          (PAYLINE_DATA[hit.lineIndex * 5 + col]! + 0.5) *
            REEL_CONFIG.symbolHeight *
            this._reelSet.scale.y,
      }))
      await this._paylineTrail.play(points, msPerLine, this._presentingFree)
    }

    this._reelSet.clearAllHighlights()
  }

  private _findScatterCells(transposedGrid: number[][]): { col: number; row: number }[] {
    const cells: { col: number; row: number }[] = []
    for (let col = 0; col < transposedGrid.length; col++) {
      const reelSyms = transposedGrid[col]!
      for (let row = 0; row < reelSyms.length; row++) {
        if (reelSyms[row] === SCATTER_ID) cells.push({ col, row })
      }
    }
    return cells
  }

  private _wait(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms))
  }
}
