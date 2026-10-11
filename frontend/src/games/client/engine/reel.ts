import { Container, Ticker } from 'pixi.js'
import type { UIReelConfig } from '../types'
import { gsap } from 'gsap'
import { SymbolView } from './symbol-view'
import type { GameAssets } from './asset-registry'
import { getSpinSpeedProfile } from './spin-speed.js'
import type { SpinSpeedProfile } from './spin-speed.js'

export class Reel extends Container {
  private _symbols: SymbolView[] = []
  private _config: UIReelConfig
  private _assets: GameAssets
  private _symbolCount: number
  private _spinning = false
  private _scrollY = 0
  private _spinTicker?: (ticker: Ticker) => void
  private _speedProfile: SpinSpeedProfile = getSpinSpeedProfile('normal')

  constructor(
    config: UIReelConfig,
    initialSymbolIds: number[],
    assets: GameAssets,
    symbolCount: number,
  ) {
    super()
    this._config = config
    this._assets = assets
    this._symbolCount = symbolCount
    this._init(initialSymbolIds)
  }

  private _init(initialSymbolIds: number[]) {
    const { totalSymbols, symbolHeight, symbolWidth } = this._config

    for (let i = 0; i < totalSymbols; i++) {
      const symbolView = new SymbolView()
      symbolView.y = i * symbolHeight
      symbolView.setSize(symbolWidth, symbolHeight)

      const symbolId = initialSymbolIds[i % initialSymbolIds.length]
      if (symbolId !== undefined) {
        symbolView.setTexture(this._assets.getSymbolTextureSafe(symbolId, 0))
      }

      this._symbols.push(symbolView)
      this.addChild(symbolView)
    }
  }

  public async spin(): Promise<void> {
    if (this._spinning) return
    this._spinning = true
    this._scrollY = 0
    // A new spin replaces every symbol on this reel. Surviving cascade badges
    // belong to the previous result and must never enter the scrolling pool.
    this.clearMultipliers()

    const { symbolHeight, totalSymbols } = this._config
    const reelHeight = totalSymbols * symbolHeight
    this._symbols.forEach((s) => s.setBlur(10))

    this._spinTicker = (ticker: Ticker) => {
      const delta = ticker.deltaMS / 1000
      const spinSpeed = symbolHeight * 15 * this._speedProfile.reelVelocityMultiplier
      this._scrollY += spinSpeed * delta

      for (let i = 0; i < this._symbols.length; i++) {
        const symbol = this._symbols[i]!
        const prevY = symbol.y
        const newY = (i * symbolHeight + this._scrollY) % reelHeight
        symbol.y = newY

        if (newY < prevY - symbolHeight / 2) {
          const randomId = Math.floor(Math.random() * this._symbolCount)
          symbol.setTexture(this._assets.getSymbolTextureSafe(randomId, 0))
        }
      }
    }

    Ticker.shared.add(this._spinTicker)
  }

  public async stop(finalSymbols: number[]): Promise<void> {
    if (!this._spinning) return

    if (this._spinTicker) {
      Ticker.shared.remove(this._spinTicker)
      this._spinTicker = undefined
    }

    const { symbolHeight } = this._config

    this._symbols.forEach((s) => s.setBlur(0))

    this._symbols.forEach((symbol, i) => {
      gsap.killTweensOf(symbol)
      const symbolId = finalSymbols[i]
      if (symbolId !== undefined) {
        symbol.setTexture(this._assets.getSymbolTextureSafe(symbolId, 0))
      }
      symbol.y = i * symbolHeight - symbolHeight * 0.25
    })

    return new Promise((resolve) => {
      let completed = 0
      this._symbols.forEach((symbol, i) => {
        gsap.to(symbol, {
          y: i * symbolHeight,
          duration: this._speedProfile.reelSettleDurationMs / 1000,
          ease: 'back.out(1.7)',
          onComplete: () => {
            completed++
            if (completed === this._symbols.length) {
              this._spinning = false
              resolve()
            }
          },
        })
      })
    })
  }

  get isSpinning(): boolean {
    return this._spinning
  }

  get config(): UIReelConfig {
    return this._config
  }

  public getSymbolAt(row: number): SymbolView | null {
    return this._symbols[row] ?? null
  }

  public clearMultipliers(): void {
    this._symbols.forEach((s) => s.clearMultiplier())
  }

  public setSymbols(symbols: number[]): void {
    const { symbolHeight } = this._config
    this._symbols.forEach((symbol, i) => {
      gsap.killTweensOf(symbol)
      gsap.killTweensOf(symbol.scale)
      const symbolId = symbols[i % symbols.length]
      if (symbolId !== undefined) {
        symbol.setTexture(this._assets.getSymbolTextureSafe(symbolId, 0))
      }
      symbol.y = i * symbolHeight
      symbol.alpha = 1
      symbol.scale.set(1)
      symbol.setBlur(0)
    })
    this._spinning = false
  }

