import { Texture } from 'pixi.js'

const LEAF_COLORS = ['#cc7722', '#dd8833', '#aa6622'] as const
let _textures: Texture[] | null = null

export function getLeafTextures(): Texture[] {
  if (_textures) return _textures

  _textures = LEAF_COLORS.map((color) => {
    const canvas = document.createElement('canvas')
    canvas.width = 24
    canvas.height = 14
    const ctx = canvas.getContext('2d')!

    ctx.save()
    ctx.translate(12, 7)
    ctx.beginPath()
    ctx.ellipse(0, 0, 10, 5, 0, 0, Math.PI * 2)
    ctx.fillStyle = color
    ctx.fill()

    ctx.strokeStyle = 'rgba(0,0,0,0.4)'
    ctx.lineWidth = 0.8
    ctx.beginPath()
    ctx.moveTo(-9, 0)
    ctx.lineTo(9, 0)
    ctx.moveTo(0, -4)
    ctx.lineTo(0, 4)
    ctx.stroke()
    ctx.restore()

    return Texture.from(canvas)
  })

  return _textures
}

export function destroyLeafTextures(): void {
  if (_textures) {
    for (const tex of _textures) tex.destroy(true)
    _textures = null
  }
}
