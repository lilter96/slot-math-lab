import { Container, Graphics, Sprite, Text, TextStyle, Texture, Ticker } from 'pixi.js'
import type { GameAssets } from '../../../engine/asset-registry.js'
import type { SpinSpeedProfile } from '../../../engine/spin-speed.js'
import { bakeHaloTexture } from './halos.js'
import { getLeafTextures } from './leaf-textures.js'
import { WW_SCENES } from './scenes.js'
import type { SceneSpec } from './scenes.js'

// ─── Particle data ───────────────────────────────────────────────────────────

interface Firefly {
  sprite: Sprite
  alive: boolean
  x: number
  y: number
  vx: number
  vy: number
  phase: number
  pulsePhase: number
  pulseSpeed: number
  size: number
  hue: number
}

interface Ember {
  sprite: Sprite
  alive: boolean
  x: number
  y: number
  vx: number
  vy: number
  life: number
  maxLife: number
  hue: number
}

interface Dust {
  sprite: Sprite
  alive: boolean
  x: number
  y: number
  vy: number
  opacity: number
}

interface Leaf {
  sprite: Sprite
  alive: boolean
  x: number
  y: number
  vx: number
  vy: number
  life: number
  rotation: number
  rotV: number
}

interface Sparkle {
  sprite: Sprite
  alive: boolean
  x: number
  y: number
  vx: number
  vy: number
  life: number
  maxLife: number
}

interface Coin {
  sprite: Sprite
  alive: boolean
  x: number
  y: number
  vx: number
  vy: number
  rotation: number
  rotV: number
  life: number
}

// ─── Helpers ─────────────────────────────────────────────────────────────────

function rand(min: number, max: number): number {
  return min + Math.random() * (max - min)
}

function easeInOutSine(t: number): number {
  return -(Math.cos(Math.PI * t) - 1) / 2
}

// ─── BackgroundContainer ─────────────────────────────────────────────────────

export interface BgEffects {
  coinShower(count?: number): void
  sparkleBurst(x: number, y: number, n?: number): void
  rayFlare(): void
  hollowFlash(): void
}

interface BackgroundContainerOpts {
  assets: GameAssets
}

export class BackgroundContainer extends Container {
  readonly bgEffects: BgEffects

  private _assets: GameAssets

  private _baseSprite: Sprite | null = null
  private _hollowSprite: Sprite | null = null
  private _mistSprite: Sprite | null = null
  private _rayGraphics: Graphics[] = []
  private _runeTexts: Text[] = []
  private _spec: SceneSpec | null = null
  private _variant: 'mobile' | 'tablet' | 'desktop' | null = null

  // Animated state
  private _time = 0
  private _hollowT = 0
  private _runePhases: number[] = []
  private _runeFlashTimers: number[] = []
  private _rayPhases: number[] = []
  private _rayPeriods: number[] = []
  private _mistT = 0
  private _reducedMotion: boolean

  // Particle pools
  private _fireflies: Firefly[] = []
  private _embers: Ember[] = []
  private _dusts: Dust[] = []
  private _leaves: Leaf[] = []
  private _sparkles: Sparkle[] = []
  private _coins: Coin[] = []

  // Particle containers
  private _dustContainer = new Container()
  private _emberContainer = new Container()
  private _leafContainer = new Container()
  private _fireflyContainer = new Container()
  private _sparkleContainer = new Container()
  private _coinContainer = new Container()

  // One-shot effect timers
  private _rayFlareTimer = 0
  private _hollowFlashTimer = 0

  constructor(opts: BackgroundContainerOpts) {
    super()
    this._assets = opts.assets
    this._reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches

    this.addChild(this._dustContainer)
    this.addChild(this._emberContainer)
    this.addChild(this._leafContainer)
    this.addChild(this._fireflyContainer)
    this.addChild(this._sparkleContainer)
    this.addChild(this._coinContainer)

    this.bgEffects = {
      coinShower: (count = 60) => this._coinShower(count),
      sparkleBurst: (x, y, n = 8) => this._sparkleBurst(x, y, n),
      rayFlare: () => {
        this._rayFlareTimer = 1.5
      },
      hollowFlash: () => {
        this._hollowFlashTimer = 0.3
      },
    }

    Ticker.shared.add(this._update, this)
  }

