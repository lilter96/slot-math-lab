import { Container, Graphics, Text } from 'pixi.js'
import { gsap } from 'gsap'
import type { FreeSpinsStatus } from '../types'
import { GameUIState } from '../types'
import { GameStateMachine } from './state-machine'
import { SessionManager } from './session-manager'
import type { GameEventBus } from './event-bus.js'
import type { HUDLayoutMode, UILayoutSnapshot } from './layout'
import type { SpinSpeedMode } from './spin-speed'

const FONT_DISPLAY = 'Arial, sans-serif'

const SLOT_W = 140
const SLOT_H = 68
const SPEED_W = 104
const SPEED_H = 68
const SPEED_ROW_H = 28
const AUTO_W = 140
const AUTO_H = 68
const SPIN_D = 88
const BTN_GAP = 12
const PANEL_H = SPIN_D
const PANEL_W = SPEED_W + BTN_GAP + AUTO_W + BTN_GAP + SPIN_D + BTN_GAP + SLOT_W

interface InfoCard {
  key: 'balance' | 'bet' | 'win' | 'freeSpins'
  container: Container
  background: Graphics
  title: Text
  value: Text
}

function drawWoodPanel(g: Graphics, w: number, h: number, fill = 0x142329) {
  g.clear()
  g.roundRect(0, 0, w, h, 12)
  g.fill({ color: fill, alpha: 0.96 })
  g.roundRect(0, 0, w, h, 12)
  g.stroke({ width: 1.5, color: 0xb6a477, alpha: 0.85 })
  g.roundRect(3, 3, w - 6, h - 6, 9)
  g.stroke({ width: 0.9, color: 0xffe066, alpha: 0.32 })
}

function woodPanel(w: number, h: number, fill = 0x17242b): Graphics {
  const g = new Graphics()
  drawWoodPanel(g, w, h, fill)
  return g
}

export class HUD extends Container {
  private _session: SessionManager
  private _fsm: GameStateMachine

  private _layout?: UILayoutSnapshot

  private _spinButton: Container
  private _spinLabel: Text

  private _speedPanel: Container
  private _speedBg: Graphics
  private _fastBtn: Container
  private _fastBtnBg: Graphics
  private _fastBtnLabel: Text
  private _turboBtn: Container
  private _turboBtnBg: Graphics
  private _turboBtnLabel: Text
  private _spinSpeedMode: SpinSpeedMode = 'normal'

  private _autoBtn: Container
  private _autoBtnBg: Graphics
  private _autoBtnCount: Text
  private _autoBtnHint: Text

  private _infoContainer: Container
  private _buttonPanel: Container

  private _balanceCard: InfoCard
  private _betCard: InfoCard
  private _winCard: InfoCard
  private _freeSpinsCard: InfoCard

  private _autoSpinActive = false
  private readonly _slots = new Map<string, Container>()

  constructor(
    session: SessionManager,
    fsm: GameStateMachine,
    eventBus: GameEventBus,
    private readonly _wagerAmount: (multiplier: number) => number = (multiplier) => multiplier,
  ) {
    super()
    this._session = session
    this._fsm = fsm

    this._spinButton = new Container()
    this._spinLabel = new Text({
      text: 'SPIN',
      style: { fontFamily: FONT_DISPLAY, fill: '#ffe066', fontSize: 20, fontWeight: '900' },
    })

    this._speedPanel = new Container()
    this._speedBg = new Graphics()
    this._fastBtn = new Container()
    this._fastBtnBg = new Graphics()
    this._fastBtnLabel = new Text({
      text: 'FAST',
      style: { fontFamily: FONT_DISPLAY, fill: '#ffe9b8', fontSize: 14, fontWeight: '800' },
    })
    this._turboBtn = new Container()
    this._turboBtnBg = new Graphics()
    this._turboBtnLabel = new Text({
      text: 'TURBO',
      style: { fontFamily: FONT_DISPLAY, fill: '#ffe9b8', fontSize: 14, fontWeight: '800' },
    })

    this._autoBtn = new Container()
    this._autoBtnBg = new Graphics()
    this._autoBtnCount = new Text({
      text: 'AUTO',
      style: { fontFamily: FONT_DISPLAY, fill: '#ffe066', fontSize: 16, fontWeight: '700' },
    })
    this._autoBtnHint = new Text({
      text: 'STOP',
      style: { fontFamily: FONT_DISPLAY, fill: '#ffcc80', fontSize: 10 },
    })

    this._infoContainer = new Container()
    this._buttonPanel = new Container()

    this._balanceCard = this._makeInfoCard('balance', 'BALANCE', 0x17242b)
    this._betCard = this._makeInfoCard('bet', 'BET', 0x172b30)
    this._winCard = this._makeInfoCard('win', 'WIN', 0x15322a)
    this._freeSpinsCard = this._makeInfoCard('freeSpins', 'FREE SPINS', 0x282839)
    this._freeSpinsCard.container.visible = false

    this._build()
    this.updateTexts()
    this._fsm.addListener(() => this._onStateChange())
    eventBus.on('win:awarded', () => this.updateTexts())
  }

