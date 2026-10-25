import type { AssetManifest, GameManifest } from '@tgslots/shared-contracts'

import type { SoundManager } from '../engine/sound-manager.js'
import { appendGameRules } from './game-rules.js'

export interface ShellActions {
  spin: () => void
  toggleSound: () => void
  stopAuto: () => void
  stepBet: (delta: number) => void
}

/** Semantic controls complement the canvas, including keyboard and touch users. */
export class GameShell {
  private readonly _header = document.createElement('header')
  private readonly _dialog = document.createElement('dialog')
  private readonly _toast = document.createElement('div')

  constructor(
    manifest: GameManifest,
    actions: ShellActions,
    assets: AssetManifest,
    sound: SoundManager,
  ) {
    this._header.className = 'game-shell'
    const identity = document.createElement('div')
    identity.className = 'game-identity'
    const brand = document.createElement('span')
    brand.className = 'game-brand'
    brand.textContent = 'TG'
    const name = document.createElement('h1')
    name.textContent = manifest.displayName
    identity.append(brand, name)
    const controls = document.createElement('nav')
    controls.setAttribute('aria-label', 'Game controls')
    const back = document.createElement('a')
    back.href = location.pathname
    back.textContent = 'Games'
    const button = (label: string, text: string, action: () => void) => {
      const element = document.createElement('button')
      element.type = 'button'
      element.setAttribute('aria-label', label)
      element.textContent = text
      element.addEventListener('click', action)
      return element
    }
    const soundButton = button('Toggle game sound', 'Sound', actions.toggleSound)
    const rulesButton = button('Game rules and controls', '?', () => {
      actions.stopAuto()
      this._dialog.showModal()
    })
    const fullscreen = button('Toggle fullscreen', '⛶', () => {
      if (document.fullscreenElement) void document.exitFullscreen()
      else
        void document.documentElement
          .requestFullscreen()
          .catch(() => this.showError('Fullscreen is unavailable in this browser.'))
    })
    controls.append(back, soundButton, rulesButton, fullscreen)
    this._header.append(identity, controls)

    this._dialog.className = 'game-rules'
    this._dialog.setAttribute('aria-labelledby', 'game-rules-title')
    const title = document.createElement('h2')
    title.id = 'game-rules-title'
    title.textContent = manifest.displayName
    const description = document.createElement('p')
    description.textContent = 'Personal game prototype · demo credits'
    description.className = 'rules-context'
    const keyboard = document.createElement('p')
    keyboard.textContent = 'Space: spin · M: sound · Esc: close this panel'
    const bet = document.createElement('div')
    bet.className = 'rules-bet'
    bet.append(
      button('Decrease bet', '− Bet', () => actions.stepBet(-1)),
      button('Increase bet', '+ Bet', () => actions.stepBet(1)),
    )
    this._dialog.append(
      title,
      description,
      keyboard,
      bet,
      button('Close game rules', 'Back to game', () => this._dialog.close()),
    )
    const rulesContent = document.createElement('div')
    appendGameRules(rulesContent, manifest, assets)
    this._dialog.insertBefore(rulesContent, keyboard)
    const audioControls = document.createElement('fieldset')
    audioControls.className = 'rules-audio'
    const legend = document.createElement('legend')
    legend.textContent = 'Audio mix'
    audioControls.append(legend)
    for (const [title, initial, update] of [
      ['Music', sound.bgmVolume, (value: number) => sound.setBGMVolume(value)],
      ['Effects', sound.sfxVolume, (value: number) => sound.setSFXVolume(value)],
    ] as const) {
      const label = document.createElement('label')
      const name = document.createElement('span')
      name.textContent = title
      const range = document.createElement('input')
      range.type = 'range'
      range.min = '0'
      range.max = '100'
      range.step = '1'
      range.value = String(Math.round(initial * 100))
      range.setAttribute('aria-label', `${title} volume`)
      const output = document.createElement('output')
      output.textContent = `${range.value}%`
      range.addEventListener('input', () => {
        update(Number(range.value) / 100)
        output.textContent = `${range.value}%`
      })
      label.append(name, range, output)
      audioControls.append(label)
    }
    this._dialog.insertBefore(audioControls, keyboard)
    this._dialog.addEventListener('click', (e) => {
      if (e.target === this._dialog) {
        const bounds = this._dialog.getBoundingClientRect()
        if (
          e.clientX < bounds.left ||
          e.clientX > bounds.right ||
          e.clientY < bounds.top ||
          e.clientY > bounds.bottom
        )
          this._dialog.close()
      }
    })
    this._toast.className = 'game-toast'
    this._toast.setAttribute('role', 'status')
    this._toast.hidden = true
    document.body.append(this._header, this._dialog, this._toast)
    window.addEventListener('keydown', (e) => {
      if (
        this._dialog.open ||
        e.repeat ||
        (e.target instanceof HTMLElement && e.target.closest('button, a, input, select, textarea'))
      )
        return
      if (e.code === 'Space') {
        e.preventDefault()
        actions.spin()
      }
      if (e.code === 'KeyM') actions.toggleSound()
    })
  }

  syncSound(muted: boolean) {
    const button = this._header.querySelector<HTMLButtonElement>('[aria-label="Toggle game sound"]')
    if (button) {
      button.textContent = muted ? 'Muted' : 'Sound'
      button.setAttribute('aria-pressed', String(!muted))
    }
  }

  showError(message: string) {
    this._toast.textContent = message
    this._toast.hidden = false
    window.setTimeout(() => {
      this._toast.hidden = true
    }, 7000)
  }
}