  async setVariant(v: 'mobile' | 'tablet' | 'desktop'): Promise<void> {
    if (this._variant === v) return
    this._variant = v
    const spec = WW_SCENES[v]
    this._spec = spec

    this._baseSprite?.destroy()
    this._baseSprite = null
    if (!spec.bgTexture) throw new Error(`Missing raster background for ${v}`)
    const tex = this._assets.getTexture(spec.bgTexture)

    this._baseSprite = new Sprite(tex)
    const { width: vw, height: vh } = spec.viewBox
    const s = Math.max(vw / tex.width, vh / tex.height)
    this._baseSprite.scale.set(s)
    this._baseSprite.anchor.set(0.5)
    this._baseSprite.x = vw / 2
    this._baseSprite.y = vh / 2
    this.addChildAt(this._baseSprite, 0)

    this._rebuildOverlays(spec)
    this._rebuildParticles(spec)
  }

  setViewport(w: number, h: number): void {
    if (!this._spec) return
    const { width: vw, height: vh } = this._spec.viewBox
    const s = Math.max(w / vw, h / vh)
    this.scale.set(s)
    this.x = (w - vw * s) / 2
    this.y = (h - vh * s) / 2
  }

  setSpinSpeed(_profile: SpinSpeedProfile): void {
    // drift velocity scaling reserved for future polish
  }

  override destroy(): void {
    Ticker.shared.remove(this._update, this)
    super.destroy({ children: true })
  }

  // ─── Internal overlay setup ───────────────────────────────────────────────

  private _rebuildOverlays(spec: SceneSpec): void {
    // Destroy old overlays
    for (const g of this._rayGraphics) g.destroy()
    this._rayGraphics = []
    for (const t of this._runeTexts) t.destroy()
    this._runeTexts = []
    if (this._hollowSprite) {
      this._hollowSprite.destroy({ texture: true })
      this._hollowSprite = null
    }
    if (this._mistSprite) {
      this._mistSprite.destroy({ texture: true })
      this._mistSprite = null
    }

    if (this._reducedMotion) return

    // God rays — Graphics polygons with screen blend
    this._rayPhases = []
    this._rayPeriods = []
    for (const ray of spec.godRays) {
      const g = new Graphics()
      g.blendMode = 'screen'
      const flatPts = ray.points.split(' ').flatMap((p) => p.split(',').map(Number))
      g.poly(flatPts).fill({ color: 0xffe066, alpha: (ray.opacity ?? 1.0) * 0.25 })
      this._rayGraphics.push(g)
      this._rayPhases.push(Math.random() * Math.PI * 2)
      this._rayPeriods.push(rand(8, 14))
      this.addChild(g)
    }

    // Hollow glow — radial gradient sprite
    const hollowTex = bakeHaloTexture({
      radius: spec.hollowRx * 4,
      hue: 45,
      saturation: 100,
      lightness: 75,
    })
    this._hollowSprite = new Sprite(hollowTex)
    this._hollowSprite.blendMode = 'screen'
    this._hollowSprite.anchor.set(0.5)
    this._hollowSprite.x = spec.hollowCenter.x
    this._hollowSprite.y = spec.hollowCenter.y
    this._hollowSprite.alpha = 0.85
    this.addChild(this._hollowSprite)

    // Mist — ellipse drawn as Graphics with screen blend
    this._mistSprite = this._bakeMistSprite(spec)
    this.addChild(this._mistSprite)

    // Runes — Text objects with screen blend
    const fontSize = spec.viewBox.width < 500 ? 9 : spec.viewBox.width < 1500 ? 16 : 22
    const runeStyle = new TextStyle({
      fontFamily: 'Cinzel, serif',
      fontSize,
      fill: 0xffe066,
    })
    for (const r of spec.runes) {
      const t = new Text({ text: r.glyph, style: runeStyle })
      t.blendMode = 'screen'
      t.anchor.set(0.5)
      t.x = r.x
      t.y = r.y
      t.alpha = rand(0.5, 0.9)
      this._runeTexts.push(t)
      this.addChild(t)
    }
    this._runePhases = spec.runes.map(() => Math.random() * Math.PI * 2)
    this._runeFlashTimers = spec.runes.map(() => rand(6, 10))

    // Re-add particle containers on top
    this.removeChild(this._dustContainer)
    this.removeChild(this._emberContainer)
    this.removeChild(this._leafContainer)
    this.removeChild(this._fireflyContainer)
    this.removeChild(this._sparkleContainer)
    this.removeChild(this._coinContainer)
    this.addChild(this._dustContainer)
    this.addChild(this._emberContainer)
    this.addChild(this._leafContainer)
    this.addChild(this._fireflyContainer)
    this.addChild(this._sparkleContainer)
    this.addChild(this._coinContainer)
  }