  /** Returns a named slot container inside the button panel for game-specific feature buttons. */
  public slot(name: string): Container {
    let c = this._slots.get(name)
    if (!c) {
      c = new Container()
      this._slots.set(name, c)
      if (name === 'control-right') {
        c.x = SPEED_W + BTN_GAP + AUTO_W + BTN_GAP + SPIN_D + BTN_GAP
        c.y = (PANEL_H - SLOT_H) / 2
        this._buttonPanel.addChild(c)
      }
    }
    return c
  }

  /** Clears all slot children added by a game runtime — call on game unmount. */
  public clearSlots(): void {
    for (const c of this._slots.values()) c.removeChildren()
  }

  private _makeInfoCard(key: InfoCard['key'], title: string, fill: number): InfoCard {
    const container = new Container()
    const background = woodPanel(160, 64, fill)
    const titleText = new Text({
      text: title,
      style: {
        fontFamily: FONT_DISPLAY,
        fill: '#c8a060',
        fontSize: 12,
        fontWeight: '700',
        letterSpacing: 1.2,
      },
    })
    titleText.anchor.set(0.5, 0)

    const valueText = new Text({
      text: '',
      style: {
        fontFamily: FONT_DISPLAY,
        fill: '#ffe066',
        fontSize: 24,
        fontWeight: '700',
        stroke: { color: '#5a3a00', width: 3 },
      },
    })
    valueText.anchor.set(0.5)

    container.addChild(background, titleText, valueText)
    this._infoContainer.addChild(container)

    return { key, container, background, title: titleText, value: valueText }
  }

