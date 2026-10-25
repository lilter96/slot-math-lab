import { Container, Graphics } from 'pixi.js'
import { gsap } from 'gsap'
import type { ActivationEvent } from '@tgslots/le-militare'
import { drawArmedReels, drawActivationPulse } from './armed-reel-renderer.js'
import type { CombatLayout } from './combat-layout.js'

const prefersReducedMotion = (): boolean => {
  try {
    return window.matchMedia('(prefers-reduced-motion: reduce)').matches
  } catch {
    return false
  }
}

async function flashReel(
  overlay: Graphics,
  parent: Container,
  reel: number,
  layout: CombatLayout,
): Promise<void> {
  const { symbolWidth, reelSpacing, totalHeight } = layout
  const x = reel * (symbolWidth + reelSpacing)
  overlay.clear()
  overlay.rect(x, 0, symbolWidth, totalHeight).fill({ color: 0x8cdbc4, alpha: 0.12 })
  overlay.alpha = 0
  await new Promise<void>((resolve) => {
    gsap.to(overlay, {
      alpha: 1,
      duration: 0.1,
      ease: 'sine.out',
      onComplete: () => {
        gsap.to(overlay, {
          alpha: 0,
          duration: 0.28,
          ease: 'power1.in',
          onComplete: () => {
            if (!parent.destroyed) overlay.clear()
            overlay.alpha = 1
            resolve()
          },
        })
      },
    })
  })
}

export async function animateActivations(
  parent: Container,
  overlay: Graphics,
  armedG: Graphics,
  currentG: Graphics,
  activations: readonly ActivationEvent[],
  layout: CombatLayout,
  activeStates: boolean[],
): Promise<void> {
  if (activations.length === 0) return

  const redraw = (): void => drawArmedReels(armedG, layout, activeStates)
  const reduced = prefersReducedMotion()

  for (const activation of activations) {
    const r = activation.reel

    if (reduced) {
      activeStates[r] = true
      redraw()
      await flashReel(overlay, parent, r, layout)
      continue
    }

    // Charge the activated column without drawing across symbols or controls.
    await new Promise<void>((resolve) => {
      const head = { t: 0 }
      gsap.to(head, {
        t: 1,
        duration: 0.35,
        ease: 'power1.in',
        onUpdate: () => {
          if (parent.destroyed) return
          drawActivationPulse(currentG, layout, r, head.t)
        },
        onComplete: resolve,
      })
    })
    if (parent.destroyed) return
    currentG.clear()

    // Keep a restrained indicator until this reel spends its activation.
    activeStates[r] = true
    redraw()
    await flashReel(overlay, parent, r, layout)
  }
}