  /**
   * Cascade animation: fade out vanished rows, then animate survivors sliding down and
   * new symbols falling in from above. Ends with a setSymbols() to guarantee clean state.
   */
  public async cascade(vanishedRows: number[], newSymbols: number[]): Promise<void> {
    if (vanishedRows.length === 0) {
      this.setSymbols(newSymbols)
      return
    }

    const { symbolHeight, visibleSymbols } = this._config
    const vanishedSet = new Set(vanishedRows)

    // Phase A: fade + shrink the symbols that won
    const vanishTweens: Promise<void>[] = []
    for (const row of vanishedRows) {
      const sym = this._symbols[row]
      if (!sym) continue
      gsap.killTweensOf(sym)
      gsap.killTweensOf(sym.scale)
      vanishTweens.push(
        new Promise<void>((resolve) => {
          gsap.to(sym, { alpha: 0, duration: 0.22, ease: 'power2.in', onComplete: resolve })
          gsap.to(sym.scale, { x: 0.65, y: 0.65, duration: 0.22, ease: 'power2.in' })
        }),
      )
    }
    await Promise.all(vanishTweens)

    // Phase B: compute gravity — survivors compress downward, vacated top rows are new fills
    // survivors[0] = bottom-most survivor oldRow, survivors[1] = next, …
    const survivors: number[] = []
    for (let row = visibleSymbols - 1; row >= 0; row--) {
      if (!vanishedSet.has(row)) survivors.push(row)
    }

    // newToOld[newRow] = oldRow (or -1 for a new fill symbol)
    const newToOld: number[] = new Array(visibleSymbols).fill(-1)
    let si = 0
    for (let newRow = visibleSymbols - 1; newRow >= 0 && si < survivors.length; newRow--) {
      newToOld[newRow] = survivors[si++]!
    }

    // Reusable views are those that were vanished (we'll recycle them for new fills)
    const reusable = vanishedRows.map((r) => this._symbols[r]).filter((s): s is SymbolView => !!s)
    let ri = 0

    const fallItems: { view: SymbolView; fromY: number; toY: number }[] = []

    // Survivors: slide from oldRow → newRow
    for (let newRow = 0; newRow < visibleSymbols; newRow++) {
      const oldRow = newToOld[newRow]
      if (oldRow === undefined || oldRow < 0) continue
      const sym = this._symbols[oldRow]!
      fallItems.push({ view: sym, fromY: oldRow * symbolHeight, toY: newRow * symbolHeight })
    }

    // New fills: stack them above the top, one slot each, then animate down
    const newFillCount = vanishedRows.length
    let fillSlot = 0
    for (let newRow = 0; newRow < visibleSymbols; newRow++) {
      if ((newToOld[newRow] ?? -1) >= 0) continue
      const sym = reusable[ri++]
      if (!sym) continue
      sym.clearMultiplier() // Multiplier only applies to original symbols that survive
      const symbolId = newSymbols[newRow]
      if (symbolId !== undefined) sym.setTexture(this._assets.getSymbolTextureSafe(symbolId, 0))
      sym.alpha = 1
      sym.scale.set(1)
      const fromY = -(newFillCount - fillSlot) * symbolHeight
      sym.y = fromY
      fallItems.push({ view: sym, fromY, toY: newRow * symbolHeight })
      fillSlot++
    }

    // Phase C: animate all views to their final y positions
    await Promise.all(
      fallItems.map(
        ({ view, fromY, toY }) =>
          new Promise<void>((resolve) => {
            view.y = fromY
            gsap.killTweensOf(view)
            gsap.to(view, { y: toY, duration: 0.42, ease: 'bounce.out', onComplete: resolve })
          }),
      ),
    )

    // REORDER: Ensure the instance array matches the new grid positions
    const nextSymbols: SymbolView[] = new Array(visibleSymbols)
    for (let newRow = 0; newRow < visibleSymbols; newRow++) {
      const item = fallItems.find((it) => it.toY === newRow * symbolHeight)
      nextSymbols[newRow] = item?.view ?? this._symbols[newRow]!
    }
    this._symbols = nextSymbols

    // Phase D: lock in clean final state (kills tweens)
    this.setSymbols(newSymbols)
  }

  public syncSpinSpeed(profile: SpinSpeedProfile): void {
    this._speedProfile = profile
  }

  override destroy(options?: {
    children?: boolean
    texture?: boolean
    baseTexture?: boolean
  }): void {
    if (this._spinTicker) {
      Ticker.shared.remove(this._spinTicker)
      this._spinTicker = undefined
    }
    gsap.killTweensOf(this)
    this._symbols.forEach((s) => {
      gsap.killTweensOf(s)
      gsap.killTweensOf(s.scale)
      s.destroy({ children: true })
    })
    super.destroy(options)
  }
}
