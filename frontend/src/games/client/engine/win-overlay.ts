import { Container, Sprite, Text, TextStyle } from 'pixi.js'
import { gsap } from 'gsap'
import type { GameAssets } from './asset-registry'
import type { WinTier } from '@tgslots/shared-contracts'
import type { UILayoutSnapshot, Rect } from './layout'
import { getSpinSpeedProfile } from './spin-speed.js'
import type { SpinSpeedProfile } from './spin-speed.js'

export class WinOverlay extends Container {
  private _tierSprite: Sprite
  private _tierText: Text
  private _amountText: Text
  private _reelBounds: Rect = { x: 0, y: 0, width: 480, height: 300 }
  private _assets: GameAssets | null = null
  private _winTiers: readonly WinTier[] = []
  private _speedProfile: SpinSpeedProfile = getSpinSpeedProfile('normal')

  constructor() {
    super()

    this._tierSprite = new Sprite()
    this._tierSprite.anchor.set(0.5)
    this._tierSprite.visible = false
    this.addChild(this._tierSprite)

    this._tierText = new Text({
      text: '',
      style: new TextStyle({
        fontFamily: 'Cinzel, serif',
        fontSize: 72,
        fontWeight: '900',
        fill: 0xffd700,
        dropShadow: { color: 0x000000, blur: 24, distance: 0, alpha: 0.9 },
        stroke: { color: 0x000000, width: 8 },
        align: 'center',
      }),
    })
    this._tierText.anchor.set(0.5)
    this._tierText.visible = false
    this.addChild(this._tierText)

    this._amountText = new Text({
      text: '',
      style: new TextStyle({
        fontFamily: 'Cinzel, serif',
        fontSize: 48,
        fontWeight: '700',
        fill: 0xffffff,
        dropShadow: { color: 0x000000, blur: 14, distance: 0, alpha: 0.85 },
        stroke: { color: 0xb8860b, width: 5 },
        align: 'center',
      }),
    })
    this._amountText.anchor.set(0.5, 0)
    this._amountText.visible = false
    this.addChild(this._amountText)

    this.alpha = 0
    this.visible = false
  }

  public setGame(assets: GameAssets, winTiers: readonly WinTier[]): void {
    this._assets = assets
    this._winTiers = winTiers
  }

  public setTypography(fontFamily: string): void {
    this._tierText.style.fontFamily = fontFamily
    this._amountText.style.fontFamily = fontFamily
  }

  public resize(layout: UILayoutSnapshot): void {
    this._reelBounds = layout.reelBounds
    this.x = layout.reelBounds.x + layout.reelBounds.width / 2
    this.y = layout.reelBounds.y + layout.reelBounds.height / 2
    this._applyFontSizes()
  }

  /** Show a named announcement banner (FREE SPINS!, BONUS!, etc.) centered on the reels. */
  public async announce(text: string, duration = 2000): Promise<void> {
    this._amountText.visible = false
    this._setupTierLabel(text)
    return this._animateIn(duration)
  }

  /**
   * Show a big-win banner with rolling counter — only for BIG WIN / MEGA WIN tiers.
   * Small/regular wins (below the second-lowest tier) return immediately so the HUD card
   * handles the display.
   */
  public async announceWin(amount: number, totalWager: number): Promise<void> {
    const ratio = totalWager > 0 ? amount / totalWager : 0
    const sortedTiers = [...this._winTiers].sort((a, b) => b.thresholdX - a.thresholdX)
    const minThreshold = sortedTiers.at(-1)?.thresholdX ?? 2
    const tier = sortedTiers.find((t) => ratio >= t.thresholdX)

    // Skip banner for small wins (below 2nd tier) — the HUD win card handles these.
    if (!tier || tier.thresholdX <= minThreshold) return

    this._setupTierLabel(tier.copy, tier.textureName)

    // Rolling counter
    this._amountText.text = '0'
    this._amountText.visible = true
    this._repositionLabels()

    const fadeInMs = this._speedProfile.overlayFadeInMs
    const holdMs = Math.max(
      this._speedProfile.overlayMinMs,
      2500 * this._speedProfile.overlayDurationMultiplier,
    )
    const fadeOutMs = this._speedProfile.overlayFadeOutMs

    const counterDuration = Math.min(((fadeInMs + holdMs) / 1000) * 0.7, 1.8)
    const obj = { val: 0 }
    gsap.to(obj, {
      val: amount,
      duration: counterDuration,
      delay: fadeInMs / 1000,
      ease: 'power1.out',
      onUpdate: () => {
        this._amountText.text = Math.floor(obj.val).toLocaleString()
      },
      onComplete: () => {
        this._amountText.text = amount.toLocaleString()
      },
    })

    return new Promise((resolve) => {
      this.visible = true
      this.alpha = 0
      this.scale.set(0.72)

      gsap
        .timeline({ onComplete: resolve })
        .to(this, { alpha: 1, duration: fadeInMs / 1000, ease: 'power2.out' })
        .to(this.scale, { x: 1, y: 1, duration: fadeInMs / 1000, ease: 'back.out(2.2)' }, '<')
        .to(this, { alpha: 1, duration: holdMs / 1000 })
        .to(this, {
          alpha: 0,
          duration: fadeOutMs / 1000,
          ease: 'power1.in',
          onComplete: () => {
            this.visible = false
            this.scale.set(1)
            this._tierText.visible = false
            this._tierSprite.visible = false
            this._amountText.visible = false
          },
        })
    })
  }

