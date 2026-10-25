import { Container, Graphics, Text } from 'pixi.js'
import type { Application, Texture } from 'pixi.js'
import { SYMBOLS } from '@tgslots/x7-club'
import type { GameAssets } from '../../engine/asset-registry'
import { createReelArt } from '../../engine/reel-art'

export const symbolTitles: Record<string, string> = {
  CHILL: 'Chill Capybara',
  HYPE: 'Heart Crystal',
  LOL: 'Good Mood',
  GG: 'DJ Star',
  SEVEN: 'Lucky Seven',
  WILD: 'Club Boss',
  COIN: 'Credit Prize',
}
/** Art is loaded through the shared asset registry; only booster UI plates are drawn in code. */
export function buildTextures(app: Application, assets: GameAssets): Record<string, Texture> {
  const textures = createReelArt(app, assets, SYMBOLS, 132)
  for (const [id, copy, color] of [
    ['STOP', 'BANK', 0xe9badf],
    ['PLUS1', '+1×', 0xdfff80],
    ['PLUS2', '+2×', 0xffa9df],
    ['X7', '×7', 0xffdf89],
  ] as const) {
    const container = new Container()
    const plate = new Graphics()
      .roundRect(3, 3, 126, 70, 14)
      .fill({ color: 0x592344 })
      .stroke({ color: 0xefbc76, width: 3 })
      .roundRect(8, 8, 116, 60, 11)
      .fill({ color: 0x230c31 })
      .stroke({ color, width: 1.5, alpha: 0.7 })
      .moveTo(13, 11)
      .lineTo(119, 11)
      .stroke({ color: 0xffffff, width: 1, alpha: 0.3 })
    const text = new Text({
      text: copy,
      style: {
        fontFamily: 'Bungee, Arial Black, sans-serif',
        fontSize: id === 'STOP' ? 25 : 36,
        fill: color,
        stroke: { color: 0x512130, width: 3 },
        dropShadow: { color, blur: 10, alpha: 0.3, distance: 0 },
      },
    })
    text.anchor.set(0.5)
    text.position.set(66, 38)
    container.addChild(plate, text)
    textures[id] = app.renderer.generateTexture({ target: container, resolution: 2 })
    container.destroy({ children: true })
  }
  return textures
}