  private _bakeMistSprite(spec: SceneSpec): Sprite {
    const canvas = document.createElement('canvas')
    const w = spec.mistRx * 2 + 40
    const h = spec.mistRy * 2 + 40
    canvas.width = w
    canvas.height = h
    const ctx = canvas.getContext('2d')!
    const grad = ctx.createRadialGradient(w / 2, h / 2, 0, w / 2, h / 2, Math.max(w, h) / 2)
    grad.addColorStop(0, 'rgba(168, 200, 144, 0.35)')
    grad.addColorStop(1, 'rgba(168, 200, 144, 0)')
    ctx.fillStyle = grad
    ctx.ellipse(w / 2, h / 2, spec.mistRx, spec.mistRy, 0, 0, Math.PI * 2)
    ctx.fill()
    const s = new Sprite(Texture.from(canvas))
    s.blendMode = 'screen'
    s.anchor.set(0.5)
    s.x = spec.mistCenter.x
    s.y = spec.mistCenter.y
    s.alpha = 0.85
    return s
  }

  // ─── Particle pool setup ──────────────────────────────────────────────────

  private _rebuildParticles(spec: SceneSpec): void {
    const rm = this._reducedMotion
    const d = spec.densities
    const counts = {
      fireflies: rm ? Math.ceil(d.fireflies / 2) : d.fireflies,
      embers: rm ? Math.ceil(d.embers / 2) : d.embers,
      dust: rm ? Math.ceil(d.dust / 2) : d.dust,
      leaves: rm ? Math.ceil(d.leaves / 2) : d.leaves,
    }

    // Clear containers
    this._dustContainer.removeChildren().forEach((c) => c.destroy())
    this._emberContainer.removeChildren().forEach((c) => c.destroy())
    this._leafContainer.removeChildren().forEach((c) => c.destroy())
    this._fireflyContainer.removeChildren().forEach((c) => c.destroy())
    this._sparkleContainer.removeChildren().forEach((c) => c.destroy())
    this._coinContainer.removeChildren().forEach((c) => c.destroy())
    this._fireflies = []
    this._embers = []
    this._dusts = []
    this._leaves = []
    this._sparkles = []
    this._coins = []

    const { viewBox, hollowCenter } = spec

    // Dust pool
    const dustTex = bakeHaloTexture({ radius: 6, hue: 120, saturation: 30, lightness: 80 })
    for (let i = 0; i < counts.dust; i++) {
      const s = new Sprite(dustTex)
      s.blendMode = 'screen'
      s.anchor.set(0.5)
      const size = rand(0.5, 1.5)
      s.scale.set(size / 6)
      const x = rand(0, viewBox.width)
      const y = rand(0, viewBox.height)
      s.x = x
      s.y = y
      s.alpha = rand(0.1, 0.3)
      this._dustContainer.addChild(s)
      this._dusts.push({
        sprite: s,
        alive: true,
        x,
        y,
        vy: rand(-0.05, -0.15),
        opacity: rand(0.1, 0.3),
      })
    }

    // Ember pool
    for (let i = 0; i < counts.embers; i++) {
      const hue = rand(20, 50)
      const emberTex = bakeHaloTexture({ radius: 8, hue, saturation: 100, lightness: 65 })
      const s = new Sprite(emberTex)
      s.blendMode = 'screen'
      s.anchor.set(0.5)
      const maxLife = rand(3, 6)
      const e: Ember = {
        sprite: s,
        alive: true,
        x: hollowCenter.x + rand(-20, 20),
        y: hollowCenter.y + rand(-10, 10),
        vx: rand(-0.3, 0.3),
        vy: rand(-0.4, -0.8),
        life: rand(0, maxLife),
        maxLife,
        hue,
      }
      s.x = e.x
      s.y = e.y
      this._emberContainer.addChild(s)
      this._embers.push(e)
    }

    // Firefly pool
    for (let i = 0; i < counts.fireflies; i++) {
      const hue = rand(50, 70)
      const size = rand(1.2, 3.4)
      const ffTex = bakeHaloTexture({ radius: size * 5, hue, saturation: 100, lightness: 75 })
      const s = new Sprite(ffTex)
      s.blendMode = 'screen'
      s.anchor.set(0.5)
      const x = rand(0, viewBox.width)
      const y = rand(50, viewBox.height * 0.7)
      s.x = x
      s.y = y
      this._fireflyContainer.addChild(s)
      this._fireflies.push({
        sprite: s,
        alive: true,
        x,
        y,
        vx: rand(-0.3, 0.3),
        vy: rand(-0.2, 0.2),
        phase: Math.random() * Math.PI * 2,
        pulsePhase: Math.random() * Math.PI * 2,
        pulseSpeed: rand(1, 3) * Math.PI * 2,
        size,
        hue,
      })
    }

    // Leaf pool
    const leafTextures = getLeafTextures()
    for (let i = 0; i < counts.leaves; i++) {
      const tex = leafTextures[i % leafTextures.length]!
      const s = new Sprite(tex)
      s.anchor.set(0.5)
      const scale = rand(0.5, 1.2)
      s.scale.set(scale)
      const x = rand(0, viewBox.width)
      s.x = x
      s.y = -20
      this._leafContainer.addChild(s)
      this._leaves.push({
        sprite: s,
        alive: true,
        x,
        y: rand(-viewBox.height, -20),
        vx: rand(-0.5, 0.5),
        vy: rand(40, 80),
        life: 0,
        rotation: rand(0, Math.PI * 2),
        rotV: rand(-2, 2),
      })
    }

    // Pre-allocate sparkle/coin pools
    const sparkleTex = bakeHaloTexture({ radius: 6, hue: 60, saturation: 100, lightness: 90 })
    for (let i = 0; i < 40; i++) {
      const s = new Sprite(sparkleTex)
      s.anchor.set(0.5)
      s.visible = false
      this._sparkleContainer.addChild(s)
      this._sparkles.push({
        sprite: s,
        alive: false,
        x: 0,
        y: 0,
        vx: 0,
        vy: 0,
        life: 0,
        maxLife: 0.6,
      })
    }

    const coinTex = bakeHaloTexture({ radius: 10, hue: 45, saturation: 100, lightness: 65 })
    for (let i = 0; i < 80; i++) {
      const s = new Sprite(coinTex)
      s.anchor.set(0.5)
      s.visible = false
      this._coinContainer.addChild(s)
      this._coins.push({
        sprite: s,
        alive: false,
        x: 0,
        y: 0,
        vx: 0,
        vy: 0,
        rotation: 0,
        rotV: 0,
        life: 0,
      })
    }
  }

