import { Container, Graphics, Sprite, Text } from 'pixi.js'
import { gsap } from 'gsap'
import type { GameAssets } from '../../../engine/asset-registry.js'
import type { CombatLayout } from './combat-layout.js'

/** A single five-row symbol, rather than five visually independent WILDs. */
export class GiantWildView extends Container {
  private readonly _columns = new Map<number, Container>()
  private readonly _highlighted = new Set<number>()
  private _layout: CombatLayout | null = null

  constructor(private readonly _assets: GameAssets) {
    super()
    this.eventMode = 'none'
  }

  setLayout(layout: CombatLayout): void {
    this._layout = layout
    this._redraw()
  }

  setReels(reels: Iterable<number>): void {
    const wanted = new Set(reels)
    for (const [reel, column] of this._columns) {
      if (wanted.has(reel)) continue
      gsap.killTweensOf(column)
      column.destroy({ children: true })
      this._columns.delete(reel)
    }
    for (const reel of wanted) {
      if (this._columns.has(reel)) continue
      const column = new Container()
      this._columns.set(reel, column)
      this.addChild(column)
    }
    this._redraw()
  }

  highlight(reels: Iterable<number>): void {
    this._highlighted.clear()
    for (const reel of reels) this._highlighted.add(reel)
    this._redraw()
  }

  private _redraw(): void {
    if (!this._layout) return
    const { symbolWidth: width, reelSpacing: gap, totalHeight: height } = this._layout
    if (!width || !height) return
    for (const [reel, column] of this._columns) {
      for (const child of column.removeChildren()) child.destroy({ children: true })
      column.position.set(reel * (width + gap), 0)
      const active = this._highlighted.has(reel)
      const plate = new Graphics()
      plate.roundRect(3, 3, width - 6, height - 6, 10).fill({ color: 0x10282b })
      plate.roundRect(6, 6, width - 12, height - 12, 8).stroke({
        color: active ? 0xffdb88 : 0x83cbb6,
        width: active ? 3 : 1.5,
        alpha: active ? 0.95 : 0.65,
      })
      // Layered metal panels keep the silhouette tall while preserving the art's aspect ratio.
      for (let row = 0; row < 5; row++) {
        const y = 18 + (row * (height - 36)) / 5
        plate
          .moveTo(13, y)
          .lineTo(width - 13, y)
          .stroke({ color: 0x335d59, width: 1, alpha: 0.35 })
      }
      plate
        .roundRect(width * 0.18, height * 0.33, width * 0.64, height * 0.34, width * 0.25)
        .fill({ color: active ? 0x625133 : 0x294a43, alpha: 0.55 })
      const emblem = new Sprite(this._assets.getTexture('WILD'))
      emblem.anchor.set(0.5)
      const size = width * 0.9
      const scale = size / Math.max(emblem.texture.width, emblem.texture.height)
      emblem.scale.set(scale)
      emblem.position.set(width / 2, height * 0.48)
      const label = new Text({
        text: 'WILD',
        style: {
          fontFamily: 'Arial, sans-serif',
          fontSize: width * 0.19,
          fontWeight: '900',
          fill: '#f7dda2',
          letterSpacing: 1,
        },
      })
      label.anchor.set(0.5)
      label.position.set(width / 2, height * 0.64)
      const lock = new Text({
        text: 'LOCKED',
        style: {
          fontFamily: 'Arial, sans-serif',
          fontSize: width * 0.16,
          fontWeight: '700',
          fill: '#abe6cf',
          letterSpacing: 1,
        },
      })
      lock.anchor.set(0.5)
      lock.position.set(width / 2, height * 0.12)
      const count = new Text({
        text: '1 SYMBOL',
        style: {
          fontFamily: 'Arial, sans-serif',
          fontSize: width * 0.15,
          fontWeight: '700',
          fill: '#a3b7ac',
          letterSpacing: 0.5,
        },
      })
      count.anchor.set(0.5)
      count.position.set(width / 2, height * 0.88)
      column.addChild(plate, emblem, label, lock, count)
    }
  }
}
