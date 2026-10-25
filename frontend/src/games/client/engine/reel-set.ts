import { Container } from 'pixi.js'
import { Reel } from './reel'
import type { UIGridConfig, UIReelConfig } from '../types'
import type { GameAssets } from './asset-registry'
import { getSpinSpeedProfile } from './spin-speed.js'
import type { SpinSpeedProfile } from './spin-speed.js'

export class ReelSet extends Container {
  private _reels: Reel[] = []
  private _gridConfig: UIGridConfig
  private _reelConfig: UIReelConfig
  private _assets: GameAssets
  private _speedProfile: SpinSpeedProfile = getSpinSpeedProfile('normal')

  constructor(
    gridConfig: UIGridConfig,
    reelConfig: UIReelConfig,
    initialGrid: number[][],
    assets: GameAssets,
    symbolCount: number,
  ) {
    super()
    this._gridConfig = gridConfig
    this._reelConfig = reelConfig
    this._assets = assets
    this._init(initialGrid, assets, symbolCount)
  }

  private _init(initialGrid: number[][], assets: GameAssets, symbolCount: number) {
    for (let i = 0; i < this._gridConfig.reels; i++) {
      const initialSymbols = initialGrid[i] || []
      const reel = new Reel(this._reelConfig, initialSymbols, assets, symbolCount)
      reel.x = i * (this._reelConfig.symbolWidth + this._gridConfig.reelSpacing)
      this._reels.push(reel)
      this.addChild(reel)
    }
  }

  public async spin(held?: ReadonlySet<number>): Promise<void> {
    const promises = this._reels.map((reel, i) => {
      // Held reels (sticky full-row wilds) stay locked — they don't re-spin.
      if (held?.has(i)) return Promise.resolve()
      return new Promise<void>((resolve) => {
        setTimeout(async () => {
          await reel.spin()
          resolve()
        }, i * this._speedProfile.reelStartStaggerMs)
      })
    })
    await Promise.all(promises)
  }

  public async stop(
    finalGrid: number[][],
    held?: ReadonlySet<number>,
    onStopped?: (index: number) => void,
  ): Promise<void> {
    const promises = this._reels.map((reel, i) => {
      if (held?.has(i)) {
        reel.setSymbols(finalGrid[i]!)
        return Promise.resolve()
      }
      return this.stopReel(i, finalGrid[i]!).then(() => onStopped?.(i))
    })
    await Promise.all(promises)
  }

  public async stopReel(index: number, symbols: number[]): Promise<void> {
    const reel = this._reels[index]
    if (!reel) return
    await new Promise<void>((resolve) => {
      setTimeout(async () => {
        await reel.stop(symbols)
        resolve()
      }, index * this._speedProfile.reelStopStaggerMs)
    })
  }

  get isSpinning(): boolean {
    return this._reels.some((r) => r.isSpinning)
  }

  get gridConfig(): UIGridConfig {
    return this._gridConfig
  }

  get reelConfig(): UIReelConfig {
    return this._reelConfig
  }

  public getReel(index: number): Reel {
    return this._reels[index]!
  }

  public clearAllMultipliers(): void {
    this._reels.forEach((r) => r.clearMultipliers())
  }

  public highlightCell(col: number, row: number, color: number): void {
    this._reels[col]?.getSymbolAt(row)?.highlight(color)
  }

  public clearAllHighlights(): void {
    for (const reel of this._reels) {
      for (let row = 0; row < this._reelConfig.visibleSymbols; row++) {
        reel.getSymbolAt(row)?.clearHighlight()
      }
    }
  }

  public setSymbols(grid: number[][]): void {
    this._reels.forEach((reel, colIndex) => {
      const symbols = grid[colIndex]
      if (symbols) reel.setSymbols(symbols)
    })
  }

  public setSymbolAt(reelIdx: number, rowIdx: number, symbolId: number): void {
    const reel = this._reels[reelIdx]
    if (reel) {
      const symbol = reel.getSymbolAt(rowIdx)
      if (symbol) {
        symbol.setTexture(this._assets.getSymbolTextureSafe(symbolId, 0))
      }
    }
  }

  /**
   * Run cascade animation across all reels simultaneously.
   * vanishedPositions: encoded as reel * rowCount + row (from backend CombatCascadeStep).
   * newGrid: column-major [reel][row] — the grid after gravity+refill (next step's preCombatGrid).
   */
  public async cascadeGrid(
    vanishedPositions: readonly number[],
    newGrid: number[][],
    rowCount: number,
  ): Promise<void> {
    await Promise.all(
      this._reels.map((reel, reelIdx) => {
        const vanishedRows = vanishedPositions
          .filter((pos) => Math.floor(pos / rowCount) === reelIdx)
          .map((pos) => pos % rowCount)
        return reel.cascade(vanishedRows, newGrid[reelIdx] ?? [])
      }),
    )
  }

  public syncSpinSpeed(profile: SpinSpeedProfile): void {
    this._speedProfile = profile
    for (const reel of this._reels) {
      reel.syncSpinSpeed(profile)
    }
  }

  override destroy(options?: {
    children?: boolean
    texture?: boolean
    baseTexture?: boolean
  }): void {
    this._reels.forEach((r) => r.destroy({ children: true }))
    super.destroy(options)
  }
}