  // ─── Per-frame update ─────────────────────────────────────────────────────

  private _update = (ticker: Ticker): void => {
    const dt = Math.min(ticker.deltaMS / 1000, 0.05)
    this._time += dt

    if (!this._spec || !this._baseSprite) return

    if (!this._reducedMotion) {
      this._updateHollow(dt)
      this._updateRays(dt)
      this._updateRunes(dt)
      this._updateMist(dt)
    }

    this._updateDust(dt)
    this._updateEmbers(dt)
    this._updateFireflies(dt)
    this._updateLeaves(dt)
    this._updateSparkles(dt)
    this._updateCoins(dt)

    // One-shot effect timers
    if (this._rayFlareTimer > 0) {
      this._rayFlareTimer = Math.max(0, this._rayFlareTimer - dt)
      const flareAlpha = this._rayFlareTimer > 0 ? 0.6 * (this._rayFlareTimer / 1.5) : 0
      for (const g of this._rayGraphics) g.alpha = 1 + flareAlpha
    }
    if (this._hollowFlashTimer > 0) {
      this._hollowFlashTimer = Math.max(0, this._hollowFlashTimer - dt)
      if (this._hollowSprite) {
        this._hollowSprite.alpha = 1.0 + this._hollowFlashTimer * 2
      }
    }
  }

