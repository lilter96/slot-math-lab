import { Container, Rectangle, Sprite } from 'pixi.js'
import type { Application, Texture } from 'pixi.js'
import type { GameAssets } from './asset-registry'
/** Pooled SpriteSymbol resets scale; logical textures keep art cell-sized at retina density. */
export function createReelArt(
  app: Application,
  assets: GameAssets,
  names: readonly string[],
  size: number,
): Record<string, Texture> {
  return Object.fromEntries(
    names.map((name) => {
      const sprite = new Sprite(assets.getTexture(name))
      sprite.width = size
      sprite.height = size
      const target = new Container()
      target.addChild(sprite)
      const texture = app.renderer.generateTexture({
        target,
        frame: new Rectangle(0, 0, size, size),
        resolution: 3,
      })
      target.destroy({ children: true })
      return [name, texture]
    }),
  )
}
