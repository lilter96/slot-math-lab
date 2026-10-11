import { Container, Sprite, Texture, BlurFilter, Graphics } from 'pixi.js'
import { gsap } from 'gsap'

export class SymbolView extends Container {
  private _displayWidth = 140
  private _displayHeight = 140
  private _sprite: Sprite
  private _blurFilter: BlurFilter
  private _highlightGfx: Graphics
  private _multiplierContainer: Container

  constructor() {
    super()
    this._sprite = new Sprite()
    this._sprite.anchor.set(0.5)
    this.addChild(this._sprite)

    this._blurFilter = new BlurFilter()
    this._blurFilter.strengthX = 0
    this._blurFilter.strengthY = 0
    this._sprite.filters = [this._blurFilter]

    this._highlightGfx = new Graphics()
    this._highlightGfx.visible = false
    this.addChild(this._highlightGfx)

    this._multiplierContainer = new Container()
    this.addChild(this._multiplierContainer)
  }

  public setTexture(texture: Texture) {
    this._sprite.texture = texture
    this._fitTexture()
  }

  public setBlur(amount: number) {
    this._blurFilter.strengthY = amount
    this._sprite.filters = amount > 0 ? [this._blurFilter] : []
  }

  public override setSize(width: number, height: number) {
    const cx = width / 2
    const cy = height / 2
    this._displayWidth = width
    this._displayHeight = height
    this._fitTexture()
    this._sprite.x = cx
    this._sprite.y = cy
    this._multiplierContainer.x = cx
    this._multiplierContainer.y = cy
  }

  private _fitTexture() {
    gsap.killTweensOf(this._sprite.scale)
    const texture = this._sprite.texture
    const scale = Math.min(
      (this._displayWidth * 0.88) / texture.width,
      (this._displayHeight * 0.88) / texture.height,
    )
    this._sprite.scale.set(scale)
  }

  public get multiplierContainer(): Container {
    return this._multiplierContainer
  }

  public clearMultiplier(): void {
    this._multiplierContainer.removeChildren().forEach((c) => {
      gsap.killTweensOf(c)
      gsap.killTweensOf(c.scale)
      c.destroy({ children: true })
    })
  }

  public highlight(color: number = 0xffd700): void {
    this._fitTexture()
    this._highlightGfx.visible = true
    this._highlightGfx.clear()
    const w = this._sprite.width + 12
    const h = this._sprite.height + 12
    const x = this._sprite.x - w / 2
    const y = this._sprite.y - h / 2
    this._highlightGfx.roundRect(x, y, w, h, 12)
    this._highlightGfx.stroke({ color, width: 2, alpha: 0.9 })
    this._highlightGfx.roundRect(x + 3, y + 3, w - 6, h - 6, 10)
    this._highlightGfx.fill({ color, alpha: 0.08 })
    for (let index = 0; index < 8; index++) {
      const angle = (index * Math.PI) / 4
      const sx = this._sprite.x + Math.cos(angle) * w * 0.47
      const sy = this._sprite.y + Math.sin(angle) * h * 0.47
      this._highlightGfx.circle(sx, sy, index % 2 ? 2 : 3).fill({ color, alpha: 0.95 })
    }
    const scale = this._sprite.scale.x
    gsap.to(this._sprite.scale, {
      x: scale * 1.045,
      y: scale * 1.045,
      duration: 0.3,
      yoyo: true,
      repeat: -1,
      ease: 'sine.inOut',
    })

    gsap.killTweensOf(this._highlightGfx)
    this._highlightGfx.alpha = 1
    gsap.to(this._highlightGfx, {
      alpha: 0.35,
      duration: 0.45,
      yoyo: true,
      repeat: -1,
      ease: 'sine.inOut',
    })
  }

  public clearHighlight(): void {
    this._fitTexture()
    gsap.killTweensOf(this._highlightGfx)
    gsap.killTweensOf(this._sprite.scale)
    this._highlightGfx.clear()
    this._highlightGfx.visible = false
    this._highlightGfx.alpha = 1
  }

  override destroy(options?: {
    children?: boolean
    texture?: boolean
    baseTexture?: boolean
  }): void {
    gsap.killTweensOf(this)
    gsap.killTweensOf(this.scale)
    gsap.killTweensOf(this._sprite.scale)
    gsap.killTweensOf(this._highlightGfx)
    this.clearMultiplier()
    super.destroy(options)
  }
}
