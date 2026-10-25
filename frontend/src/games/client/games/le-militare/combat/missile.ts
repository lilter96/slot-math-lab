import { Container, Graphics, Sprite, type Texture } from 'pixi.js'
import { gsap } from 'gsap'
import type { GameEventBus } from '../../../engine/event-bus.js'
import { ANIMATION_CONFIG } from '../animation-config.js'
import { explode } from './explosion.js'
import { playBadge } from './badge.js'

const bz = (t: number, p0: number, p1: number, p2: number): number =>
  (1 - t) ** 2 * p0 + 2 * (1 - t) * t * p1 + t ** 2 * p2

const bzd = (t: number, p0: number, p1: number, p2: number): number =>
  2 * (1 - t) * (p1 - p0) + 2 * t * (p2 - p1)

export function buildMissile(c: Container, sw: number, texture: Texture): Graphics {
  const body = new Sprite(texture)
  body.anchor.set(0.5, 0.8)
  body.height = sw * 0.82
  body.width = (body.height * texture.width) / texture.height
  c.addChild(body)

  const glow = new Graphics()
  glow.ellipse(0, sw * 0.13, sw * 0.04, sw * 0.1)
  glow.fill({ color: 0xff8800, alpha: 0.7 })
  glow.ellipse(0, sw * 0.11, sw * 0.018, sw * 0.045)
  glow.fill({ color: 0xfff3c5, alpha: 0.95 })
  c.addChildAt(glow, 0)
  return glow
}

function drawTrail(g: Graphics, trail: Array<{ x: number; y: number }>, sw: number): void {
  g.clear()
  if (trail.length < 2) return
  const baseW = sw * 0.04
  for (let i = 1; i < trail.length; i++) {
    const age = i / trail.length
    const prev = trail[i - 1]!
    const cur = trail[i]!
    const alpha = age * 0.75
    const w = baseW * age * 1.8
    const color = age > 0.6 ? 0xffa040 : 0xff5500
    g.moveTo(prev.x, prev.y)
    g.lineTo(cur.x, cur.y)
    g.stroke({ color, width: w, alpha })
  }
  const tip = trail[trail.length - 1]!
  g.circle(tip.x, tip.y, baseW * 1.1)
  g.fill({ color: 0xffffff, alpha: 0.9 })
}

export interface FireMissileParams {
  parent: Container
  lx: number
  ly: number
  tx: number
  ty: number
  sw: number
  sh: number
  reel: number
  row: number
  multiplier: number
  wildId: number
  bus: GameEventBus
  texture: Texture
  impactFrames: readonly Texture[]
}

export async function fireMissile(p: FireMissileParams): Promise<void> {
  const { parent, lx, ly, tx, ty, sw, sh, reel, row, multiplier, wildId } = p
  p.bus.emit('le-militare:missile:launched', {})
  const cpX = lx + (tx - lx) * 0.15
  const cpY = Math.min(ly, ty) - sh * 2

  const missile = new Container()
  const engineGlow = buildMissile(missile, sw, p.texture)
  missile.x = lx
  missile.y = ly
  missile.rotation = -Math.PI / 2
  parent.addChild(missile)

  const trail: Array<{ x: number; y: number }> = []
  const trailG = new Graphics()
  parent.addChildAt(trailG, parent.getChildIndex(missile))

  gsap.to(engineGlow, { alpha: 0.4, duration: 0.12, repeat: -1, yoyo: true, ease: 'none' })

  const progress = { t: 0 }
  await new Promise<void>((resolve) => {
    gsap.to(progress, {
      t: 1,
      duration: ANIMATION_CONFIG.MISSILE_FLIGHT_MS / 1000,
      ease: 'power1.in',
      onUpdate: () => {
        if (parent.destroyed || missile.destroyed) return
        const t = progress.t
        const px = bz(t, lx, cpX, tx)
        const py = bz(t, ly, cpY, ty)
        missile.x = px
        missile.y = py
        const dx = bzd(t, lx, cpX, tx)
        const dy = bzd(t, ly, cpY, ty)
        missile.rotation = Math.atan2(dy, dx) + Math.PI / 2
        trail.push({ x: px, y: py })
        if (trail.length > ANIMATION_CONFIG.TRAIL_LEN) trail.shift()
        drawTrail(trailG, trail, sw)
      },
      onComplete: resolve,
    })
  })

  gsap.killTweensOf(engineGlow)
  if (parent.destroyed) return
  parent.removeChild(missile)
  missile.destroy({ children: true })

  p.bus.emit('le-militare:impact', {})
  p.bus.emit('le-militare:symbol:transform', { reel, row, newSymbolId: wildId })

  void (async () => {
    await new Promise<void>((r) => {
      gsap.to(trailG, {
        alpha: 0,
        duration: 0.18,
        ease: 'power2.in',
        onComplete: () => {
          if (parent.destroyed) {
            r()
            return
          }
          parent.removeChild(trailG)
          trailG.destroy()
          r()
        },
      })
    })
  })()

  await Promise.all([
    explode(parent, tx, ty, sw, p.impactFrames),
    playBadge(parent, tx, ty, multiplier, sh, reel, row, p.bus),
  ])
}
