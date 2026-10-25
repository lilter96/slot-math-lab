import { PaylineTrail } from '../../engine/payline-trail.js'
import { FeatureScene, type FeatureSource } from '../../engine/feature-scene.js'
import featureConfig from '@tgslots/ancient-dragon/config'
import { Graphics, Sprite } from 'pixi.js'
import type { Texture } from 'pixi.js'
import type { GameRuntime, GameUIContext } from '../../engine/game-client.js'
import type { UILayoutSnapshot } from '../../engine/layout.js'
import { ReelSet } from '../../engine/reel-set.js'
import { WinOverlay } from '../../engine/win-overlay.js'
import { PAYLINE_DATA, Symbols } from '@tgslots/ancient-dragon/constants'
import type {
  AncientDragonBaseResult,
  AncientDragonFreeResult,
  AncientDragonResult,
} from '@tgslots/ancient-dragon'
import type { AncientDragonSerializedState } from '@tgslots/shared-contracts/states'
import type { ActionType } from '@tgslots/shared-contracts'
import { manifest } from './manifest.js'
import { getSpinSpeedProfile } from '../../engine/spin-speed.js'
import type { SpinSpeedProfile } from '../../engine/spin-speed.js'

const FRAME_PAD = 4
const PAYLINE_WIN_COLOR = 0xffd700
const SCATTER_WIN_COLOR = 0xff4444
const SCATTER_ID = Symbols['YINYANG']!

const GRID_CONFIG = {
  reels: manifest.grid.reels,
  rows: manifest.grid.rows,
  reelSpacing: 20,
}
const REEL_CONFIG = {
  symbolWidth: manifest.symbolSize,
  symbolHeight: manifest.symbolSize,
  visibleSymbols: manifest.grid.rows,
  totalSymbols: 5,
}

export class AncientDragonRuntime implements GameRuntime<'ancient-dragon'> {
  private _ctx!: GameUIContext<'ancient-dragon'>
  private _reelSet!: ReelSet
  private _featureScene!: FeatureScene
  private _paylineTrail!: PaylineTrail
  private _freeRemaining = 0
  private _freeTotal = 0
  private _overlay!: WinOverlay
  private _bgSprite!: Sprite
  private _bgTex!: Texture
  private _boardPlate!: Graphics
  private _mask!: Graphics
  private _frame!: Graphics
  private _layout?: UILayoutSnapshot
  private _spinSpeedProfile: SpinSpeedProfile = getSpinSpeedProfile('normal')

