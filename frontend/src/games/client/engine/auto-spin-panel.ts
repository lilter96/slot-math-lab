import { Container, Graphics, Text } from 'pixi.js'
import { gsap } from 'gsap'
import type { AutoSpinConfig } from '../types'
import type { UILayoutSnapshot } from './layout'

const PRESETS: Array<{ label: string; value: number }> = [
  { label: '10', value: 10 },
  { label: '25', value: 25 },
  { label: '50', value: 50 },
  { label: '100', value: 100 },
  { label: '∞', value: 0 },
]

interface PresetChip {
  container: Container
  background: Graphics
  label: Text
  value: number
}

interface ToggleControl {
  container: Container
  box: Graphics
  check: Text
  label: Text
}

export class AutoSpinPanel extends Container {
  private _selectedSpins = 10
  private _stopOnWin = false
  private _stopOnBonus = false

  private _layout?: UILayoutSnapshot
  private _inner: Container
  private _backdrop: Graphics
  private _panelBg: Graphics
  private _divider: Graphics
  private _title: Text
  private _sectionSpins: Text
  private _sectionStop: Text
  private _closeBtn: Container
  private _closeBg: Graphics
  private _closeLabel: Text
  private _chips: PresetChip[] = []
  private _winToggle: ToggleControl
  private _bonusToggle: ToggleControl
  private _startBtn: Container
  private _startBg: Graphics
  private _startLabel: Text
  private _innerBaseY = 0
  private _chipWidth = 72
  private _chipHeight = 50

  constructor() {
    super()
    this.visible = false
    this._inner = new Container()
    this._backdrop = new Graphics()
    this._panelBg = new Graphics()
    this._divider = new Graphics()
    this._title = new Text({
      text: 'AUTO SPIN',
      style: {
        fill: '#d0d0ff',
        fontSize: 24,
        fontWeight: 'bold',
        letterSpacing: 3,
      },
    })
    this._sectionSpins = this._sectionLabel('NUMBER OF SPINS')
    this._sectionStop = this._sectionLabel('STOP CONDITIONS')
    this._closeBg = new Graphics()
    this._closeLabel = new Text({ text: '✕', style: { fill: '#9090c0', fontSize: 16 } })
    this._closeBtn = new Container()
    this._startBg = new Graphics()
    this._startLabel = new Text({
      text: 'START AUTO SPIN',
      style: { fill: '#ffffff', fontSize: 20, fontWeight: 'bold', letterSpacing: 1 },
    })
    this._startBtn = new Container()
    this._winToggle = this._makeToggle('Stop on any win')
    this._bonusToggle = this._makeToggle('Stop on bonus')
    this._build()
  }

  private _build() {
    this._backdrop.interactive = true
    this._backdrop.on('pointerdown', () => this.hide())
    this.addChild(this._backdrop)

    this._panelBg.interactive = true
    this._panelBg.on('pointerdown', (e) => e.stopPropagation())
    this._inner.addChild(this._panelBg, this._divider)

    this._title.anchor.set(0.5, 0)
    this._inner.addChild(this._title, this._sectionSpins, this._sectionStop)

    this._closeLabel.anchor.set(0.5)
    this._closeBtn.addChild(this._closeBg, this._closeLabel)
    this._closeBtn.interactive = true
    this._closeBtn.cursor = 'pointer'
    this._closeBtn.on('pointerdown', (e) => {
      e.stopPropagation()
      this.hide()
    })
    this._inner.addChild(this._closeBtn)

    PRESETS.forEach(({ label, value }) => {
      const background = new Graphics()
      const text = new Text({
        text: label,
        style: { fill: '#ffffff', fontSize: 22, fontWeight: 'bold' },
      })
      text.anchor.set(0.5)

      const container = new Container()
      container.addChild(background, text)
      container.interactive = true
      container.cursor = 'pointer'
      container.on('pointerdown', (e) => {
        e.stopPropagation()
        this._selectedSpins = value
        this._refreshChips()
      })

      this._chips.push({ container, background, label: text, value })
      this._inner.addChild(container)
    })

    this._winToggle.container.on('pointerdown', (e) => {
      e.stopPropagation()
      this._stopOnWin = !this._stopOnWin
      this._refreshToggle(this._winToggle, this._stopOnWin)
    })
    this._bonusToggle.container.on('pointerdown', (e) => {
      e.stopPropagation()
      this._stopOnBonus = !this._stopOnBonus
      this._refreshToggle(this._bonusToggle, this._stopOnBonus)
    })
    this._inner.addChild(this._winToggle.container, this._bonusToggle.container)

    this._startLabel.anchor.set(0.5)
    this._startBtn.addChild(this._startBg, this._startLabel)
    this._startBtn.interactive = true
    this._startBtn.cursor = 'pointer'
    this._startBtn.on('pointerover', () => {
      this._startBg.tint = 0xaaddaa
    })
    this._startBtn.on('pointerout', () => {
      this._startBg.tint = 0xffffff
    })
    this._startBtn.on('pointerdown', (e) => {
      e.stopPropagation()
      const config: AutoSpinConfig = {
        spins: this._selectedSpins,
        stopOnWin: this._stopOnWin,
        stopOnBonus: this._stopOnBonus,
      }
      this.emit('start', config)
      this.hide()
    })
    this._inner.addChild(this._startBtn)

    this.addChild(this._inner)
    this._refreshChips()
    this._refreshToggle(this._winToggle, false)
    this._refreshToggle(this._bonusToggle, false)
  }