  private _build() {
    this.addChild(this._infoContainer)

    this._speedBg.roundRect(0, 0, SPEED_W, SPEED_H, 10)
    this._speedBg.fill(0x0f1722)
    this._speedBg.stroke({ width: 1.5, color: 0xb6a477, alpha: 0.85 })
    this._speedPanel.addChild(this._speedBg)
    this._speedPanel.y = (PANEL_H - SPEED_H) / 2

    this._buildSpeedButton(this._fastBtn, this._fastBtnBg, this._fastBtnLabel, 5)
    this._fastBtn.on('pointerdown', () => {
      if (this._layout?.viewportClass === 'phone')
        this.emit(this._spinSpeedMode === 'normal' ? 'toggleFastSpin' : 'toggleTurboSpin')
      else this.emit('toggleFastSpin')
    })

    this._buildSpeedButton(
      this._turboBtn,
      this._turboBtnBg,
      this._turboBtnLabel,
      SPEED_H - SPEED_ROW_H - 5,
    )
    this._turboBtn.on('pointerdown', () => this.emit('toggleTurboSpin'))

    this._speedPanel.addChild(this._fastBtn, this._turboBtn)
    this._buttonPanel.addChild(this._speedPanel)

    this._drawAutoBtnBg(false)
    this._autoBtnCount.anchor.set(0.5)
    this._autoBtnCount.x = AUTO_W / 2
    this._autoBtnCount.y = AUTO_H / 2
    this._autoBtnHint.anchor.set(0.5)
    this._autoBtnHint.x = AUTO_W / 2
    this._autoBtnHint.visible = false
    this._autoBtn.addChild(this._autoBtnBg, this._autoBtnCount, this._autoBtnHint)
    this._autoBtn.x = SPEED_W + BTN_GAP
    this._autoBtn.y = (PANEL_H - AUTO_H) / 2
    this._autoBtn.interactive = true
    this._autoBtn.cursor = 'pointer'
    this._autoBtn.on('pointerdown', () => {
      if (this._autoSpinActive) this.emit('stopAutoSpin')
      else this.emit('autoSpin')
    })
    this._buttonPanel.addChild(this._autoBtn)

    const spinBg = new Graphics()
    spinBg.circle(SPIN_D / 2, SPIN_D / 2, SPIN_D / 2)
    spinBg.fill(0xe8ce8c)
    spinBg.circle(SPIN_D / 2, SPIN_D / 2, 39)
    spinBg.fill(0x17433c)
    spinBg.circle(SPIN_D / 2, SPIN_D / 2, 34)
    spinBg.stroke({ color: 0x82d2b9, width: 1.2, alpha: 0.65 })
    this._spinButton.addChild(spinBg)
    this._spinLabel.anchor.set(0.5)
    this._spinLabel.x = SPIN_D / 2
    this._spinLabel.y = SPIN_D / 2
    this._spinButton.addChild(this._spinLabel)
    this._spinButton.x = SPEED_W + BTN_GAP + AUTO_W + BTN_GAP
    this._spinButton.interactive = true
    this._spinButton.cursor = 'pointer'
    this._spinButton.on('pointerdown', () => {
      gsap.to(this._spinButton.scale, { x: 0.92, y: 0.92, duration: 0.06, ease: 'power2.in' })
      this.emit('spin')
    })
    this._spinButton.on('pointerup', () => {
      gsap.to(this._spinButton.scale, { x: 1, y: 1, duration: 0.12, ease: 'back.out(2)' })
    })
    this._spinButton.on('pointerupoutside', () => {
      gsap.to(this._spinButton.scale, { x: 1, y: 1, duration: 0.12, ease: 'back.out(2)' })
    })
    this._spinButton.on('pointerover', () => {
      gsap.to(this._spinButton.scale, { x: 1.06, y: 1.06, duration: 0.15, ease: 'power2.out' })
    })
    this._spinButton.on('pointerout', () => {
      gsap.to(this._spinButton.scale, { x: 1, y: 1, duration: 0.15, ease: 'power2.out' })
    })
    this._buttonPanel.addChild(this._spinButton)

    this.addChild(this._buttonPanel)
    this.syncSpinSpeed('normal')
  }

  public resize(layout: UILayoutSnapshot) {
    this._layout = layout
    this._layoutInfoCards(layout)
    this._layoutControls(layout)
  }

  private _layoutInfoCards(layout: UILayoutSnapshot) {
    const activeCards = [this._balanceCard, this._betCard, this._winCard]
    if (this._freeSpinsCard.container.visible) activeCards.push(this._freeSpinsCard)

    const { infoArea } = layout
    const gap = layout.hudMode === 'portrait' ? 8 : 10

    const minCardWidth = layout.hudMode === 'wide' ? 132 : layout.hudMode === 'portrait' ? 92 : 104
    const targetCardWidth =
      layout.hudMode === 'wide' ? 156 : layout.hudMode === 'portrait' ? 108 : 118
    const maxCardWidth = layout.hudMode === 'wide' ? 168 : layout.hudMode === 'portrait' ? 136 : 128
    const cols = Math.min(
      layout.hudMode === 'portrait' ? 2 : activeCards.length,
      Math.max(1, Math.floor((infoArea.width + gap) / (minCardWidth + gap))),
    )
    const rows = Math.ceil(activeCards.length / cols)

    const availableCardWidth = (infoArea.width - gap * (cols - 1)) / cols
    const cardWidth =
      rows === 1
        ? Math.min(
            maxCardWidth,
            Math.max(minCardWidth, Math.min(targetCardWidth, availableCardWidth)),
          )
        : availableCardWidth
    const cardHeight = (infoArea.height - gap * (rows - 1)) / rows

    const usedWidth = cols * cardWidth + gap * (cols - 1)
    const usedHeight = rows * cardHeight + gap * (rows - 1)
    const startX = infoArea.x + (infoArea.width - usedWidth) / 2
    const startY = infoArea.y + (infoArea.height - usedHeight) / 2

    for (const card of [this._balanceCard, this._betCard, this._winCard, this._freeSpinsCard]) {
      card.container.visible =
        card === this._freeSpinsCard ? this._freeSpinsCard.container.visible : true
    }

    activeCards.forEach((card, index) => {
      const col = index % cols
      const row = Math.floor(index / cols)
      card.container.x = startX + col * (cardWidth + gap)
      card.container.y = startY + row * (cardHeight + gap)
      this._sizeInfoCard(card, cardWidth, cardHeight, layout.hudMode)
    })
  }

