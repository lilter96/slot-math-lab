import { Container } from 'pixi.js'
import { gsap } from 'gsap'

/** Recursively kills all GSAP tweens targeting a container and its descendants. */
export function killAllTweens(c: Container): void {
  gsap.killTweensOf(c)
  if (c.scale) gsap.killTweensOf(c.scale)
  for (const child of [...c.children]) {
    if (child instanceof Container) killAllTweens(child)
    else gsap.killTweensOf(child)
  }
}