  private _updateHollow(dt: number): void {
    if (!this._hollowSprite) return
    this._hollowT = (this._hollowT + dt / 4) % 1
    const t = easeInOutSine(this._hollowT < 0.5 ? this._hollowT * 2 : (1 - this._hollowT) * 2)
    const alpha = 0.85 + 0.15 * t
    const scale = 1.0 + 0.08 * t
    this._hollowSprite.alpha = alpha
    this._hollowSprite.scale.set(scale)
  }

  private _updateRays(dt: number): void {
    for (let i = 0; i < this._rayGraphics.length; i++) {
      const g = this._rayGraphics[i]!
      const period = this._rayPeriods[i]!
      const phase = this._rayPhases[i]!
      const tNorm = (this._time / period + phase / (Math.PI * 2)) % 1
      const alpha = 0.5 + 0.5 * Math.sin(tNorm * Math.PI * 2)
      const dx = Math.sin((this._time / period) * Math.PI * 2 + phase) * 10
      g.alpha = alpha
      g.x = dx
    }
    void dt
  }

  private _updateRunes(dt: number): void {
    for (let i = 0; i < this._runeTexts.length; i++) {
      const t = this._runeTexts[i]!
      this._runePhases[i]! += dt * (2 + Math.random() * 0.5)
      const flicker = 0.65 + 0.25 * Math.sin(this._runePhases[i]!)
      t.alpha = flicker

      this._runeFlashTimers[i]! -= dt
      if (this._runeFlashTimers[i]! <= 0) {
        t.alpha = 1.0
        this._runeFlashTimers[i] = rand(6, 10)
      }
    }
  }

  private _updateMist(dt: number): void {
    if (!this._mistSprite || !this._spec) return
    this._mistT += dt
    const dx = Math.sin((this._mistT / 15) * Math.PI * 2) * 15
    const alpha = 0.7 + 0.3 * Math.sin((this._mistT / 15) * Math.PI * 2 + 0.5)
    this._mistSprite.x = this._spec.mistCenter.x + dx
    this._mistSprite.alpha = alpha
  }

  private _updateDust(dt: number): void {
    if (!this._spec) return
    const { width: W, height: H } = this._spec.viewBox
    for (const d of this._dusts) {
      d.y += d.vy * dt * 60
      if (d.y < -10) {
        d.y = H + 10
        d.x = rand(0, W)
      }
      d.sprite.x = d.x
      d.sprite.y = d.y
    }
  }

  private _updateEmbers(dt: number): void {
    if (!this._spec) return
    const { hollowCenter } = this._spec
    for (const e of this._embers) {
      e.life += dt
      if (e.life >= e.maxLife) {
        e.x = hollowCenter.x + rand(-20, 20)
        e.y = hollowCenter.y + rand(-10, 10)
        e.vx = rand(-0.3, 0.3)
        e.vy = rand(-0.4, -0.8)
        e.life = 0
        e.maxLife = rand(3, 6)
      }
      e.vy -= 0.005
      e.vx += Math.sin(e.life * 2 + e.x * 0.01) * 0.15 * dt
      e.x += e.vx * dt * 60
      e.y += e.vy * dt * 60
      const progress = e.life / e.maxLife
      e.sprite.alpha = progress < 0.1 ? progress * 10 : 1 - progress
      e.sprite.x = e.x
      e.sprite.y = e.y
    }
  }

