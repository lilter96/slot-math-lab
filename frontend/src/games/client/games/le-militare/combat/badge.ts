import { Container, Graphics, Text } from 'pixi.js'
import { gsap } from 'gsap'
import type { GameEventBus } from '../../../engine/event-bus.js'
import { ANIMATION_CONFIG } from '../animation-config.js'

const BADGE_STYLE_OPTS = {
  fontFamily: 'Cinzel, serif',
  fontSize: 30,
  fontWeight: '900' as const,
  fill: '#ffe066',
  stroke: '#8a6010',
  strokeThickness: 4,
  dropShadow: {
    color: '#000000',
    blur: 4,
    distance: 2,
    alpha: 0.9,
  },
}

export async function playBadge(
  parent: Container,
  cx: number,
  cy: number,
  multiplier: number,
  sh: number,
  reel: number,
  row: number,
  bus: GameEventBus,
): Promise<void> {
  if (multiplier <= 1) return
  const badge = new Container()
  const label = `×${multiplier}`
  // Scale badge width for large multipliers
  const badgeW = Math.max(76, label.length * 18 + 12)
  const bg = new Graphics()
  bg.roundRect(-badgeW / 2, -22, badgeW, 44, 11)
  bg.fill({ color: 0x0a0a0a, alpha: 0.92 })
  bg.stroke({ color: 0xd4af37, width: 2.5 })
  const txt = new Text({ text: label, style: BADGE_STYLE_OPTS })
  txt.anchor.set(0.5)
  badge.addChild(bg)
  badge.addChild(txt)
  badge.x = cx
  badge.y = cy - sh * 0.15
  badge.scale.set(0)
  badge.alpha = 1
  parent.addChild(badge)

  await new Promise<void>((resolve) => {
    gsap.to(badge.scale, {
      x: 1.18,
      y: 1.18,
      duration: 0.22,
      delay: 0.04,
      ease: 'back.out(2.8)',
      onComplete: () => {
        gsap.to(badge.scale, { x: 1.0, y: 1.0, duration: 0.1 })
      },
    })

    // Drift upward for a "rising prize" feel
    gsap.to(badge, {
      y: cy - sh * 0.22,
      duration: ANIMATION_CONFIG.BADGE_DRIFT_DURATION_S,
      delay: ANIMATION_CONFIG.BADGE_DRIFT_DELAY_S,
      ease: 'power2.out',
      onComplete: () => {
        const isDestroyed = parent.destroyed
        if (!isDestroyed) {
          parent.removeChild(badge)
          gsap.killTweensOf(badge)
          gsap.killTweensOf(badge.scale)
        }
        // Always emit so the handler cleans up the badge regardless
        bus.emit('le-militare:multiplier:stick', { reel, row, multiplier, badge })
        if (isDestroyed) {
          badge.destroy({ children: true })
        }
        resolve()
      },
    })
  })
}
