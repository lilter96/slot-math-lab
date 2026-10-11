import { Container, Graphics, Text } from 'pixi.js'
import { gsap } from 'gsap'
import type { DestroyOptions } from 'pixi.js'
import type { GameEventBus } from '../../engine/event-bus.js'
import type { GameStateMachine } from '../../engine/state-machine.js'
import type { GameUIState } from '../../types.js'
import { isBuyBonusEnabled } from './helpers/buy-bonus-rules.js'

const CONTROL_W = 120
const CONTROL_H = 60
const DEBOUNCE_MS = 400

// Hacksaw-style "Buy Feature" entry button: opens the feature menu modal.
export class BuyFeatureControl extends Container {
  private readonly _background = new Graphics()
  private readonly _title = new Text({
    text: 'BUY',
    style: { fontFamily: 'Cinzel, serif', fill: '#1a1205', fontSize: 16, fontWeight: '900' },
  })
  private readonly _subtitle = new Text({
    text: 'FEATURE',
    style: { fontFamily: 'Cinzel, serif', fill: '#1a1205', fontSize: 12, fontWeight: '700' },
  })
  private readonly _unsubs: Array<() => void> = []

  private _freeSpinsRemaining = 0
  private _isAutoSpin = false
  private _lastClickTime = 0

  constructor(eventBus: GameEventBus, fsm: GameStateMachine) {
    super()
    this._build()
    this._sync(fsm.state)

    this._unsubs.push(
      fsm.addListener((state) => this._sync(state)),
      eventBus.on('auto-spin:updated', ({ active }) => {
        this._isAutoSpin = active
        this._sync(fsm.state)
      }),
      eventBus.on('free-spins:updated', ({ remaining }) => {
        this._freeSpinsRemaining = remaining
        this._sync(fsm.state)
      }),
    )

    this.on('pointerdown', () => {
      if (this.eventMode === 'none') return
      const now = performance.now()
      if (now - this._lastClickTime < DEBOUNCE_MS) return
      this._lastClickTime = now
      eventBus.emit('feature-modal:open', {})
    })
  }

  override destroy(options?: DestroyOptions): void {
    for (const unsub of this._unsubs) unsub()
    this._unsubs.length = 0
    super.destroy(options)
  }

  private _build(): void {
    this._title.anchor.set(0.5)
    this._title.x = CONTROL_W / 2
    this._title.y = 22
    this._subtitle.anchor.set(0.5)
    this._subtitle.x = CONTROL_W / 2
    this._subtitle.y = 40
    this.cursor = 'pointer'
    this.addChild(this._background, this._title, this._subtitle)
  }

  private _sync(uiState: GameUIState): void {
    const enabled = isBuyBonusEnabled({
      uiState,
      isAutoSpin: this._isAutoSpin,
      freeSpinsRemaining: this._freeSpinsRemaining,
    })
    this.eventMode = enabled ? 'static' : 'none'
    this.cursor = enabled ? 'pointer' : 'default'
    gsap.killTweensOf(this)
    gsap.to(this, { alpha: enabled ? 1 : 0.55, duration: 0.2 })
    this._background.clear()
    this._background.roundRect(0, 0, CONTROL_W, CONTROL_H, 8)
    this._background.fill(enabled ? 0xffb02e : 0x4a3a14)
    this._background.stroke({ width: 1.5, color: 0x29313b })
  }
}
