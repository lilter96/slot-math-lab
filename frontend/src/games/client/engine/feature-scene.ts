import { Container, Graphics, Sprite, Text, type Texture } from 'pixi.js'
import { gsap } from 'gsap'
import type { GameAssets } from './asset-registry.js'
import type { UILayoutSnapshot } from './layout.js'

export interface FeatureSource {
  x: number
  y: number
  texture: Texture
}

type Theme = 'dragon' | 'forest'

/** A real feature transition, driven only by the awarded game result. */
export class FeatureScene extends Container {
  private _layout?: UILayoutSnapshot
  private _timeline?: gsap.core.Timeline
  private _finish?: () => void
  private readonly _world = new Container()
  private readonly _shade = new Graphics()
  private readonly _color: number

  constructor(
    private readonly _assets: GameAssets,
    private readonly _theme: Theme,
  ) {
    super()
    this._color = _theme === 'dragon' ? 0xffc564 : 0xa9edaa
    this.visible = false
    this.eventMode = 'static'
    this.addChild(this._shade, this._world)
  }

  resize(layout: UILayoutSnapshot): void {
    this._layout = layout
    const { screenWidth: w, screenHeight: h } = layout
    this._shade
      .clear()
      .rect(0, 52, w, h - 52)
      .fill({ color: 0x040d14, alpha: 0.9 })
    this._world.scale.set(Math.min((w - 24) / 760, (h - 70) / 940))
    this._world.position.set(w / 2, 52 + (h - 52) * 0.45)
  }

