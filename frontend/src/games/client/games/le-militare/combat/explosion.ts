import { AnimatedSprite, Container, type Texture } from 'pixi.js'
import { gsap } from 'gsap'

export async function explode(
  parent: Container,
  cx: number,
  cy: number,
  sw: number,
  frames: readonly Texture[],
): Promise<void> {
  if (parent.destroyed) return
  const burst = new AnimatedSprite([...frames])
  burst.anchor.set(0.5)
  burst.position.set(cx, cy)
  burst.width = sw * 1.65
  burst.height = (burst.width * frames[0]!.height) / frames[0]!.width
  burst.loop = false
  burst.animationSpeed = 0.16
  parent.addChild(burst)

  await new Promise<void>((resolve) => {
    burst.onComplete = () => {
      gsap.to(burst, {
        alpha: 0,
        duration: 0.18,
        ease: 'power2.out',
        onComplete: () => {
          if (!burst.destroyed) {
            if (!parent.destroyed) parent.removeChild(burst)
            burst.destroy()
          }
          resolve()
        },
      })
    }
    burst.play()
  })
}
