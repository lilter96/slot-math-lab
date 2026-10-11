import { Graphics } from 'pixi.js'
import type { CombatLayout } from './combat-layout.js'

const ENERGY = 0x8cdbc4

/** Armed-state indicators stay inside their reel; no paths cross the HUD. */
export function drawArmedReels(
  graphics: Graphics,
  layout: CombatLayout,
  activeStates: readonly boolean[],
): void {
  graphics.clear()
  const { symbolWidth: width, reelSpacing: gap, totalHeight: height } = layout
  if (!width || !height) return
  for (let reel = 0; reel < activeStates.length; reel++) {
    if (!activeStates[reel]) continue
    const x = reel * (width + gap)
    for (const edge of [x + 3, x + width - 3]) {
      graphics
        .moveTo(edge, 5)
        .lineTo(edge, height - 5)
        .stroke({ color: ENERGY, width: 2, alpha: 0.42 })
    }
    // A small status light at the foot of the armed column, within the board.
    graphics
      .roundRect(x + width * 0.25, height - 7, width * 0.5, 3, 1.5)
      .fill({ color: ENERGY, alpha: 0.85 })
  }
}

/** A brief upward charge sweep marks the actual reel activated by an S300. */
export function drawActivationPulse(
  graphics: Graphics,
  layout: CombatLayout,
  reel: number,
  progress: number,
): void {
  graphics.clear()
  const { symbolWidth: width, reelSpacing: gap, totalHeight: height } = layout
  const x = reel * (width + gap) + 4
  const head = height * (1 - progress)
  for (let band = 0; band < 8; band++) {
    const y = head + band * 5
    if (y < 4 || y + 4 > height - 4) continue
    graphics.rect(x, y, width - 8, 4).fill({ color: ENERGY, alpha: 0.14 * (1 - band / 8) })
  }
  if (head >= 4 && head <= height - 4) {
    graphics
      .moveTo(x, head)
      .lineTo(x + width - 8, head)
      .stroke({ color: 0xdcfff1, width: 1.5, alpha: 0.65 })
  }
}