  public resize(layout: UILayoutSnapshot) {
    this._layout = layout

    this._backdrop.clear()
    this._backdrop.rect(0, 0, layout.screenWidth, layout.screenHeight)
    this._backdrop.fill({ color: 0x000000, alpha: 0.65 })

    const maxWidth = Math.min(480, layout.modalBounds.width)
    const panelWidth = Math.max(280, maxWidth)
    const padding = panelWidth < 380 ? 18 : 24
    const closeSize = 30
    const dividerY = 54
    const titleY = 24
    const chipGap = 10
    const chipCols = panelWidth < 420 ? 3 : 5
    const chipRows = Math.ceil(PRESETS.length / chipCols)
    const chipWidth =
      chipCols === 5
        ? Math.min(72, (panelWidth - padding * 2 - chipGap * (chipCols - 1)) / chipCols)
        : Math.min(88, (panelWidth - padding * 2 - chipGap * (chipCols - 1)) / chipCols)
    const chipHeight = chipWidth < 76 ? 44 : 50
    const toggleStacked = panelWidth < 430
    const toggleHeight = 26
    const toggleGap = 16
    const startWidth = Math.min(260, panelWidth - padding * 2)
    const startHeight = 56
    const chipsTop = dividerY + 36
    const chipsBlockHeight = chipRows * chipHeight + (chipRows - 1) * chipGap
    const togglesTop = chipsTop + chipsBlockHeight + 66
    const togglesBlockHeight = toggleStacked ? toggleHeight * 2 + toggleGap : toggleHeight
    const startTop = togglesTop + togglesBlockHeight + 44
    const panelHeight = startTop + startHeight + 22

    const innerScale = Math.min(1, layout.modalBounds.height / panelHeight)

    this._inner.scale.set(innerScale)
    this._inner.x = layout.modalBounds.x + layout.modalBounds.width / 2
    this._innerBaseY = layout.modalBounds.y + layout.modalBounds.height / 2
    this._inner.y = this._innerBaseY

    this._panelBg.clear()
    this._panelBg.roundRect(-panelWidth / 2, -panelHeight / 2, panelWidth, panelHeight, 16)
    this._panelBg.fill(0x14142a)
    this._panelBg.stroke({ width: 1.5, color: 0x38386a })

    this._title.style.fontSize = panelWidth < 380 ? 20 : 24
    this._title.x = 0
    this._title.y = -panelHeight / 2 + titleY

    this._closeBg.clear()
    this._closeBg.roundRect(-closeSize / 2, -closeSize / 2, closeSize, closeSize, 6)
    this._closeBg.fill(0x2a2a4a)
    this._closeBtn.x = panelWidth / 2 - padding + closeSize / 2 - 4
    this._closeBtn.y = -panelHeight / 2 + titleY + closeSize / 2 - 2

    this._divider.clear()
    this._divider.rect(
      -panelWidth / 2 + padding,
      -panelHeight / 2 + dividerY,
      panelWidth - padding * 2,
      1,
    )
    this._divider.fill(0x38386a)

    this._sectionSpins.x = 0
    this._sectionSpins.y = -panelHeight / 2 + dividerY + 18
    this._sectionStop.x = 0
    this._sectionStop.y = togglesTop - 28 - panelHeight / 2

    this._chips.forEach((chip, index) => {
      const col = index % chipCols
      const row = Math.floor(index / chipCols)
      const rowCount =
        row === chipRows - 1 && PRESETS.length % chipCols !== 0
          ? PRESETS.length % chipCols
          : chipCols
      const rowWidth = rowCount * chipWidth + (rowCount - 1) * chipGap
      const rowStart = -rowWidth / 2 + chipWidth / 2

      chip.container.x = rowStart + col * (chipWidth + chipGap)
      chip.container.y = -panelHeight / 2 + chipsTop + row * (chipHeight + chipGap)
      chip.label.style.fontSize = chipWidth < 76 ? 18 : 22
      this._drawChip(chip, chipWidth, chipHeight)
    })

    this._chipWidth = chipWidth
    this._chipHeight = chipHeight

    const toggleWidth = toggleStacked
      ? panelWidth - padding * 2
      : (panelWidth - padding * 2 - toggleGap) / 2
    this._layoutToggle(
      this._winToggle,
      -panelWidth / 2 + padding,
      -panelHeight / 2 + togglesTop,
      toggleWidth,
    )
    this._layoutToggle(
      this._bonusToggle,
      toggleStacked
        ? -panelWidth / 2 + padding
        : -panelWidth / 2 + padding + toggleWidth + toggleGap,
      -panelHeight / 2 + togglesTop + (toggleStacked ? toggleHeight + toggleGap : 0),
      toggleWidth,
    )

    this._startBg.clear()
    this._startBg.roundRect(-startWidth / 2, -startHeight / 2, startWidth, startHeight, 10)
    this._startBg.fill(0x1b5e20)
    this._startLabel.style.fontSize = startWidth < 220 ? 17 : 20
    this._startBtn.x = 0
    this._startBtn.y = -panelHeight / 2 + startTop + startHeight / 2
  }