  async play(title: string, detail: string, sources: readonly FeatureSource[] = []): Promise<void> {
    if (!this._layout || this.destroyed) return
    this._cancel()
    this._world.removeChildren().forEach((child) => child.destroy({ children: true }))
    this.visible = true
    this.alpha = 0
    const world = this._world
    const aura = new Container()
    aura.y = -35
    for (let layer = 0; layer < 12; layer++) {
      aura.addChild(
        new Graphics().circle(0, 0, 280 - layer * 15).fill({
          color: this._theme === 'dragon' ? 0xd07116 : 0x238b6b,
          alpha: 0.015 + layer * 0.003,
        }),
      )
    }
    world.addChild(aura)
    const portal = new Graphics()
    for (let ring = 0; ring < 3; ring++) {
      portal
        .circle(0, -35, 230 + ring * 22)
        .stroke({ color: this._color, width: 2 - ring * 0.4, alpha: 0.55 - ring * 0.12 })
    }
    portal.alpha = 0
    world.addChild(portal)
    const frames = [0, 1, 2, 3].map((index) => this._assets.getTexture(`FEATURE_GUARDIAN_${index}`))
    const hero = new Container()
    const poses = frames.map((texture, index) => {
      const pose = new Sprite(texture)
      pose.anchor.set(0.5)
      pose.height = 510
      pose.width = (pose.height * texture.width) / texture.height
      pose.alpha = index === 0 ? 1 : 0
      hero.addChild(pose)
      return pose
    })
    hero.position.set(this._theme === 'dragon' ? -55 : 45, -35)
    hero.alpha = 0
    world.addChild(hero)
    const top = this._text(
      this._theme === 'dragon' ? 'THE SHRINE COMES ALIVE' : 'THE FOREST HAS CHOSEN YOU',
      19,
      '#c9d8db',
      -350,
    )
    const heading = this._text(title, 54, this._theme === 'dragon' ? '#ffe1a4' : '#e2ffbd', 270)
    const caption = this._text(detail, 28, '#edf5eb', 355)
    top.alpha = heading.alpha = caption.alpha = 0
    world.addChild(top, heading, caption)

    const ringPhase = { angle: 0, radius: 230 }
    const orbit = new Graphics()
    orbit.y = -35
    world.addChildAt(orbit, world.getChildIndex(hero))
    const drawOrbit = () => {
      orbit.clear()
      for (let index = 0; index < 24; index++) {
        const angle = ringPhase.angle + (index * Math.PI * 2) / 24
        const radius = ringPhase.radius + Math.sin(index * 2.4) * 18
        const x = Math.cos(angle) * radius
        const y = Math.sin(angle) * radius * 0.82
        if (this._theme === 'forest') {
          orbit.ellipse(x, y, 4, 10).fill({ color: index % 2 ? 0xffd37b : 0x85d89a, alpha: 0.85 })
        } else {
          orbit.circle(x, y, index % 3 ? 3 : 5).fill({ color: this._color, alpha: 0.9 })
        }
      }
    }
    drawOrbit()
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    return new Promise<void>((resolve) => {
      this._finish = resolve
      const timeline = gsap.timeline({ onComplete: () => this._cancel() })
      this._timeline = timeline
      timeline.timeScale(reduced ? 1.8 : 1)
      timeline.to(this, { alpha: 1, duration: 0.35 }, 0)
      timeline.to(portal, { alpha: 1, duration: 0.7 }, 0.15)
      timeline.fromTo(
        portal.scale,
        { x: 0.3, y: 0.3 },
        { x: 1, y: 1, duration: 1.15, ease: 'power3.out' },
        0.15,
      )
      timeline.to(hero, { alpha: 1, x: 0, duration: 0.9, ease: 'power3.out' }, 0.55)
      for (let index = 1; index < poses.length; index++) {
        const at = 0.55 + index * 0.55
        timeline.to(poses[index - 1]!, { alpha: 0, duration: 0.14 }, at)
        timeline.to(poses[index]!, { alpha: 1, duration: 0.14 }, at)
      }
      timeline.to(top, { alpha: 1, duration: 0.4 }, 0.8)
      timeline.to(
        ringPhase,
        { angle: Math.PI * 1.3, radius: 250, duration: 3.2, ease: 'none', onUpdate: drawOrbit },
        0,
      )

      for (const [index, source] of sources.entries()) {
        const sprite = new Sprite(source.texture)
        sprite.anchor.set(0.5)
        sprite.height = 72
        sprite.width = (72 * source.texture.width) / source.texture.height
        sprite.position.set(
          (source.x - world.x) / world.scale.x,
          (source.y - world.y) / world.scale.y,
        )
        world.addChild(sprite)
        timeline.to(
          sprite,
          { x: 0, y: -40, alpha: 0, duration: 0.9, delay: index * 0.11, ease: 'power2.in' },
          0.1,
        )
        timeline.to(
          sprite.scale,
          {
            x: sprite.scale.x * 0.35,
            y: sprite.scale.y * 0.35,
            duration: 0.9,
            delay: index * 0.11,
          },
          0.1,
        )
      }
      const flash = new Graphics()
        .circle(0, 0, 460)
        .fill({ color: this._color, alpha: 0.08 })
        .stroke({ color: this._color, width: 10, alpha: 0.3 })
      flash.y = -35
      flash.scale.set(0.22)
      flash.alpha = 0
      world.addChild(flash)
      timeline.to(flash, { alpha: 1, duration: 0.12 }, 1.55)
      timeline.to(flash.scale, { x: 1, y: 1, duration: 0.7, ease: 'power3.out' }, 1.55)
      timeline.to(flash, { alpha: 0, duration: 0.7 }, 1.67)
      timeline.fromTo(
        heading,
        { alpha: 0, y: 292 },
        { alpha: 1, y: 270, duration: 0.6, ease: 'back.out(1.2)' },
        1.7,
      )
      timeline.to(caption, { alpha: 1, duration: 0.5 }, 2.1)
      timeline.to(this, { alpha: 0, duration: 0.55 }, 3.5)
    })
  }

  private _text(value: string, size: number, color: string, y: number): Text {
    const text = new Text({
      text: value,
      style: {
        fontFamily: 'Georgia',
        fontSize: size,
        fontWeight: '700',
        fill: color,
        align: 'center',
        wordWrap: true,
        wordWrapWidth: 700,
      },
    })
    text.anchor.set(0.5)
    text.y = y
    return text
  }

  private _cancel(): void {
    this._timeline?.kill()
    this._timeline = undefined
    this.visible = false
    const resolve = this._finish
    this._finish = undefined
    resolve?.()
  }

  override destroy(options?: { children?: boolean }): void {
    this._cancel()
    super.destroy(options)
  }
}