  private _sizeInfoCard(card: InfoCard, width: number, height: number, hudMode: HUDLayoutMode) {
    const fill =
      card.key === 'win'
        ? 0x15322a
        : card.key === 'freeSpins'
          ? 0x282839
          : card.key === 'bet'
            ? 0x172b30
            : 0x17242b
    drawWoodPanel(card.background, width, height, fill)

    const titleSize = height < 54 ? 10 : hudMode === 'wide' ? 12 : 11
    const valueSize = Math.max(16, Math.min(28, height * 0.34))
    card.title.style.fontSize = titleSize
    card.value.style.fontSize = valueSize
    card.value.style.stroke = { color: '#5a3a00', width: height < 50 ? 2 : 3 }

    card.title.x = width / 2
    card.title.y = Math.max(6, height * 0.12)
    card.value.x = width / 2
    card.value.y = height * 0.64
  }

  private _layoutControls(layout: UILayoutSnapshot) {
    const scale = Math.min(
      1,
      layout.controlsArea.width / PANEL_W,
      layout.controlsArea.height / PANEL_H,
    )
    this._buttonPanel.scale.set(scale)
    this._buttonPanel.x = layout.controlsArea.x + (layout.controlsArea.width - PANEL_W * scale) / 2
    this._buttonPanel.y = layout.controlsArea.y + (layout.controlsArea.height - PANEL_H * scale) / 2
    this._syncSpeedLayout()
  }

  private _drawAutoBtnBg(active: boolean) {
    this._autoBtnBg.clear()
    this._autoBtnBg.roundRect(0, 0, AUTO_W, AUTO_H, 8)
    if (active) {
      this._autoBtnBg.fill(0x5a1a00)
      this._autoBtnBg.stroke({ width: 2, color: 0xff8040 })
    } else {
      this._autoBtnBg.fill(0x0d1a2a)
      this._autoBtnBg.stroke({ width: 1.5, color: 0xb6a477, alpha: 0.85 })
    }
    this._autoBtnBg.roundRect(3, 3, AUTO_W - 6, AUTO_H - 6, 6)
    this._autoBtnBg.stroke({ width: 0.8, color: active ? 0xffaa60 : 0xffe066, alpha: 0.3 })
  }

  private _buildSpeedButton(
    container: Container,
    background: Graphics,
    label: Text,
    y: number,
  ): void {
    label.anchor.set(0.5)
    label.x = (SPEED_W - 10) / 2
    label.y = SPEED_ROW_H / 2
    container.x = 5
    container.y = y
    container.interactive = true
    container.cursor = 'pointer'
    container.addChild(background, label)
  }

  private _drawSpeedButton(background: Graphics, active: boolean, turbo = false): void {
    background.clear()
    background.roundRect(0, 0, SPEED_W - 10, SPEED_ROW_H, 8)
    background.fill(active ? (turbo ? 0x6a1200 : 0x163d61) : 0x1d2430)
    background.stroke({
      width: 1.5,
      color: active ? (turbo ? 0xff8c42 : 0x74c0fc) : 0x5b6677,
    })
    background.roundRect(2, 2, SPEED_W - 14, SPEED_ROW_H - 4, 6)
    background.stroke({
      width: 0.8,
      color: active ? 0xffe4b0 : 0xd9dfeb,
      alpha: active ? 0.34 : 0.18,
    })
  }

