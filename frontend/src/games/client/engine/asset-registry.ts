import { Assets, Texture, Rectangle } from 'pixi.js'
import { sound } from '@pixi/sound'
import type { AssetManifest, GameManifest } from '@tgslots/shared-contracts'

export interface GameAssets {
  readonly manifest: GameManifest // Access to symbol logic, paylines, etc.
  getSymbolTexture(symbolId: number): Texture
  hasSymbol(symbolId: number): boolean
  getSymbolTextureSafe(symbolId: number, fallbackId: number): Texture

  getTexture(name: string): Texture
}

export class AssetRegistry {
  private readonly _textures = new Map<string, Map<string, Texture>>()
  private readonly _frames = new Map<string, Set<Texture>>()
  private readonly _manifests = new Map<string, GameManifest>()

  // Trackers for cleanup
  private readonly _soundKeys = new Map<string, string[]>()

  async loadGame(
    manifest: GameManifest,
    assets: AssetManifest,
    onProgress?: (loaded: number, total: number) => void,
  ): Promise<GameAssets> {
    const { gameId } = manifest

    // 1. Prevent memory leaks from double-loading
    if (this._manifests.has(gameId)) await this.unloadGame(gameId)

    this._manifests.set(gameId, manifest)
    const textures = new Map<string, Texture>()
    this._textures.set(gameId, textures)
    const frames = new Set<Texture>()
    this._frames.set(gameId, frames)

    const soundKeys: string[] = []
    this._soundKeys.set(gameId, soundKeys)

    // 2. Register PNG/JPG assets as a Pixi Bundle
    // This is the "Proper" v8 way. It lets Pixi optimize the loading queue.
    const bundleAssets = Object.entries(assets.images || {}).map(([alias, src]) => ({
      alias,
      src,
    }))

    Assets.addBundle(gameId, bundleAssets)

    // 3. Start Loading Jobs
    const jobs: Promise<void>[] = []
    let jobTotal = 0
    let jobDone = 0

    const push = (p: Promise<void>) => {
      jobTotal++
      jobs.push(
        p.then(() => {
          jobDone++
          onProgress?.(jobDone, jobTotal)
        }),
      )
    }

    // Job: Load PNG Bundle
    push(
      Assets.loadBundle(gameId).then((loadedAssets) => {
        for (const [alias, texture] of Object.entries(loadedAssets)) {
          const t = texture as Texture
          t.source.autoGenerateMipmaps = true
          t.source.scaleMode = 'linear'
          textures.set(alias, t)
        }
        for (const spec of assets.atlases ?? []) {
          const atlas = loadedAssets[spec.image] as Texture
          if (!atlas) throw new Error(`Missing raster atlas: ${spec.image}`)
          for (const [name, frame] of Object.entries(spec.frames)) {
            const texture = new Texture({
              source: atlas.source,
              frame: new Rectangle(frame.x, frame.y, frame.width, frame.height),
            })
            frames.add(texture)
            textures.set(name, texture)
          }
        }
      }),
    )

    // Job: Audio (Parallel)
    if (assets.audio) {
      for (const [name, url] of Object.entries(assets.audio)) {
        if (!sound.exists(name)) {
          push(
            new Promise<void>((resolve, reject) => {
              sound.add(name, {
                url,
                preload: true,
                loaded: (error) => {
                  if (error)
                    reject(new Error(`Could not load game audio: ${name}`, { cause: error }))
                  else resolve()
                },
              })
            }),
          )
          soundKeys.push(name)
        }
      }
    }

    await Promise.all(jobs)
    return this._makeAccessor(gameId)
  }

  async unloadGame(gameId: string): Promise<void> {
    for (const texture of this._frames.get(gameId) ?? []) texture.destroy(false)
    this._frames.delete(gameId)
    this._textures.delete(gameId)
    await Assets.unloadBundle(gameId)

    // 3. Clear Sounds
    const sounds = this._soundKeys.get(gameId)
    sounds?.forEach((k) => sound.remove(k))
    this._soundKeys.delete(gameId)

    // 4. Clear Manifest
    this._manifests.delete(gameId)
  }

  private _makeAccessor(gameId: string): GameAssets {
    const manifest = this._manifests.get(gameId)!
    const textures = this._textures.get(gameId)!

    return {
      manifest, // Now the UI can read manifest.reels, manifest.paylines, etc.

      getSymbolTexture(symbolId: number): Texture {
        const meta = manifest.symbols[symbolId]
        if (!meta) throw new Error(`Symbol ${symbolId} missing in manifest`)
        const t = textures.get(meta.name)
        if (!t) throw new Error(`Texture missing: ${meta.name}`)
        return t
      },

      hasSymbol(symbolId: number): boolean {
        const meta = manifest.symbols[symbolId]
        if (!meta) return false
        return textures.has(meta.name)
      },

      getSymbolTextureSafe(symbolId: number, fallbackId: number): Texture {
        const meta = manifest.symbols[symbolId]
        if (!meta) {
          console.error(
            `[AssetRegistry] Symbol ${symbolId} missing in manifest — using fallback ${fallbackId}`,
          )
          const fallbackMeta = manifest.symbols[fallbackId]
          const fallbackTex = fallbackMeta ? textures.get(fallbackMeta.name) : undefined
          if (!fallbackTex) throw new Error(`Fallback symbol ${fallbackId} also missing`)
          return fallbackTex
        }
        const t = textures.get(meta.name)
        if (!t) {
          console.error(
            `[AssetRegistry] Texture missing for symbol ${symbolId} (${meta.name}) — using fallback ${fallbackId}`,
          )
          const fallbackMeta = manifest.symbols[fallbackId]
          const fallbackTex = fallbackMeta ? textures.get(fallbackMeta.name) : undefined
          if (!fallbackTex) throw new Error(`Fallback symbol ${fallbackId} also missing`)
          return fallbackTex
        }
        return t
      },

      getTexture(name: string): Texture {
        const t = textures.get(name)
        if (!t) throw new Error(`Texture missing: ${name}`)
        return t
      },
    }
  }
}
