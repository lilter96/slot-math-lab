import { Container, Graphics, Text } from 'pixi.js'
import { gsap } from 'gsap'
import type { CombatLayout } from './combat-layout.js'

/** Radar acquires only actual server-selected targets, before the launcher fires. */
export class EngagementBoard extends Container {
  private readonly _ink = new Graphics()
  private readonly _label = new Text({
    text: '',
    style: {
      fontFamily: 'Arial',
      fontSize: 22,
      fontWeight: '700',
      fill: '#ffe3a4',
      letterSpacing: 3,
    },
  })
  private _timeline?: gsap.core.Timeline
  private _resolve?: () => void

  constructor() {
    super()
    this.eventMode = 'none'
    this.visible = false
    this.addChild(this._ink, this._label)
    this._label.anchor.set(0.5)
  }

  async acquire(
    targets: readonly { reel: number; row: number }[],
    layout: CombatLayout,
    reels: number,
    label: string,
  ): Promise<void> {
    this.hide()
    const { symbolWidth: sw, symbolHeight: sh, reelSpacing: gap, totalHeight: height } = layout
    if (!sw || this.destroyed) return
    const width = reels * (sw + gap) - gap
    this._label.text = label
    this._label.position.set(width / 2, 24)
    this.visible = true
    this.alpha = 0
    const phase = { angle: -Math.PI / 2, lock: 0 }
    const paint = () => {
      this._ink.clear().rect(0, 0, width, height).fill({ color: 0x03140f, alpha: 0.3 })
      const cx = width / 2,
        cy = height / 2,
        radius = Math.max(width, height) * 0.55
      for (let ring = 1; ring <= 3; ring++)
        this._ink
          .circle(cx, cy, (radius * ring) / 3)
          .stroke({ color: 0x8addac, alpha: 0.12, width: 1 })
      this._ink.moveTo(cx, cy)
      for (let part = 0; part <= 12; part++) {
        const angle = phase.angle - 0.45 + (part * 0.45) / 12
        this._ink.lineTo(cx + Math.cos(angle) * radius, cy + Math.sin(angle) * radius)
      }
      this._ink.closePath().fill({ color: 0x7aedb4, alpha: 0.08 })
      for (const target of targets) {
        const x = target.reel * (sw + gap) + sw / 2
        const y = target.row * sh + sh / 2
        const r = sw * (0.4 - phase.lock * 0.1)
        for (const [sx, sy] of [
          [-1, -1],
          [-1, 1],
          [1, -1],
          [1, 1],
        ] as const) {
          this._ink
            .moveTo(x + sx * r, y + sy * r * 0.5)
            .lineTo(x + sx * r, y + sy * r)
            .lineTo(x + sx * r * 0.5, y + sy * r)
            .stroke({ color: 0xffd171, width: 3, alpha: 0.5 + phase.lock * 0.5 })
        }
        this._ink
          .circle(x, y, r * 0.4)
          .stroke({ color: 0xffd171, width: 1, alpha: phase.lock * 0.8 })
      }
    }
    paint()
    await new Promise<void>((resolve) => {
      this._resolve = resolve
      this._timeline = gsap
        .timeline({
          onComplete: () => {
            this._resolve = undefined
            resolve()
          },
        })
        .to(this, { alpha: 1, duration: 0.2 }, 0)
        .to(phase, { angle: Math.PI * 1.5, duration: 0.42, ease: 'none', onUpdate: paint }, 0)
        .to(phase, { lock: 1, duration: 0.18, onUpdate: paint }, 0.24)
    })
  }

  hide(): void {
    this._timeline?.kill()
    this._timeline = undefined
    this._resolve?.()
    this._resolve = undefined
    this.visible = false
  }

  override destroy(options?: { children?: boolean }): void {
    this.hide()
    super.destroy(options)
  }
}
