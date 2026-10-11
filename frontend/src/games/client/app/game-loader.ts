function toHex(n: number): string {
  return '#' + n.toString(16).padStart(6, '0')
}

export interface GameLoaderTheme {
  primary: number
  accent: number
  background: number
  text: number
}

export class GameLoader {
  private readonly _el: HTMLElement
  private readonly _title: HTMLElement
  private readonly _fill: HTMLElement
  private readonly _status: HTMLElement

  constructor() {
    this._el = document.getElementById('game-loader')!
    this._title = this._el.querySelector('.loader-title')!
    this._fill = this._el.querySelector('.loader-bar-fill')!
    this._status = this._el.querySelector('.loader-status')!
  }

  theme(opts: { theme: GameLoaderTheme; displayName: string; backgroundUrl?: string }): void {
    const { theme, displayName, backgroundUrl } = opts
    this._el.style.setProperty('--lb', toHex(theme.background))
    this._el.style.setProperty('--la', toHex(theme.primary))
    this._el.style.setProperty('--lt', toHex(theme.text))
    this._title.textContent = displayName.toUpperCase()
    if (backgroundUrl) {
      const img = new Image()
      img.onload = () => {
        this._el.style.setProperty('--lbg-img', `url('${backgroundUrl}')`)
      }
      img.src = backgroundUrl
    }
  }

  setProgress(loaded: number, total: number): void {
    const p = total > 0 ? Math.min(1, loaded / total) : 0
    this._fill.style.setProperty('--p', String(p))
    this._fill.style.width = `${p * 100}%`
  }

  awaitTap(): Promise<void> {
    this._el.classList.add('is-tap')
    this._status.textContent = 'TAP TO PLAY'
    this._el.tabIndex = 0
    this._el.setAttribute('role', 'button')
    this._el.setAttribute('aria-label', 'Start game')
    return new Promise((resolve) => {
      this._el.addEventListener('pointerdown', () => resolve(), { once: true })
      this._el.addEventListener('keydown', (e) => {
        if (e.code === 'Enter' || e.code === 'Space') {
          e.preventDefault()
          resolve()
        }
      })
    })
  }

  dismiss(): Promise<void> {
    this._el.classList.add('is-fade')
    return new Promise((resolve) => {
      const onEnd = () => {
        this._el.remove()
        resolve()
      }
      this._el.addEventListener('transitionend', onEnd, { once: true })
      // Fallback if transition doesn't fire (e.g. hidden tab)
      setTimeout(onEnd, 500)
    })
  }
}
