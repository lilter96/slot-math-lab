import { Container, Graphics, Sprite, Text } from 'pixi.js'
import { gsap } from 'gsap'
import type { GameAssets } from '../../engine/asset-registry.js'
import type { UILayoutSnapshot } from '../../engine/layout.js'
import { S300Mascot } from './mascot/index.js'

/** Combat operation entry: full-scale deployment, incoming aircraft and real award. */
export class OperationBriefing extends Container {
  private readonly _shade = new Graphics()
  private readonly _world = new Container()
  private _timeline?: gsap.core.Timeline
  private _resolve?: () => void

  constructor(private readonly _assets: GameAssets) {
    super()
    this.visible = false
    this.eventMode = 'static'
    this.addChild(this._shade, this._world)
  }

  resize(layout: UILayoutSnapshot): void {
    this._shade
      .clear()
      .rect(0, 52, layout.screenWidth, layout.screenHeight - 52)
      .fill({ color: 0x020c13, alpha: 0.93 })
    this._world.scale.set(
      Math.min((layout.screenWidth - 24) / 1000, (layout.screenHeight - 70) / 760),
    )
    this._world.position.set(layout.screenWidth / 2, 52 + (layout.screenHeight - 52) * 0.45)
  }

  async play(awarded: number, retrigger = false, summary = false): Promise<void> {
    if (this.destroyed) return
    this._stop()
    this._world.removeChildren().forEach((child) => child.destroy({ children: true }))
    this.visible = true
    this.alpha = 0
    const world = this._world
    const grid = new Graphics()
    for (let x = -460; x <= 460; x += 40)
      grid.moveTo(x, -320).lineTo(x, 220).stroke({ color: 0x58a8a0, width: 1, alpha: 0.09 })
    for (let y = -320; y <= 220; y += 40)
      grid.moveTo(-460, y).lineTo(460, y).stroke({ color: 0x58a8a0, width: 1, alpha: 0.09 })
    world.addChild(grid)
    const truck = new S300Mascot(this._assets)
    truck.scale.set(0.72)
    truck.position.set(-315, -280)
    world.addChild(truck)
    const heading = new Text({
      text: summary
        ? 'MISSION COMPLETE'
        : retrigger
          ? 'REINFORCEMENTS INBOUND'
          : 'COMBAT OPERATION',
      style: {
        fontFamily: 'Arial',
        fontSize: 42,
        fontWeight: '800',
        letterSpacing: 4,
        fill: '#ffdc91',
        align: 'center',
        wordWrap: true,
        wordWrapWidth: 900,
      },
    })
    heading.anchor.set(0.5)
    heading.y = -310
    const award = new Text({
      text: summary ? `${awarded.toLocaleString('en-US')} CREDITS` : `${awarded} FREE SPINS`,
      style: {
        fontFamily: 'Arial',
        fontSize: 66,
        fontWeight: '800',
        fill: '#fff0c6',
        letterSpacing: 3,
      },
    })
    award.anchor.set(0.5)
    award.y = 255
    award.alpha = 0
    const subtitle = new Text({
      text: summary
        ? 'OPERATION CLOSED · RETURN TO BASE'
        : 'ARMED REELS · ACCUMULATING MULTIPLIERS',
      style: { fontFamily: 'Arial', fontSize: 20, fill: '#9dd3c9', letterSpacing: 2 },
    })
    subtitle.anchor.set(0.5)
    subtitle.y = 330
    subtitle.alpha = 0
    world.addChild(heading, award, subtitle)
    const planes = (summary ? [] : [0, 1, 2]).map((index) => {
      const plane = new Sprite(this._assets.getTexture('COMBAT_PLANE'))
      plane.anchor.set(0.5)
      plane.width = 160 - index * 18
      plane.height = (plane.width * plane.texture.height) / plane.texture.width
      plane.position.set(-680 - index * 120, -225 + index * 38)
      world.addChild(plane)
      return plane
    })
    // The operation is already awarded. Aircraft establish the setting;
    // actual interceptions and multiplier hits are shown later on the real grid.
    const deployment = summary ? Promise.resolve() : truck.triggerS300Feature()
    await new Promise<void>((resolve) => {
      this._resolve = resolve
      this._timeline = gsap
        .timeline({ onComplete: () => this._stop() })
        .to(this, { alpha: 1, duration: 0.35 }, 0)
        .from(heading, { y: -335, alpha: 0, duration: 0.6, ease: 'power2.out' }, 0.2)
        .to(award, { alpha: 1, duration: 0.4 }, 2.4)
        .to(subtitle, { alpha: 1, duration: 0.4 }, 2.6)
        .to(this, { alpha: 0, duration: 0.45 }, 4.0)
      planes.forEach((plane, index) =>
        this._timeline!.to(plane, { x: 680, duration: 2.8, ease: 'none' }, 0.25 + index * 0.2),
      )
    })
    // Deployment completes before the timeline's exit; destruction also cancels
    // it when the player leaves the game during this scene.
    if (!truck.destroyed) {
      await deployment
      truck.destroy({ children: true })
    }
  }

  private _stop(): void {
    this._timeline?.kill()
    this._timeline = undefined
    this.visible = false
    const resolve = this._resolve
    this._resolve = undefined
    resolve?.()
  }

  override destroy(options?: { children?: boolean }): void {
    this._stop()
    super.destroy(options)
  }
}
