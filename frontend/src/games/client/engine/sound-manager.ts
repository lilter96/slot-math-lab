import { sound } from '@pixi/sound'
import type { GameEventBus, GameEventMap, GameEventType } from './event-bus.js'

export type SoundMapping = {
  readonly [K in GameEventType]?:
    | string
    | { name: string; options?: { loop?: boolean; volume?: number; fade?: number } }
}

export class SoundManager {
  private _sfxVolume = 0.8
  private _bgmVolume = 0.3
  private _muted = false
  private _currentBgm: string | null = null
  private _mapping: SoundMapping = {}

  constructor(private readonly _eventBus: GameEventBus) {
    // Reserve headroom when music and distinct effects overlap.
    sound.volumeAll = 0.8
    this._loadSettings()
    this._setupEventListeners()
  }

  get isMuted(): boolean {
    return this._muted
  }

  get sfxVolume(): number {
    return this._sfxVolume
  }

  get bgmVolume(): number {
    return this._bgmVolume
  }

  setMuted(muted: boolean): void {
    this._muted = muted
    if (muted) {
      sound.muteAll()
    } else {
      sound.unmuteAll()
    }
    this._saveSettings()
  }

  setSFXVolume(volume: number): void {
    this._sfxVolume = Math.max(0, Math.min(1, volume))
    this._saveSettings()
    // We don't set global volume for SFX because we apply it per-play
  }

  setBGMVolume(volume: number): void {
    this._bgmVolume = Math.max(0, Math.min(1, volume))
    if (this._currentBgm) {
      sound.volume(this._currentBgm, this._bgmVolume)
    }
    this._saveSettings()
  }

  setMapping(mapping: SoundMapping): void {
    this._mapping = mapping
  }

  playBGM(name: string, options: { loop?: boolean; fade?: number } = {}): void {
    const { loop = true, fade = 1000 } = options

    if (this._currentBgm === name) return

    if (this._currentBgm) {
      sound.stop(this._currentBgm)
    }

    this._currentBgm = name
    sound.play(name, {
      loop,
      volume: this._bgmVolume,
      singleInstance: true,
    })

    if (fade > 0) {
      // @pixi/sound handles volume, we can animate it if needed,
      // but for now simple play is fine as we are adding the capability.
    }
  }

  unlock(): void {
    void sound.context.audioContext.resume()
  }

  stopBGM(): void {
    if (this._currentBgm) {
      sound.stop(this._currentBgm)
      this._currentBgm = null
    }
  }

  playSFX(name: string, options: { volume?: number } = {}): void {
    const volume = (options.volume ?? 1.0) * this._sfxVolume
    // Reels can settle in the same frame. Stacking identical waveforms makes
    // that cue several times louder without adding useful information.
    sound.play(name, { volume, singleInstance: true })
  }

  private _setupEventListeners(): void {
    const originalEmit = this._eventBus.emit.bind(this._eventBus)

    this._eventBus.emit = <K extends GameEventType>(event: K, payload: GameEventMap[K]): void => {
      originalEmit(event, payload)
      this._handleEvent(event)
    }
  }

  private _handleEvent(event: GameEventType): void {
    const entry = this._mapping[event]
    if (!entry) return

    if (typeof entry === 'string') {
      this.playSFX(entry)
    } else {
      const { name, options } = entry
      if (options?.loop) {
        this.playBGM(name, options)
      } else {
        this.playSFX(name, options)
      }
    }
  }
  private _loadSettings(): void {
    try {
      const saved = localStorage.getItem('tgslots_sound_settings')
      if (saved) {
        const settings = JSON.parse(saved)
        this._sfxVolume = settings.sfxVolume ?? 1.0
        this._bgmVolume = settings.bgmVolume ?? 0.5
        this._muted = settings.muted ?? false
        if (this._muted) sound.muteAll()
      }
    } catch (e) {
      console.warn('Failed to load sound settings', e)
    }
  }

  private _saveSettings(): void {
    try {
      const settings = {
        sfxVolume: this._sfxVolume,
        bgmVolume: this._bgmVolume,
        muted: this._muted,
      }
      localStorage.setItem('tgslots_sound_settings', JSON.stringify(settings))
    } catch (e) {
      console.warn('Failed to save sound settings', e)
    }
  }
}