  public async announceFreeSpinsAwarded(awarded: number, duration = 2000): Promise<void> {
    return this.announce(`${awarded} FREE SPINS WON`, duration)
  }

  public syncSpinSpeed(profile: SpinSpeedProfile): void {
    this._speedProfile = profile
  }

  private _applyFontSizes(): void {
    const w = this._reelBounds.width

    const tierSize = Math.round(Math.min(Math.max(w * 0.15, 32), 100))
    this._tierText.style.fontSize = tierSize
    this._tierText.style.stroke = {
      color: 0x000000,
      width: Math.max(4, Math.round(tierSize * 0.1)),
    }
    this._tierText.style.dropShadow = {
      color: 0x000000,
      blur: tierSize * 0.35,
      distance: 0,
      alpha: 0.9,
      angle: Math.PI / 4,
    }

    const amountSize = Math.round(Math.min(Math.max(w * 0.1, 22), 68))
    this._amountText.style.fontSize = amountSize
    this._amountText.style.stroke = {
      color: 0xb8860b,
      width: Math.max(3, Math.round(amountSize * 0.08)),
    }
    this._amountText.style.dropShadow = {
      color: 0x000000,
      blur: amountSize * 0.3,
      distance: 0,
      alpha: 0.85,
      angle: Math.PI / 4,
    }

    const spriteTargetW = w * 0.65
    if (this._tierSprite.texture?.width > 0) {
      this._tierSprite.scale.set(spriteTargetW / this._tierSprite.texture.width)
    }

    this._repositionLabels()
  }

  private _repositionLabels(): void {
    const tierSize = (this._tierText.style.fontSize as number) ?? 72
    const amountSize = (this._amountText.style.fontSize as number) ?? 48
    const hasAmount = this._amountText.visible

    if (hasAmount) {
      // Tier label above center, amount below
      const gap = Math.round(tierSize * 0.12)
      this._tierText.y = -(amountSize * 0.5 + gap)
      this._tierSprite.y = -(amountSize * 0.5 + gap)
      this._amountText.y = tierSize * 0.5 + gap
    } else {
      // Tier label centered
      this._tierText.y = 0
      this._tierSprite.y = 0
    }
  }

  private _setupTierLabel(text: string, textureName?: string): void {
    const name =
      textureName ?? this._winTiers.find((t) => t.copy === text)?.textureName ?? 'WIN_SMALL'
    if (name && this._assets) {
      try {
        this._tierSprite.texture = this._assets.getTexture(name)
        const spriteTargetW = this._reelBounds.width * 0.65
        if (this._tierSprite.texture.width > 0) {
          this._tierSprite.scale.set(spriteTargetW / this._tierSprite.texture.width)
        }
        this._tierSprite.visible = true
        this._tierText.text = text
        this._tierText.visible = true
        return
      } catch {
        // fallthrough to text
      }
    }
    this._tierText.text = text
    this._tierText.scale.set(1)
    const maxWidth = this._reelBounds.width * 0.9
    if (this._tierText.width > maxWidth) this._tierText.scale.set(maxWidth / this._tierText.width)
    this._tierText.visible = true
    this._tierSprite.visible = false
  }

  private _animateIn(duration: number): Promise<void> {
    this._repositionLabels()
    const holdMs = Math.max(
      this._speedProfile.overlayMinMs,
      duration * this._speedProfile.overlayDurationMultiplier,
    )
    const fadeInMs = this._speedProfile.overlayFadeInMs
    const fadeOutMs = this._speedProfile.overlayFadeOutMs

    return new Promise((resolve) => {
      this.visible = true
      this.alpha = 0
      this.scale.set(0.72)

      gsap
        .timeline({ onComplete: resolve })
        .to(this, { alpha: 1, duration: fadeInMs / 1000, ease: 'power2.out' })
        .to(this.scale, { x: 1, y: 1, duration: fadeInMs / 1000, ease: 'back.out(2.2)' }, '<')
        .to(this, { alpha: 1, duration: holdMs / 1000 })
        .to(this, {
          alpha: 0,
          duration: fadeOutMs / 1000,
          ease: 'power1.in',
          onComplete: () => {
            this.visible = false
            this.scale.set(1)
            this._tierText.visible = false
            this._tierSprite.visible = false
          },
        })
    })
  }

  override destroy(options?: {
    children?: boolean
    texture?: boolean
    baseTexture?: boolean
  }): void {
    gsap.killTweensOf(this)
    gsap.killTweensOf(this.scale)
    super.destroy(options)
  }
}
