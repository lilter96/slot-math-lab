import { Container, Graphics, Text } from 'pixi.js'
import type { DestroyOptions } from 'pixi.js'
import type { GameEventBus } from '../../engine/event-bus.js'
import type { GameStateMachine } from '../../engine/state-machine.js'
import { GameUIState } from '../../types.js'

const CONTROL_W = 140
const CONTROL_H = 68
const TITLE_TEXT = 'BUY BONUS'

export interface BuyBonusControlState {
  readonly uiState: GameUIState
  readonly isAutoSpin: boolean
  readonly freeSpinsRemaining: number
}

export function formatBuyBonusCostLabel(costMultiplier: number): string {
  return `${costMultiplier}x BET`
}

export function isBuyBonusEnabled(state: BuyBonusControlState): boolean {
  return state.uiState === GameUIState.IDLE && !state.isAutoSpin && state.freeSpinsRemaining <= 0
}

export class BuyBonusControl extends Container {
  private readonly _background = new Graphics()
  private readonly _title = new Text({
    text: TITLE_TEXT,
    style: {
      fontFamily: 'Cinzel, serif',
      fill: '#ffe9a8',
      fontSize: 16,
      fontWeight: '900',
      letterSpacing: 0.6,
    },
  })
  private readonly _subtitle: Text
  private readonly _unsubs: Array<() => void> = []

  private _freeSpinsRemaining = 0
  private _isAutoSpin = false

  constructor(eventBus: GameEventBus, fsm: GameStateMachine, costMultiplier: number) {
    super()

    this._subtitle = new Text({
      text: formatBuyBonusCostLabel(costMultiplier),
      style: {
        fontFamily: 'Cinzel, serif',
        fill: '#ffd36b',
        fontSize: 11,
        fontWeight: '700',
        letterSpacing: 0.8,
      },
    })

    this._build()
    this._sync(fsm.state)

    this._unsubs.push(
      fsm.addListener((state) => {
        this._sync(state)
      }),
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
      if (!this.eventMode || this.eventMode !== 'static') return
      eventBus.emit('buy-bonus:requested', {})
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
    this._title.y = 17

    this._subtitle.anchor.set(0.5)
    this._subtitle.x = CONTROL_W / 2
    this._subtitle.y = 44

    this.cursor = 'pointer'
    this.addChild(this._background, this._title, this._subtitle)
  }

  private _sync(uiState: GameUIState): void {
    const enabled = isBuyBonusEnabled({
      uiState,
      isAutoSpin: this._isAutoSpin,
      freeSpinsRemaining: this._freeSpinsRemaining,
    })

    this.alpha = enabled ? 1 : 0.45
    this.eventMode = enabled ? 'static' : 'none'
    this._drawBackground(enabled)
  }

  private _drawBackground(enabled: boolean): void {
    this._background.clear()
    this._background.roundRect(0, 0, CONTROL_W, CONTROL_H, 10)
    this._background.fill(enabled ? 0x9c5f05 : 0x4a3517)
    this._background.stroke({ width: 2, color: enabled ? 0xffd36b : 0xb69254 })
    this._background.roundRect(3, 3, CONTROL_W - 6, CONTROL_H - 6, 8)
    this._background.stroke({
      width: 1,
      color: enabled ? 0xffefb3 : 0xd6bf92,
      alpha: 0.32,
    })
  }
}