  public syncAutoSpin(isActive: boolean, remaining: number) {
    this._autoSpinActive = isActive
    this._drawAutoBtnBg(isActive)

    if (isActive) {
      this._autoBtnCount.text = remaining === 0 ? '∞' : remaining.toString()
      this._autoBtnCount.y = 22
      this._autoBtnHint.y = 46
      this._autoBtnHint.visible = true
    } else {
      this._autoBtnCount.text = 'AUTO'
      this._autoBtnCount.y = AUTO_H / 2
      this._autoBtnHint.visible = false
    }

    this._refreshButtonStates()
  }

  public syncSpinSpeed(mode: SpinSpeedMode) {
    this._spinSpeedMode = mode
    this._drawSpeedButton(this._fastBtnBg, mode === 'fast')
    this._drawSpeedButton(this._turboBtnBg, mode === 'turbo', true)
    this._fastBtn.alpha = mode === 'turbo' ? 0.55 : 1
    this._turboBtn.alpha = mode === 'fast' ? 0.55 : 1
    // Bold stroke for active, subtle for inactive
    this._fastBtnBg.alpha = mode === 'fast' ? 1 : 0.7
    this._turboBtnBg.alpha = mode === 'turbo' ? 1 : 0.7
    this._syncSpeedLayout()
  }

  private _syncSpeedLayout() {
    const touch = this._layout?.viewportClass === 'phone'
    this._turboBtn.visible = !touch
    this._fastBtnLabel.text = touch ? this._spinSpeedMode.toUpperCase() : 'FAST'
    this._fastBtnLabel.y = touch ? 32 : SPEED_ROW_H / 2
    this._fastBtnLabel.style.fontSize = touch ? 16 : 14
    this._fastBtn.alpha = 1
    if (touch) {
      this._fastBtnBg.clear()
      this._fastBtnBg.roundRect(0, 0, SPEED_W - 10, 64, 8)
      this._fastBtnBg.fill(0x1b303a)
      this._fastBtnBg.stroke({ color: 0xd8bf80, width: 1.5 })
      this._fastBtnBg.alpha = 1
    }
  }

  public updateTexts() {
    this._balanceCard.value.text = this._session.balance.toLocaleString('en-US')
    this._betCard.value.text = this._wagerAmount(this._session.betMultiplier).toLocaleString(
      'en-US',
    )
    this._winCard.value.text =
      this._session.lastWin > 0 ? this._session.lastWin.toLocaleString('en-US') : '—'
  }

  public syncFreeSpinsStatus(status: FreeSpinsStatus) {
    this._freeSpinsCard.container.visible = status.active
    this._freeSpinsCard.value.text = status.active ? `${status.remaining} LEFT` : ''
    if (this._layout) this._layoutInfoCards(this._layout)
  }

  public animateBalance(target: number) {
    const obj = { val: this._session.balance - this._session.lastWin }
    gsap.to(obj, {
      val: target,
      duration: 1.5,
      onUpdate: () => {
        this._balanceCard.value.text = Math.floor(obj.val).toLocaleString('en-US')
      },
    })
  }

  private _refreshButtonStates() {
    const idle = this._fsm.state === GameUIState.IDLE
    const spinEnabled = idle && !this._autoSpinActive
    this._spinButton.alpha = spinEnabled ? 1 : 0.4
    this._spinButton.interactive = spinEnabled
    const autoEnabled = this._autoSpinActive || idle
    this._autoBtn.alpha = autoEnabled ? 1 : 0.5
    this._autoBtn.interactive = autoEnabled
  }

  private _onStateChange() {
    const state = this._fsm.state

    if (state === GameUIState.WIN_SHOW) {
      this.updateTexts()
      gsap.fromTo(
        this._winCard.value.scale,
        { x: 1, y: 1 },
        { x: 1.18, y: 1.18, duration: 0.4, yoyo: true, repeat: 3 },
      )
    } else {
      this.updateTexts()
    }

    this._refreshButtonStates()
  }
}