  private _sectionLabel(text: string): Text {
    const label = new Text({
      text,
      style: { fill: '#6868a0', fontSize: 12, letterSpacing: 2 },
    })
    label.anchor.set(0.5, 0)
    return label
  }

  private _makeToggle(label: string): ToggleControl {
    const box = new Graphics()
    const check = new Text({
      text: '✓',
      style: { fill: '#ffffff', fontSize: 13, fontWeight: 'bold' },
    })
    check.anchor.set(0.5)

    const labelText = new Text({
      text: label,
      style: { fill: '#b0b0d8', fontSize: 15 },
    })
    labelText.anchor.set(0, 0.5)

    const container = new Container()
    container.addChild(box, check, labelText)
    container.interactive = true
    container.cursor = 'pointer'

    return { container, box, check, label: labelText }
  }

  private _layoutToggle(toggle: ToggleControl, x: number, y: number, width: number) {
    toggle.container.x = x
    toggle.container.y = y
    toggle.check.x = 11
    toggle.check.y = 0
    toggle.label.style.fontSize = width < 170 ? 14 : 15
    toggle.label.anchor.set(0, 0.5)
    toggle.label.x = 32
    toggle.label.y = 0
  }

  private _drawChip(chip: PresetChip, width: number, height: number) {
    const selected = chip.value === this._selectedSpins
    chip.background.clear()
    chip.background.roundRect(-width / 2, -height / 2, width, height, 9)
    chip.background.fill(selected ? 0x2d2d6a : 0x1e1e3a)
    chip.background.stroke({ width: selected ? 2.5 : 1, color: selected ? 0x8080ff : 0x38386a })
  }

  private _refreshChips() {
    this._chips.forEach((chip) => {
      this._drawChip(chip, this._chipWidth, this._chipHeight)
    })
  }

  private _refreshToggle(toggle: ToggleControl, active: boolean) {
    toggle.box.clear()
    toggle.box.roundRect(0, -11, 22, 22, 5)
    toggle.box.fill(active ? 0x1b5e20 : 0x1e1e3a)
    toggle.box.stroke({ width: 1.5, color: active ? 0x4caf50 : 0x38386a })
    toggle.check.visible = active
  }

  public show() {
    this.visible = true
    this.alpha = 0
    this._inner.y = this._innerBaseY + 24
    gsap.to(this, { alpha: 1, duration: 0.2, ease: 'power2.out' })
    gsap.to(this._inner, { y: this._innerBaseY, duration: 0.28, ease: 'back.out(1.7)' })
  }

  public hide() {
    gsap.to(this, {
      alpha: 0,
      duration: 0.16,
      ease: 'power2.in',
      onComplete: () => {
        this.visible = false
        this._inner.y = this._innerBaseY
      },
    })
  }
}
