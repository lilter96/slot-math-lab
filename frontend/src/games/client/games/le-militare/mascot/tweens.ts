import { Container } from 'pixi.js'
import { gsap } from 'gsap'

export interface IdleTweens {
  bob: gsap.core.Tween
  radar: gsap.core.Tween
}

export function startIdleTweens(masterContainer: Container, radarContainer: Container): IdleTweens {
  const bob = gsap.to(masterContainer, {
    y: 5,
    duration: 1.5,
    ease: 'sine.inOut',
    yoyo: true,
    repeat: -1,
  })
  const radar = gsap.to(radarContainer, {
    alpha: 0.92,
    duration: 2.4,
    ease: 'sine.inOut',
    yoyo: true,
    repeat: -1,
  })
  return { bob, radar }
}

export function startDeployedIdleTweens(
  masterContainer: Container,
  radarContainer: Container,
): IdleTweens {
  const bob = gsap.to(masterContainer, {
    y: 3,
    duration: 2.0,
    ease: 'sine.inOut',
    yoyo: true,
    repeat: -1,
  })
  const radar = gsap.to(radarContainer, {
    alpha: 0.92,
    duration: 1.6,
    yoyo: true,
    ease: 'sine.inOut',
    repeat: -1,
  })
  return { bob, radar }
}

export function createDeployTimeline(
  masterContainer: Container,
  launcherContainer: Container,
  onComplete: () => void,
): gsap.core.Timeline {
  const tl = gsap.timeline()
  tl.to(masterContainer, { y: 6, duration: 0.1, ease: 'power2.in' })
  tl.to(launcherContainer, { angle: -90, duration: 0.55, ease: 'power2.out' }, '>')
  tl.to(masterContainer, { y: 10, duration: 0.06, ease: 'none' }, '-=0.04')
  tl.to(masterContainer, { y: -2, duration: 0.07, ease: 'none' })
  tl.to(masterContainer, { y: 0, duration: 0.16, ease: 'power2.out', onComplete })
  return tl
}

export function createRetractTween(
  launcherContainer: Container,
  onComplete: () => void,
): gsap.core.Tween {
  return gsap.to(launcherContainer, {
    angle: 0,
    duration: 0.45,
    ease: 'power2.inOut',
    onComplete,
  })
}
