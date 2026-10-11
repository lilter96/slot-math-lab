import { Graphics } from 'pixi.js'
import type { UILayoutSnapshot } from '../../../engine/layout.js'

const FRAME_PAD = 8
const FRAME_RADIUS = 14

export interface ReelFrameConfig {
  reels: number
  rows: number
  symbolSize: number
  reelSpacing: number
}

export class ReelFrame extends Graphics {
  private readonly _cfg: ReelFrameConfig

  constructor(cfg: ReelFrameConfig) {
    super()
    this._cfg = cfg
  }

  update(layout: UILayoutSnapshot, reelScale: number): void {
    const { reels, rows, symbolSize, reelSpacing } = this._cfg
    const { x, y, width, height } = layout.reelBounds
    this.clear()

    // Faint cell separators (kept very low so symbols read cleanly).
    for (let col = 1; col < reels; col++) {
      const sepX = x + (col * (symbolSize + reelSpacing) - reelSpacing / 2) * reelScale
      this.moveTo(sepX, y)
        .lineTo(sepX, y + height)
        .stroke({ color: 0xffffff, width: 1, alpha: 0.05 })
    }
    for (let row = 1; row < rows; row++) {
      const sepY = y + row * symbolSize * reelScale
      this.moveTo(x, sepY)
        .lineTo(x + width, sepY)
        .stroke({ color: 0xffffff, width: 1, alpha: 0.05 })
    }

    // Clean board frame: a dark outer edge with a soft brass inner line.
    const fx = x - FRAME_PAD
    const fy = y - FRAME_PAD
    const fw = width + FRAME_PAD * 2
    const fh = height + FRAME_PAD * 2
    this.roundRect(fx, fy, fw, fh, FRAME_RADIUS).stroke({ color: 0x140d07, width: 6, alpha: 0.9 })
    this.roundRect(fx, fy, fw, fh, FRAME_RADIUS).stroke({ color: 0x8a6a3a, width: 2, alpha: 0.55 })
  }
}
