import { Application, Container, Graphics, Text } from 'pixi.js'
import { gsap } from 'gsap'
import { ReelSetBuilder, SpriteSymbol, SpeedPresets, driveGsapWithTicker } from 'pixi-reels'
import { config, SYMBOLS } from '@tgslots/nine-lives'
import type { LivesState, LivesResult } from '@tgslots/nine-lives'
import type { RevisionedResponse } from '@tgslots/shared-contracts/revisioned-session'
import { RevisionedGameApi, RevisionedApiError } from '../../engine/revisioned-api'
import { AssetRegistry } from '../../engine/asset-registry'
import { createReelArt } from '../../engine/reel-art'
import { WinOverlay } from '../../engine/win-overlay'
import { getResponsiveLayout } from '../../engine/layout'
import { getSpinSpeedProfile } from '../../engine/spin-speed'
import { manifest, assets } from './manifest'
import './style.css'
type Response = RevisionedResponse<LivesState, LivesResult>
const format = (value: number) => value.toLocaleString('en-US')
const frames = (grid: number[][]) =>
  grid.map((column) => ({ visible: column.map((id) => SYMBOLS[id]!) }))
const cell = (position: number) => ({ reel: Math.floor(position / 5), cell: position % 5 })
const titles = [
  'The Reaper · Wild',
  'Crossbones',
  'Fish Bones',
  'Crimson Yarn',
  'Midnight Candle',
  'Black Cat',
  'Paw Chip',
  'Hourglass · Scatter',
]
const wait = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms))
export async function launchNineLives(): Promise<void> {
  document.getElementById('game-loader')?.remove()
  document.title = 'Nine Lives — TGSlots'
  const root = document.createElement('main')
  root.className = 'nl-game'
  root.dataset.phase = 'BASE'
  root.innerHTML = `<header class="nl-top"><a href="${location.pathname}">← TG / SLOTS</a><span>VIRTUAL CREDITS</span><button class="nl-sound" aria-pressed="false">SOUND OFF</button></header>
  <section class="nl-layout"><header class="nl-brand"><p>ONE MORE SPIN BEFORE THE AFTERLIFE.</p><h1><span>NINE</span> LIVES<i>✦</i></h1><div>DEATH HAS A SOFT SPOT FOR CATS.</div></header>
  <div class="nl-cabinet"><div class="nl-marquee"><strong>THE MIDNIGHT SHIFT</strong><span>6 × 5 · CLUSTER PAYS</span></div><div class="nl-stage" role="img" aria-label="Six columns and five rows"><div class="nl-loading"><strong>OPENING AT MIDNIGHT</strong><small>Gathering the souls…</small><div><i></i></div></div></div><div class="nl-message" role="status" aria-live="polite">The Reaper is waiting.</div></div>
  <aside class="nl-feature"><div class="nl-lives"><span>NINE LIVES</span><div>${Array.from({ length: 9 }, (_, i) => `<i data-life="${i}">♥</i>`).join('')}</div><small>4 hourglasses unlock the afterlife</small></div><div class="nl-multiplier"><span>CASCADE MULTIPLIER</span><strong>×1</strong><small>Every winning cascade turns it up</small></div><div class="nl-reaper"><img src="/assets/images/nine-lives/reaper-hero.webp" alt="The cat Reaper holding a paw chip"/><div><span>BONUS BANK</span><strong>0</strong><small>He takes the chips. You keep the credits.</small></div></div></aside>
  <div class="nl-controls"><div><span>BALANCE</span><strong class="nl-balance">—</strong></div><div class="nl-bet"><span>BET</span><div><button class="nl-minus" aria-label="Decrease bet">−</button><strong>${config.baseCost}</strong><button class="nl-plus" aria-label="Increase bet">+</button></div></div><div><span>LAST WIN</span><strong class="nl-win">0</strong></div><button class="nl-spin" disabled>LOADING</button><button class="nl-buy" disabled>BUY NINE LIVES<small>${config.buyCost}× BET</small></button></div>
  <footer class="nl-bottom"><button class="nl-rules">THE FINE PRINT ↗</button><span>SAME TOMORROW?</span><button class="nl-turbo" aria-pressed="false">TURBO OFF</button></footer><div class="nl-error" role="alert" hidden></div><button class="nl-refresh" hidden>REFRESH SESSION</button></section>`
  document.body.append(root)
  const element = <T extends HTMLElement>(selector: string) => {
    const found = root.querySelector<T>(selector)
    if (!found) throw new Error(`Missing Nine Lives element ${selector}`)
    return found
  }
  const spin = element<HTMLButtonElement>('.nl-spin'),
    buy = element<HTMLButtonElement>('.nl-buy'),
    status = element('.nl-message')
  const api = new RevisionedGameApi<LivesState, LivesResult>(
    'nine-lives',
    import.meta.env.VITE_API_URL ?? '',
  )
  const app = new Application()
  await app.init({
    width: 746,
    height: 632,
    backgroundAlpha: 0,
    antialias: true,
    resolution: Math.min(devicePixelRatio || 1, 2),
    autoDensity: true,
  })
  element('.nl-stage').append(app.canvas)
  const stopGsap = driveGsapWithTicker(app.ticker, gsap)
  const registry = new AssetRegistry()
  const art = await registry.loadGame(manifest, assets, (loaded, total) => {
    element('.nl-loading i').style.width = `${Math.round((loaded / total) * 100)}%`
  })
  await document.fonts.ready
  const textures = createReelArt(app, art, SYMBOLS, 116)
  element('.nl-loading').remove()
  const background = new Graphics()
  for (let reel = 0; reel < 6; reel++)
    for (let row = 0; row < 5; row++)
      background
        .roundRect(10 + reel * 122, 10 + row * 122, 116, 116, 10)
        .fill({ color: (reel + row) % 2 ? 0x241817 : 0x1c1312 })
        .stroke({ color: 0xc78664, alpha: 0.13, width: 1 })
  app.stage.addChild(background)
  const reels = new ReelSetBuilder()
    .reels(6)
    .visibleCells(5)
    .symbolSize(116, 116)
    .symbolGap(6, 6)
    .symbols((reg) => {
      for (const id of SYMBOLS) reg.register(id, SpriteSymbol, { textures, anchor: { x: 0, y: 0 } })
    })
    .ticker(app.ticker)
    .gsap(gsap)
    .weights(Object.fromEntries(SYMBOLS.map((id, i) => [id, Math.max(1, config.weights[i]!)])))
    .tumble()
    .speed('normal', SpeedPresets.NORMAL)
    .speed('turbo', SpeedPresets.TURBO)
    .initialFrame(
      Array.from({ length: 6 }, () => ({ visible: ['BONE', 'FISH', 'YARN', 'CANDLE', 'CAT'] })),
    )
    .build()
  reels.position.set(10, 10)
  app.stage.addChild(reels)
  const labels = new Container()
  app.stage.addChild(labels)
  const particles = new Container()
  app.stage.addChild(particles)
  const shade = new Graphics().rect(0, 0, 746, 632).fill({ color: 0x0c0807, alpha: 0.88 })
  shade.alpha = 0
  app.stage.addChild(shade)
  const overlay = new WinOverlay()
  overlay.setGame(art, manifest.winTiers)
  overlay.setTypography('Bangers, Arial Black, sans-serif')
  overlay.resize({
    ...getResponsiveLayout(746, 632),
    reelBounds: { x: 10, y: 10, width: 726, height: 604 },
  })
  app.stage.addChild(overlay)
  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches
  let multiplier = 1,
    busy = false,
    turbo = false,
    muted = true,
    audio: AudioContext | null = null,
    current: Response | null = null
  function tone(frequency: number, seconds = 0.12): void {
    if (muted) return
    audio ??= new AudioContext()
    void audio.resume()
    const oscillator = audio.createOscillator(),
      gain = audio.createGain()
    oscillator.type = 'triangle'
    oscillator.frequency.setValueAtTime(frequency, audio.currentTime)
    oscillator.frequency.exponentialRampToValueAtTime(frequency * 0.6, audio.currentTime + seconds)
    gain.gain.setValueAtTime(0.035, audio.currentTime)
    gain.gain.exponentialRampToValueAtTime(0.001, audio.currentTime + seconds)
    oscillator.connect(gain)
    gain.connect(audio.destination)
    oscillator.start()
    oscillator.stop(audio.currentTime + seconds)
  }
  function embers(positions: readonly number[], collect = false): void {
    if (reduced) return
    for (const position of positions)
      for (let i = 0; i < 9; i++) {
        const particle = new Graphics()
          .circle(0, 0, 2 + (i % 3))
          .fill({ color: i % 3 === 0 ? 0xf4e6ca : 0xf15c35 })
        const origin = cell(position)
        particle.position.set(68 + origin.reel * 122, 68 + origin.cell * 122)
        particles.addChild(particle)
        const angle = i * 2.4 + position,
          distance = 30 + ((i * 13) % 60)
        gsap.to(particle, {
          x: collect ? 720 : particle.x + Math.cos(angle) * distance,
          y: collect ? 20 : particle.y + Math.sin(angle) * distance,
          alpha: 0,
          duration: collect ? 0.85 : 0.5,
          ease: 'power2.out',
          onComplete: () => particle.destroy(),
        })
      }
  }
  function coinLabels(coins: LivesResult['coins']): void {
    for (const child of labels.removeChildren()) child.destroy()
    for (const coin of coins) {
      const spot = cell(coin.position)
      const label = new Text({
        text: format(coin.value),
        style: {
          fontFamily: 'Barlow Condensed, Arial, sans-serif',
          fontSize: coin.value >= 100000 ? 23 : 29,
          fontWeight: '800',
          fill: 0xfff0d8,
          stroke: { color: 0x130c0a, width: 4 },
        },
      })
      label.anchor.set(0.5)
      label.position.set(68 + spot.reel * 122, 76 + spot.cell * 122)
      labels.addChild(label)
    }
  }
  function sync(response: Response): void {
    current = response
    const state = response.state,
      bonus = state.phase === 'FREE'
    root.dataset.phase = state.phase
    element('.nl-balance').textContent = format(response.balance)
    element('.nl-bet strong').textContent = format(config.baseCost * multiplier)
    element('.nl-multiplier strong').textContent =
      `×${bonus ? state.multiplier : (response.result?.steps.at(-1)?.multiplier ?? 1)}`
    element('.nl-lives small').textContent = bonus
      ? `${state.remaining} / 9 free spins remaining`
      : '4 hourglasses unlock the afterlife'
    root
      .querySelectorAll('.nl-lives i')
      .forEach((life, index) => life.classList.toggle('is-live', bonus && index < state.remaining))
    element('.nl-reaper strong').textContent = format(state.bonusWin)
    spin.textContent = api.hasPending ? 'RETRY' : bonus ? `NEXT LIFE · ${state.remaining}` : 'SPIN'
    spin.setAttribute('aria-label', spin.textContent)
    spin.disabled = busy
    buy.disabled = busy || bonus || api.hasPending
    buy.querySelector('small')!.textContent =
      `${format(config.baseCost * config.buyCost * multiplier)} CREDITS`
    for (const selector of ['.nl-minus', '.nl-plus'])
      element<HTMLButtonElement>(selector).disabled = busy || bonus || api.hasPending
    app.canvas.dataset.gameState = busy ? 'BUSY' : state.phase
    app.canvas.dataset.revision = String(response.revision)
    app.canvas.setAttribute('aria-busy', String(busy))
  }
  async function banner(copy: string, ms = 1100): Promise<void> {
    shade.alpha = 1
    await overlay.announce(copy, turbo ? 350 : ms)
    shade.alpha = 0
  }
  async function present(response: Response): Promise<void> {
    const result = response.result
    if (!result) {
      coinLabels([])
      response.state.lastGrid.forEach((column, reel) =>
        column.forEach((id, row) => reels.setSymbolAt(reel, row, SYMBOLS[id]!)),
      )
      return
    }
    const animation = reels.spin()
    reels.setResult(frames(result.grid))
    await animation
    tone(160)
    if (result.type === 'BUY') {
      await banner('YOU HAVE NINE LIVES')
      return
    }
    if (result.coins.length) {
      coinLabels(result.coins)
      root.dataset.collecting = 'true'
      status.textContent = `THE REAPER COLLECTS ${format(result.collectionWin)} CREDITS.`
      tone(440, 0.3)
      embers(
        result.coins.map((c) => c.position),
        true,
      )
      await wait(turbo || reduced ? 120 : 650)
      await reels.swapSymbols(
        result.coins.map((coin) => ({ ...cell(coin.position), id: 'WILD' })),
        { holdMs: turbo ? 30 : 120 },
      )
      coinLabels([])
      delete root.dataset.collecting
    }
    for (const step of result.steps) {
      const positions = step.vanished
      element('.nl-multiplier strong').textContent = `×${step.multiplier}`
      status.textContent = `${step.hits.map((hit) => `${hit.size} ${titles[hit.symbolId]}`).join(' + ')} · ${format(step.win)} CREDITS`
      await reels.spotlight.show(
        positions.map((position) => ({
          reelIndex: cell(position).reel,
          cellIndex: cell(position).cell,
        })),
      )
      tone(220 + step.multiplier * 45, 0.15)
      await wait(turbo || reduced ? 50 : 250)
      embers(positions)
      await reels.spotlight.hide()
      const winners = positions.map(cell)
      await reels.destroySymbols(winners)
      await reels.refill({ winners, grid: frames(step.after) })
    }
    element('.nl-win').textContent = format(result.win)
    if (result.win >= config.baseCost * response.state.triggeringMultiplier * 10) {
      root.dataset.celebrating = 'true'
      shade.alpha = 1
      await overlay.announceWin(result.win, config.baseCost * response.state.triggeringMultiplier)
      shade.alpha = 0
      delete root.dataset.celebrating
    }
    if (result.bonusTriggered) await banner('NINE LIVES. MAKE THEM COUNT.')
    status.textContent = result.capped
      ? '9,999× MAX WIN. THE REAPER BOWS.'
      : result.bonusEnded
        ? `BACK FROM THE DEAD · ${format(response.state.bonusWin)} BONUS CREDITS`
        : result.win
          ? `${format(result.win)} CREDITS. DEAD LUCKY.`
          : result.type === 'FREE'
            ? 'The Reaper is not done with you.'
            : 'Some nights are quieter than others.'
    if (result.bonusEnded) await banner('BACK FROM THE DEAD', 900)
  }
  function showError(error: Error): void {
    const box = element('.nl-error')
    box.hidden = false
    box.textContent = error.message
    element<HTMLButtonElement>('.nl-refresh').hidden = !(
      error instanceof RevisionedApiError && [404, 409].includes(error.status)
    )
  }
  async function run(purchase = false): Promise<void> {
    if (busy) return
    busy = true
    element('.nl-error').hidden = true
    element('.nl-refresh').hidden = true
    if (current) sync(current)
    try {
      const response = api.hasPending
        ? await api.retry()
        : await api.action(
            purchase ? 'buybonus' : current?.state.phase === 'FREE' ? 'next' : 'spin',
            multiplier,
          )
      await present(response)
      sync(response)
    } catch (error) {
      showError(error instanceof Error ? error : new Error('Unable to finish the spin'))
    } finally {
      busy = false
      if (api.response) sync(api.response)
      else {
        spin.textContent = 'RETRY'
        spin.disabled = false
      }
    }
  }
  async function restore(): Promise<void> {
    if (busy) return
    busy = true
    spin.disabled = true
    try {
      const response = await api.restore()
      multiplier = response.state.triggeringMultiplier
      await present(response)
      sync(response)
    } catch (error) {
      showError(error instanceof Error ? error : new Error('Unable to enter the casino'))
    } finally {
      busy = false
      if (api.response) sync(api.response)
      else {
        spin.textContent = 'RETRY'
        spin.disabled = false
      }
    }
  }
  function dialog(html: string, onClose?: (value: string) => void): void {
    const dialog = document.createElement('dialog')
    dialog.className = 'nl-dialog'
    dialog.innerHTML = html
    root.append(dialog)
    dialog.showModal()
    dialog.addEventListener('close', () => {
      onClose?.(dialog.returnValue)
      dialog.remove()
    })
  }
  spin.addEventListener('click', () => {
    if (!api.response && !api.hasPending) void restore()
    else void run()
  })
  buy.addEventListener('click', () =>
    dialog(
      `<h2>MAKE A DEAL WITH DEATH.</h2><img src="/assets/images/nine-lives/reaper-hero.webp" alt=""/><p>Nine free spins. The Reaper collects chips and turns them into Wild. Your multiplier carries between lives.</p><strong>${format(config.buyCost * config.baseCost * multiplier)} VIRTUAL CREDITS · ${config.buyCost}× BET</strong><form method="dialog"><button value="cancel">NOT TONIGHT</button><button value="buy">DEAL.</button></form>`,
      (value) => {
        if (value === 'buy') void run(true)
      },
    ),
  )
  for (const [selector, delta] of [
    ['.nl-minus', -1],
    ['.nl-plus', 1],
  ] as const)
    element(selector).addEventListener('click', () => {
      multiplier = Math.min(10000, Math.max(1, multiplier + delta))
      if (current) sync(current)
    })
  element('.nl-turbo').addEventListener('click', () => {
    turbo = !turbo
    const speed = turbo ? 'turbo' : 'normal'
    reels.setSpeed(speed)
    overlay.syncSpinSpeed(getSpinSpeedProfile(speed))
    element('.nl-turbo').textContent = turbo ? 'TURBO ON' : 'TURBO OFF'
    element('.nl-turbo').setAttribute('aria-pressed', String(turbo))
  })
  element('.nl-sound').addEventListener('click', () => {
    muted = !muted
    element('.nl-sound').textContent = muted ? 'SOUND OFF' : 'SOUND ON'
    element('.nl-sound').setAttribute('aria-pressed', String(!muted))
    tone(110, 0.4)
  })
  element('.nl-refresh').addEventListener('click', () => {
    api.resetExpiredSession()
    void restore()
  })
  element('.nl-rules').addEventListener('click', () =>
    dialog(
      `<h2>THE FINE PRINT.</h2><p>6 columns × 5 rows. Clusters of 5 or more matching symbols connected horizontally or vertically pay. Wild substitutes, and each Wild can belong to only one winning cluster per cascade. All-Wild groups do not pay alone.</p><p>Winning symbols disappear and gravity refills their cells. Multipliers begin at ×1 and rise by one after every winning cascade, up to ×${config.maxMultiplier}. The base-game multiplier resets every paid spin. A chain ends after a losing grid or ${config.maxCascades} winning cascades.</p><p>4+ hourglasses on the initial paid-spin grid award exactly ${config.freeSpins} free spins. There are no retriggers. Bonus multiplier starts at ×1 and carries across all nine lives.</p><p>At the start of each free spin, the Reaper collects every paw chip once at the current multiplier. Chips then become consumable Wild for that spin's cascades. Chips do not pay in the base game. Chip prizes: ${config.prizes.map(([value]) => `${value}×`).join(', ')} the triggering stake, before multiplier.</p><p>Round cap: ${format(config.maxWinX)}× original stake, including the triggering base win. Reaching the cap ends the bonus. Purchase costs ${config.buyCost}× original stake; free-spin payouts retain that original stake. Virtual credits and ephemeral sessions.</p><table><thead><tr><th>Symbol</th><th>5–6</th><th>7–8</th><th>9–10</th></tr></thead><tbody>${Object.entries(
        config.paytable,
      )
        .filter(([, pays]) => Object.keys(pays).length)
        .map(
          ([name, pays]) =>
            `<tr><th><img src="/assets/images/nine-lives/${name.toLowerCase()}.webp" alt=""/>${titles[SYMBOLS.indexOf(name as (typeof SYMBOLS)[number])]}</th>${[5, 7, 9].map((size) => `<td>${(pays as Record<string, number>)[size]}</td>`).join('')}</tr>`,
        )
        .join(
          '',
        )}</tbody></table><p>Paytable values × bet level × cascade multiplier. Bet level = total stake ÷ ${config.baseCost}.</p><details><summary>Full cluster paytable · 5–30 symbols</summary>${Object.entries(
        config.paytable,
      )
        .filter(([, pays]) => Object.keys(pays).length)
        .map(
          ([name, pays]) =>
            `<p><strong>${name}</strong>: ${Object.entries(pays)
              .map(([size, value]) => `${size} → ${value}`)
              .join(' · ')}</p>`,
        )
        .join('')}</details><form method="dialog"><button>GOT IT.</button></form>`,
    ),
  )
  document.addEventListener('keydown', (event) => {
    if (
      event.code === 'Space' &&
      !root.querySelector('dialog[open]') &&
      !(event.target instanceof HTMLButtonElement) &&
      !(event.target instanceof HTMLInputElement)
    ) {
      event.preventDefault()
      spin.click()
    }
  })
  window.addEventListener(
    'pagehide',
    () => {
      stopGsap()
      for (const p of particles.children) gsap.killTweensOf(p)
      overlay.destroy({ children: true })
      reels.destroy()
      app.destroy(true, { children: true })
      for (const texture of Object.values(textures)) texture.destroy(true)
      void registry.unloadGame(manifest.gameId)
      void audio?.close()
    },
    { once: true },
  )
  await restore()
}