  private _updateFireflies(dt: number): void {
    if (!this._spec) return
    const { width: W, height: H } = this._spec.viewBox
    const canopyBottom = H * 0.7
    for (const ff of this._fireflies) {
      ff.pulsePhase += ff.pulseSpeed * dt
      ff.phase += dt * 0.8
      ff.x += (ff.vx + Math.sin(ff.phase) * 0.3) * dt * 60
      ff.y += ff.vy * dt * 60

      if (ff.x < 0) ff.x = W
      if (ff.x > W) ff.x = 0
      if (ff.y < 0) ff.y = canopyBottom
      if (ff.y > canopyBottom) ff.y = 0

      const pulse = 0.5 + 0.5 * Math.sin(ff.pulsePhase)
      ff.sprite.alpha = 0.4 + 0.6 * pulse
      ff.sprite.x = ff.x
      ff.sprite.y = ff.y
    }
  }

  private _updateLeaves(dt: number): void {
    if (!this._spec) return
    const { width: W, height: H } = this._spec.viewBox
    for (const leaf of this._leaves) {
      leaf.life += dt
      leaf.x += (leaf.vx + Math.sin(leaf.life * 1.5) * 1.2) * dt * 60
      leaf.y += leaf.vy * dt
      leaf.rotation += leaf.rotV * dt
      if (leaf.y > H + 30) {
        leaf.x = rand(0, W)
        leaf.y = -20
        leaf.life = 0
        leaf.vx = rand(-0.5, 0.5)
        leaf.vy = rand(40, 80)
        leaf.rotation = rand(0, Math.PI * 2)
        leaf.rotV = rand(-2, 2)
      }
      leaf.sprite.x = leaf.x
      leaf.sprite.y = leaf.y
      leaf.sprite.rotation = leaf.rotation
    }
  }

  private _updateSparkles(dt: number): void {
    for (const sp of this._sparkles) {
      if (!sp.alive) continue
      sp.life += dt
      if (sp.life >= sp.maxLife) {
        sp.alive = false
        sp.sprite.visible = false
        continue
      }
      sp.x += sp.vx * dt * 60
      sp.y += sp.vy * dt * 60
      sp.vy -= 0.01
      const t = sp.life / sp.maxLife
      sp.sprite.alpha = t < 0.3 ? t / 0.3 : 1 - t
      sp.sprite.x = sp.x
      sp.sprite.y = sp.y
    }
  }

  private _updateCoins(dt: number): void {
    if (!this._spec) return
    const H = this._spec.viewBox.height
    for (const c of this._coins) {
      if (!c.alive) continue
      c.life += dt
      c.vy += 200 * dt
      c.x += c.vx * dt
      c.y += c.vy * dt
      c.rotation += c.rotV * dt
      c.sprite.x = c.x
      c.sprite.y = c.y
      c.sprite.rotation = c.rotation
      if (c.y > H + 20) {
        c.alive = false
        c.sprite.visible = false
      }
    }
  }

  // ─── One-shot effects ─────────────────────────────────────────────────────

  private _sparkleBurst(x: number, y: number, n: number): void {
    let spawned = 0
    for (const sp of this._sparkles) {
      if (sp.alive) continue
      const angle = rand(0, Math.PI * 2)
      const speed = rand(1, 4)
      sp.x = x
      sp.y = y
      sp.vx = Math.cos(angle) * speed
      sp.vy = Math.sin(angle) * speed - 2
      sp.life = 0
      sp.maxLife = rand(0.4, 0.8)
      sp.alive = true
      sp.sprite.x = x
      sp.sprite.y = y
      sp.sprite.visible = true
      sp.sprite.alpha = 1
      if (++spawned >= n) break
    }
  }

  private _coinShower(count: number): void {
    if (!this._spec) return
    const cx = this._spec.hollowCenter.x
    let spawned = 0
    for (const c of this._coins) {
      if (c.alive) continue
      c.x = cx + rand(-80, 80)
      c.y = -20
      c.vx = rand(-60, 60)
      c.vy = rand(-50, 0)
      c.rotation = rand(0, Math.PI * 2)
      c.rotV = rand(-5, 5)
      c.life = 0
      c.alive = true
      c.sprite.x = c.x
      c.sprite.y = c.y
      c.sprite.visible = true
      c.sprite.alpha = 1
      if (++spawned >= count) break
    }
  }
}