  async init(ctx: GameUIContext<'ancient-dragon'>): Promise<void> {
    this._ctx = ctx
    ctx.sound.playBGM('bgm-dragon')

    this._bgTex = ctx.assets.getTexture('BACKGROUND_16_9')
    this._bgSprite = new Sprite(this._bgTex)
    this._bgSprite.anchor.set(0.5)
    ctx.scene.background.addChild(this._bgSprite)

    const emptyGrid = Array.from({ length: GRID_CONFIG.reels }, (_col, col) =>
      Array.from({ length: GRID_CONFIG.rows }, (_row, row) => 1 + ((col * 3 + row) % 11)),
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

    this._frame = new Graphics()
    ctx.scene.reels.addChild(this._frame)

    this._overlay = new WinOverlay()
    this._overlay.setGame(ctx.assets, [...manifest.winTiers])
    ctx.scene.overlays.addChild(this._overlay)
    this._paylineTrail = new PaylineTrail('dragon')
    ctx.scene.overlays.addChild(this._paylineTrail)
    this._featureScene = new FeatureScene(ctx.assets, 'dragon')
    ctx.scene.overlays.addChild(this._featureScene)
  }

  applyState(state: AncientDragonSerializedState): void {
    const remaining = state.freeSpins?.spinsRemaining ?? 0
    this._freeRemaining = remaining
    this._freeTotal = state.freeSpins?.totalWin ?? 0
    this._ctx.eventBus.emit('free-spins:updated', { remaining })
  }

  async presentResult(
    _action: ActionType<'ancient-dragon'>,
    result: AncientDragonResult,
  ): Promise<void> {
    switch (result.type) {
      case 'BASE':
        await this._presentBase(result)
        break
      case 'FREE':
        await this._presentFree(result)
        break
    }
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
    this._mask.rect(
      layout.reelBounds.x,
      layout.reelBounds.y,
      layout.reelBounds.width,
      layout.reelBounds.height,
    )
    this._mask.fill(0xffffff)

    this._frame.clear()
    this._frame.roundRect(
      layout.reelBounds.x - FRAME_PAD,
      layout.reelBounds.y - FRAME_PAD,
      layout.reelBounds.width + FRAME_PAD * 2,
      layout.reelBounds.height + FRAME_PAD * 2,
      12,
    )
    this._frame.stroke({ color: 0xe4c283, width: 3, alpha: 0.95 })

    for (let col = 1; col < GRID_CONFIG.reels; col++) {
      const sepX =
        layout.reelBounds.x +
        (col * (REEL_CONFIG.symbolWidth + GRID_CONFIG.reelSpacing) - GRID_CONFIG.reelSpacing / 2) *
          reelScale
      this._frame.moveTo(sepX, layout.reelBounds.y)
      this._frame.lineTo(sepX, layout.reelBounds.y + layout.reelBounds.height)
    }
    this._frame.stroke({ color: 0xe4c283, width: 1, alpha: 0.15 })

    for (let row = 1; row < REEL_CONFIG.visibleSymbols; row++) {
      const sepY = layout.reelBounds.y + row * REEL_CONFIG.symbolHeight * reelScale
      this._frame.moveTo(layout.reelBounds.x, sepY)
      this._frame.lineTo(layout.reelBounds.x + layout.reelBounds.width, sepY)
    }
    this._frame.stroke({ color: 0xe4c283, width: 1, alpha: 0.15 })

    this._overlay.resize(layout)
    this._featureScene.resize(layout)
  }

  destroy(): void {
    this._reelSet.destroy({ children: true })
    this._overlay.destroy({ children: true })
    this._featureScene?.destroy({ children: true })
    this._paylineTrail?.destroy({ children: true })
    this._bgSprite.destroy()
    this._boardPlate.destroy()
    this._mask.destroy()
    this._frame.destroy()
  }

  syncSpinSpeed(profile: SpinSpeedProfile): void {
    this._spinSpeedProfile = profile
    this._reelSet.syncSpinSpeed(profile)
    this._overlay.syncSpinSpeed(profile)
  }

  private async _presentBase(result: AncientDragonBaseResult): Promise<void> {
    this._ctx.eventBus.emit('spin:started', {})
    this._reelSet.spin()
    await this._wait(this._spinSpeedProfile.reelSpinMs)

    await Promise.all(
      result.grid.map(async (symbols, index) => {
        await this._reelSet.stopReel(index, symbols)
        this._ctx.eventBus.emit('reel:stopped', {
          reelIndex: index,
          isLast: index === result.grid.length - 1,
        })
      }),
    )

    if (result.win > 0) {
      this._ctx.eventBus.emit('win:awarded', { amount: result.win, multiplierX: 0 })
    }
    if (result.win > 0 || result.sc >= 2) {
      await this._showWinAnimation(result.hits, result.grid)
    }

    if (result.triggeredFreeSpins) {
      this._ctx.sound.playSFX('feature-rise')
      await this._featureScene.play(
        'GUARDIAN AWAKENED',
        `${featureConfig.feature.free_spins_per_trigger} FREE SPINS`,
        this._featureSources(result.grid),
      )
    }

    if (result.win > 0) {
      await this._overlay.announceWin(result.win, this._ctx.session.lastWager)
    }
  }

  private async _presentFree(result: AncientDragonFreeResult): Promise<void> {
    this._ctx.eventBus.emit('spin:started', {})
    this._reelSet.spin()
    await this._wait(this._spinSpeedProfile.reelSpinMs)

    await Promise.all(
      result.grid.map(async (symbols, index) => {
        await this._reelSet.stopReel(index, symbols)
        this._ctx.eventBus.emit('reel:stopped', {
          reelIndex: index,
          isLast: index === result.grid.length - 1,
        })
      }),
    )

    if (result.win > 0) {
      this._ctx.eventBus.emit('win:awarded', { amount: result.win, multiplierX: 0 })
    }
    if (result.win > 0 || result.sc >= 2) {
      await this._showWinAnimation(result.hits, result.grid)
    }

    if (result.retriggeredFreeSpins) {
      this._ctx.sound.playSFX('feature-rise')
      await this._featureScene.play(
        'THE DRAGON RETURNS',
        `+${featureConfig.feature.free_spins_per_trigger} FREE SPINS`,
        this._featureSources(result.grid),
      )
    } else if (this._freeRemaining === 0) {
      await this._featureScene.play(
        'SHRINE REWARD',
        `${this._freeTotal.toLocaleString('en-US')} CREDITS WON`,
      )
    }
  }

  private _featureSources(grid: number[][]): FeatureSource[] {
    return this._findScatterCells(grid).map(({ col, row }) => ({
      x:
        this._reelSet.x +
        (col * (REEL_CONFIG.symbolWidth + GRID_CONFIG.reelSpacing) + REEL_CONFIG.symbolWidth / 2) *
          this._reelSet.scale.x,
      y: this._reelSet.y + (row + 0.5) * REEL_CONFIG.symbolHeight * this._reelSet.scale.y,
      texture: this._ctx.assets.getTexture('YINYANG'),
    }))
  }

  private async _showWinAnimation(
    hits: readonly { lineIndex: number; matchCount: number }[],
    transposedGrid: number[][],
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
          (col * (REEL_CONFIG.symbolWidth + 20) + REEL_CONFIG.symbolWidth / 2) *
            this._reelSet.scale.x,
        y:
          this._reelSet.y +
          (PAYLINE_DATA[hit.lineIndex * 5 + col]! + 0.5) *
            REEL_CONFIG.symbolHeight *
            this._reelSet.scale.y,
      }))
      await this._paylineTrail.play(points, msPerLine, false)
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
