import { Container, Graphics, Text } from 'pixi.js'
import { gsap } from 'gsap'
import type { Point } from './layout.js'

/** A moving energy trail follows only the cells returned by the winning payline. */
export class PaylineTrail extends Container {
  private readonly _ink = new Graphics()
  private readonly _badge: Text
  private _timeline?: gsap.core.Timeline
  private _resolve?: () => void

  constructor(private readonly _theme: 'dragon' | 'forest') {
    super()
    this.eventMode = 'none'
    this._badge = new Text({
      text: '×2',
      style: {
        fontFamily: 'Arial',
        fontSize: 28,
        fontWeight: '800',
        fill: '#e4ffc6',
        stroke: { color: '#17331f', width: 4 },
      },
    })
    this._badge.anchor.set(0.5)
    this.addChild(this._ink, this._badge)
    this.visible = false
  }

  async play(points: readonly Point[], durationMs: number, doubled = false): Promise<void> {
    this._stop()
    if (this.destroyed || points.length < 2) return
    this.visible = true
    this.alpha = 1
    const color = this._theme === 'dragon' ? 0xffb331 : 0xa9f89d
    const core = this._theme === 'dragon' ? 0xfff1b0 : 0xedffdc
    const reduced = globalThis.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false
    const cursor = { progress: 0 }
    const first = points[0]!
    const last = points[points.length - 1]!
    const gap = Math.abs(points[1]!.x - first.x)
    this._badge.visible = doubled
    this._badge.position.set(last.x, last.y - Math.max(24, gap * 0.33))
    this._badge.scale.set(Math.min(1, gap / 100))
    const render = () => {
      const ink = this._ink.clear()
      for (let i = 0; i < points.length - 1; i++) {
        const a = points[i]!,
          b = points[i + 1]!
        ink
          .moveTo(a.x, a.y)
          .lineTo(b.x, b.y)
          .stroke({ color, width: Math.max(5, gap * 0.1), alpha: 0.12 })
        ink
          .moveTo(a.x, a.y)
          .lineTo(b.x, b.y)
          .stroke({ color, width: Math.max(2, gap * 0.025), alpha: 0.55 })
      }
      if (reduced) return
      // Twelve particles follow the same piecewise-linear path, with a tapered wake.
      for (let i = 0; i < 12; i++) {
        const p = cursor.progress - i * 0.025
        if (p < 0 || p > 1) continue
        const distance = p * (points.length - 1)
        const index = Math.min(points.length - 2, Math.floor(distance))
        const a = points[index]!,
          b = points[index + 1]!
        const t = distance - index
        const x = a.x + (b.x - a.x) * t
        const y = a.y + (b.y - a.y) * t
        const radius = Math.max(2, gap * 0.085) * (1 - i / 15)
        ink.circle(x, y, radius * 2).fill({ color, alpha: 0.025 * (12 - i) })
        ink.circle(x, y, radius).fill({ color: i === 0 ? core : color, alpha: 1 - i / 15 })
        if (i % 3 === 0) {
          const offset = Math.sin(p * 24 + i) * gap * 0.13
          ink
            .circle(x - radius, y + offset, Math.max(1, radius * 0.25))
            .fill({ color: core, alpha: 0.7 })
        }
      }
    }
    await new Promise<void>((resolve) => {
      this._resolve = resolve
      this._timeline = gsap.timeline({ onComplete: () => this._stop() }).to(cursor, {
        progress: 1.25,
        duration: Math.max(0.18, durationMs / 1000),
        ease: 'none',
        onUpdate: render,
      })
    })
  }

  private _stop(): void {
    this._timeline?.kill()
    this._timeline = undefined
    this._ink.clear()
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
