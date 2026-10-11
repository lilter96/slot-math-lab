import { Texture } from 'pixi.js'

interface HaloOpts {
  radius: number
  hue: number
  saturation: number
  lightness: number
}

const _cache = new Map<string, Texture>()

function bucket(v: number, step: number): number {
  return Math.round(v / step) * step
}

export function bakeHaloTexture(opts: HaloOpts): Texture {
  const r = bucket(opts.radius, 2)
  const h = bucket(opts.hue, 10)
  const s = bucket(opts.saturation, 10)
  const l = bucket(opts.lightness, 10)
  const key = `${r}|${h}|${s}|${l}`

  const cached = _cache.get(key)
  if (cached) return cached

  const size = r * 2 + 4
  const canvas = document.createElement('canvas')
  canvas.width = size
  canvas.height = size
  const ctx = canvas.getContext('2d')!
  const cx = size / 2
  const cy = size / 2

  const grad = ctx.createRadialGradient(cx, cy, 0, cx, cy, size / 2)
  grad.addColorStop(0, `hsla(${h},${s}%,${l}%,1.0)`)
  grad.addColorStop(0.15, `hsla(${h},${s}%,${l}%,0.8)`)
  grad.addColorStop(0.4, `hsla(${h},${s}%,${Math.max(l - 20, 20)}%,0.4)`)
  grad.addColorStop(0.7, `hsla(${h},${s}%,${Math.max(l - 30, 10)}%,0.12)`)
  grad.addColorStop(1.0, `hsla(${h},${s}%,${Math.max(l - 40, 5)}%,0.0)`)

  ctx.fillStyle = grad
  ctx.beginPath()
  ctx.arc(cx, cy, size / 2, 0, Math.PI * 2)
  ctx.fill()

  const tex = Texture.from(canvas)
  _cache.set(key, tex)
  return tex
}

export function destroyHaloCache(): void {
  for (const tex of _cache.values()) tex.destroy(true)
  _cache.clear()
}
