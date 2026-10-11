import { Application, Container, Graphics, Text } from 'pixi.js'
import { gsap } from 'gsap'
import {
  ReelSetBuilder,
  HoldAndWinBuilder,
  SpriteSymbol,
  SpeedPresets,
  driveGsapWithTicker,
  enableDebug,
} from 'pixi-reels'
import type { HwCoin } from 'pixi-reels'
import { SYMBOLS, PAYLINES, config } from '@tgslots/x7-club'
import type { ClubCoin, ClubResponse } from '@tgslots/x7-club'
import { ClubApi, ClubApiError } from './api'
import { AssetRegistry } from '../../engine/asset-registry'
import { WinOverlay } from '../../engine/win-overlay'
import { getResponsiveLayout } from '../../engine/layout'
import { getSpinSpeedProfile } from '../../engine/spin-speed'
import { manifest, assets } from './manifest'
import { buildTextures, symbolTitles } from './symbols'
import './style.css'

const ATTRACT_GRID = [
  [4, 1, 0],
  [2, 3, 1],
  [5, 4, 3],
  [1, 0, 2],
  [3, 2, 4],
]
const format = (n: number) => n.toLocaleString('en-US')
const wait = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms))
const coordinate = (coin: ClubCoin) => ({
  reel: Math.floor(coin.position / 3),
  cell: coin.position % 3,
})
const hwCoins = (coins: ClubCoin[]): HwCoin<ClubCoin>[] =>
  coins.map((coin) => ({ cell: coordinate(coin), id: 'COIN', data: coin }))

export async function launchX7Club(): Promise<void> {
  document.getElementById('game-loader')?.remove()
  document.title = 'X7 Club — TGSlots'
  const root = document.createElement('main')
  root.className = 'x7-club'
  root.innerHTML = `
    <header class="x7-header"><a href="${location.pathname}" aria-label="Back to game library">← TG / SLOTS</a><span class="x7-demo">DEMO CREDITS</span><button class="x7-sound" aria-label="Enable sound">SOUND OFF</button></header>
    <section class="x7-main"><div class="x7-heading"><p>WELCOME TO THE AFTERPARTY</p><h1>X7<span>CLUB</span><i>✦</i></h1><div class="x7-subtitle">HOLD • SPIN • GET THE BIG MOOD</div></div>
    <img class="x7-mascot" src="/assets/images/x7-club/wild-hero.webp" alt="The capybara club boss"/><span class="x7-mascot-note">GOOD VIBES ONLY.</span><div class="x7-machine"><div class="x7-loading"><strong>OPENING THE CLUB</strong><small>GETTING YOUR GOOD VIBES READY</small><div class="x7-loading-track"><i></i></div></div><div class="x7-prizes">${['MINI', 'MAJOR', 'MEGA'].map((tier) => `<div data-tier="${tier}"><span>${tier}</span><strong>${config.prizes.find(([name]) => name === tier)![1]}×</strong><small>FIXED PRIZE</small></div>`).join('')}</div>
      <div class="x7-canvas" role="img" aria-label="Five reels with three rows"><div class="x7-rail">${Array.from({ length: 5 }, (_, column) => `<div data-column="${column}"><span>COLLECT</span><strong>0 / 3</strong></div>`).join('')}</div></div>
      <div class="x7-status" role="status" aria-live="polite">Opening the club…</div>
    </div>
    <div class="x7-feature-banner"><span class="x7-feature-title">FILL A COLUMN<small>Collect 3 coins to unlock its booster</small></span><div class="x7-respins" aria-label="Respins remaining"><i>1</i><i>2</i><i>3</i></div></div><div class="x7-controls"><div class="x7-wallet"><span>BALANCE</span><strong class="x7-balance">—</strong></div><div class="x7-bet"><span>BET</span><div><button class="x7-minus" aria-label="Decrease bet">−</button><strong class="x7-stake">${config.baseCost}</strong><button class="x7-plus" aria-label="Increase bet">+</button></div></div><div class="x7-win"><span>LAST WIN</span><strong>0</strong></div><button class="x7-spin" disabled>LOADING</button><button class="x7-buy" disabled>BUY BONUS<span>${config.buyCost}× BET</span></button></div>
    <div class="x7-bottom"><button class="x7-rules">HOW TO PLAY ↗</button><span class="x7-bank">YOUR BONUS: 0</span><button class="x7-turbo" aria-pressed="false">TURBO OFF</button></div>
    <p class="x7-footnote">20 lines · 6 coins unlock Hold & Spin · full columns unlock a booster</p>
    <div class="x7-error" role="alert" hidden></div>
    <button class="x7-refresh" hidden>REFRESH SESSION</button>
    </section>`
  document.body.append(root)
  const element = <T extends HTMLElement>(selector: string) => {
    const found = root.querySelector<T>(selector)
    if (!found) throw new Error(`Missing X7 element: ${selector}`)
    return found
  }
  const holder = element<HTMLDivElement>('.x7-canvas')
  const spinButton = element<HTMLButtonElement>('.x7-spin')
  const buyButton = element<HTMLButtonElement>('.x7-buy')
  const status = element<HTMLDivElement>('.x7-status')
  const errorBox = element<HTMLDivElement>('.x7-error')
  const refresh = element<HTMLButtonElement>('.x7-refresh')
  const api = new ClubApi(import.meta.env.VITE_API_URL ?? '')
  const app = new Application()
  await app.init({
    width: 700,
    height: 520,
    backgroundAlpha: 0,
    antialias: true,
    resolution: Math.min(devicePixelRatio || 1, 2),
    autoDensity: true,
  })
  holder.append(app.canvas)
  const stopGsap = driveGsapWithTicker(app.ticker, gsap)
  const assetRegistry = new AssetRegistry()
  const loadedAssets = await assetRegistry.loadGame(manifest, assets, (loaded, total) => {
    element<HTMLElement>('.x7-loading-track i').style.width =
      `${Math.round((loaded / total) * 100)}%`
  })
  const textures = buildTextures(app, loadedAssets)
  element('.x7-loading').remove()
  const background = new Graphics()
  for (let column = 0; column < 5; column++) {
    background
      .roundRect(4 + column * 140, 100, 132, 412, 14)
      .fill({ color: column % 2 ? 0x210d30 : 0x180a25 })
      .stroke({ color: 0xb675ae, width: 1, alpha: 0.25 })
    for (const y of [236, 376])
      background
        .moveTo(13 + column * 140, y)
        .lineTo(127 + column * 140, y)
        .stroke({ color: 0x925991, width: 1, alpha: 0.15 })
  }
  app.stage.addChild(background)
  const register = (registry: Parameters<Parameters<ReelSetBuilder['symbols']>[0]>[0]) => {
    for (const id of Object.keys(textures))
      registry.register(id, SpriteSymbol, {
        textures,
        anchor: { x: 0, y: 0 },
      })
  }
  const reels = new ReelSetBuilder()
    .reels(5)
    .visibleCells(3)
    .symbolSize(132, 132)
    .symbolGap(8, 8)
    .symbols(register)
    .ticker(app.ticker)
    .gsap(gsap)
    .weights({ CHILL: 25, HYPE: 23, LOL: 20, GG: 17, SEVEN: 12, WILD: 3, COIN: 9 })
    .speed('normal', SpeedPresets.NORMAL)
    .speed('turbo', SpeedPresets.TURBO)
    .initialFrame(Array.from({ length: 5 }, () => ({ visible: ['CHILL', 'HYPE', 'LOL'] })))
    .build()
  reels.position.set(4, 100)
  app.stage.addChild(reels)
  enableDebug(reels)
  const board = new HoldAndWinBuilder<ClubCoin>()
    .grid(5, 3)
    .cellSize(132, { gap: 8 })
    .symbols(register)
    .weights({ COIN: 1, empty: 3 })
    .respins(3)
    .lockAnimation('none')
    .speeds({ normal: SpeedPresets.NORMAL, turbo: SpeedPresets.TURBO })
    .ticker(app.ticker)
    .cellChrome((g, width, height) =>
      g
        .roundRect(3, 3, width - 6, height - 6, 22)
        .fill({ color: 0x26112e, alpha: 0.85 })
        .stroke({ color: 0xe7b86a, alpha: 0.25, width: 1.5 }),
    )
    .build()
  board.container.position.set(4, 100)
  board.container.visible = false
  app.stage.addChild(board.container)
  const labels = new Container()
  labels.position.set(4, 100)
  app.stage.addChild(labels)
  const booster = new ReelSetBuilder()
    .reels(1)
    .visibleCells(1)
    .symbolSize(132, 76)
    .symbols(register)
    .weights({ STOP: 65, PLUS1: 24, PLUS2: 10, X7: 1 })
    .speed('normal', SpeedPresets.NORMAL)
    .speed('turbo', SpeedPresets.TURBO)
    .ticker(app.ticker)
    .gsap(gsap)
    .initialFrame([{ visible: ['X7'] }])
    .build()
  booster.position.set(284, 10)
  app.stage.addChild(booster)
  booster.visible = false
  const activeColumn = new Graphics()
  app.stage.addChild(activeColumn)
  const particles = new Container()
  app.stage.addChild(particles)
  const veil = new Graphics().rect(0, 0, 700, 520).fill({ color: 0x0c031b, alpha: 0.82 })
  veil.alpha = 0
  app.stage.addChild(veil)
  const winOverlay = new WinOverlay()
  winOverlay.setGame(loadedAssets, manifest.winTiers)
  winOverlay.setTypography('Bungee, Arial Black, sans-serif')
  winOverlay.resize({
    ...getResponsiveLayout(700, 520),
    reelBounds: { x: 4, y: 100, width: 692, height: 412 },
  })
  app.stage.addChild(winOverlay)
  const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)').matches
  function burst(x: number, y: number, color = 0xffd580): void {
    if (reducedMotion) return
    for (let i = 0; i < 22; i++) {
      const particle = new Graphics()
        .star(0, 0, 4, 2 + (i % 4), 1)
        .fill({ color: i % 3 === 0 ? 0xff8bda : color })
      particle.position.set(x, y)
      particles.addChild(particle)
      const angle = i * 2.39996
      const distance = 35 + ((i * 17) % 95)
      gsap.to(particle, {
        x: x + Math.cos(angle) * distance,
        y: y + Math.sin(angle) * distance + 20,
        alpha: 0,
        rotation: angle,
        duration: 0.7 + (i % 4) * 0.07,
        ease: 'power2.out',
        onComplete: () => particle.destroy(),
      })
    }
  }
  async function celebrate(amount: number): Promise<void> {
    const stake = config.baseCost * (api.response?.state.triggeringMultiplier ?? multiplier)
    if (amount < stake * 10) return
    if (!reducedMotion) {
      burst(160, 230)
      burst(540, 230)
      burst(350, 120)
    }
    veil.alpha = 1
    root.dataset.celebrating = 'true'
    try {
      await winOverlay.announceWin(amount, stake)
    } finally {
      veil.alpha = 0
      delete root.dataset.celebrating
    }
  }
  let busy = false
  let multiplier = 1
  let turbo = false
  let muted = true
  let audio: AudioContext | null = null
  let previousCoins: ClubCoin[] = []
  let lastResponse: ClubResponse | null = null
  function tone(frequency: number, seconds = 0.08): void {
    if (muted) return
    audio ??= new AudioContext()
    void audio.resume()
    const oscillator = audio.createOscillator()
    const gain = audio.createGain()
    oscillator.type = 'triangle'
    oscillator.frequency.value = frequency
    gain.gain.setValueAtTime(0.045, audio.currentTime)
    gain.gain.exponentialRampToValueAtTime(0.001, audio.currentTime + seconds)
    oscillator.connect(gain).connect(audio.destination)
    oscillator.start()
    oscillator.stop(audio.currentTime + seconds)
  }
  reels.events.on('spin:reelLanded', (index) =>
    tone([261.6, 329.6, 392, 523.2, 659.2][index] ?? 659.2),
  )
  board.events.on('coin:locked', () => tone(780))
  function drawLabels(coins: ClubCoin[]): void {
    for (const child of labels.removeChildren()) child.destroy()
    for (const coin of coins) {
      const { x, y } = board.cellCenter(coordinate(coin))
      const value = new Text({
        text: coin.value ? format(coin.value) : '✦',
        style: {
          fontFamily: 'Outfit, Arial Black, sans-serif',
          fontSize: coin.value >= 100000 ? 23 : 31,
          fontWeight: '900',
          fill: 0xfff1c2,
          stroke: { color: 0x351226, width: 3 },
          dropShadow: { color: 0x000000, alpha: 0.9, blur: 4, distance: 2 },
        },
      })
      value.anchor.set(0.5)
      value.position.set(x, y + 7)
      labels.addChild(value)
      const tier = new Text({
        text: coin.tier === 'CREDIT' ? 'CREDITS' : coin.tier,
        style: {
          fontFamily: 'Outfit, Arial, sans-serif',
          fontSize: 13,
          fontWeight: '700',
          fill: 0xffca84,
          letterSpacing: 1,
        },
      })
      tier.anchor.set(0.5)
      tier.position.set(x, y - 20)
      labels.addChild(tier)
    }
  }
  function showBoard(coins: ClubCoin[]): void {
    board.reset()
    board.enter(hwCoins(coins))
    board.container.visible = true
    reels.visible = false
    previousCoins = structuredClone(coins)
    drawLabels(coins)
  }
  function sync(response: ClubResponse): void {
    lastResponse = response
    const bonus = response.state.bonus
    root.dataset.phase = response.state.phase
    booster.visible = response.state.phase === 'BOOST'
    activeColumn.clear()
    if (response.state.phase === 'BOOST')
      activeColumn
        .roundRect(5 + bonus!.pendingColumns[0]! * 140, 101, 130, 410, 15)
        .stroke({ color: 0xffc885, width: 3, alpha: 0.9 })
    for (let column = 0; column < 5; column++) {
      const item = element<HTMLElement>(`.x7-rail [data-column="${column}"]`)
      const count = (bonus?.coins ?? response.result?.coins ?? []).filter(
        (coin) => Math.floor(coin.position / 3) === column,
      ).length
      item.querySelector('strong')!.textContent = `${count} / 3`
      item.classList.toggle('is-full', count === 3)
      item.classList.toggle(
        'is-boosting',
        response.state.phase === 'BOOST' && bonus!.pendingColumns[0] === column,
      )
      item.querySelector('span')!.textContent = bonus?.pendingColumns.includes(column)
        ? 'BOOST READY'
        : bonus?.boostedColumns.includes(column)
          ? 'BANKED'
          : 'COLLECT'
    }
    const respins = root.querySelectorAll('.x7-respins i')
    respins.forEach((dot, index) =>
      dot.classList.toggle('is-live', !!bonus && index < bonus.respins),
    )
    element('.x7-respins').setAttribute(
      'aria-label',
      bonus ? `${bonus.respins} respins remaining` : 'Bonus inactive',
    )
    const featureTitle = element('.x7-feature-title')
    featureTitle.innerHTML =
      response.state.phase === 'BOOST'
        ? 'COLUMN BOOSTER<small>Extra credits or a rare ×7. Make it count.</small>'
        : bonus
          ? 'HOLD & SPIN<small>New coins reset your three respins</small>'
          : 'FILL A COLUMN<small>Collect 3 coins to unlock its booster</small>'
    element('.x7-balance').textContent = format(response.balance)
    element('.x7-stake').textContent = format(config.baseCost * multiplier)
    element('.x7-bank').textContent =
      `YOUR BONUS: ${format(bonus?.coins.reduce((sum, c) => sum + c.value, 0) ?? 0)}`
    spinButton.textContent = api.hasPending
      ? 'RETRY'
      : bonus
        ? response.state.phase === 'BOOST'
          ? 'BOOST'
          : `RESPIN · ${bonus.respins}`
        : 'SPIN'
    spinButton.disabled = busy
    buyButton.disabled = busy || !!bonus || api.hasPending
    buyButton.querySelector('span')!.textContent =
      `${format(config.buyCost * config.baseCost * multiplier)} CREDITS`
    for (const selector of ['.x7-plus', '.x7-minus'])
      element<HTMLButtonElement>(selector).disabled = busy || !!bonus || api.hasPending
    app.canvas.dataset.gameState = busy ? 'BUSY' : response.state.phase
    app.canvas.dataset.revision = String(response.revision)
    app.canvas.setAttribute('aria-busy', String(busy))
    if (response.state.phase === 'BOOST') {
      booster.position.x = 4 + response.state.bonus!.pendingColumns[0]! * 140
      status.textContent = 'FULL COLUMN! Spin the booster. A rare ×7 brings the big mood.'
    } else if (bonus) status.textContent = `${bonus.respins} respins left. New coins reset to 3.`
  }
  async function present(response: ClubResponse): Promise<void> {
    const result = response.result
    if (!result) {
      if (response.state.bonus) showBoard(response.state.bonus.coins)
      else {
        reels.visible = true
        board.container.visible = false
        drawLabels([])
        const idleGrid = response.revision === 0 ? ATTRACT_GRID : response.state.lastGrid
        idleGrid.forEach((column, reel) =>
          column.forEach((id, cell) => reels.setSymbolAt(reel, cell, SYMBOLS[id]!)),
        )
        drawLabels(
          idleGrid.flatMap((column, reel) =>
            column.flatMap((id, cell) =>
              id === 6 ? [{ position: reel * 3 + cell, value: 0, tier: 'CREDIT' } as const] : [],
            ),
          ),
        )
      }
      return
    }
    if (result.type === 'BASE' || result.type === 'BUY') {
      reels.spotlight.hide()
      board.container.visible = false
      reels.visible = true
      drawLabels([])
      const animation = reels.spin()
      reels.setResult(result.grid.map((column) => ({ visible: column.map((id) => SYMBOLS[id]!) })))
      await animation
      if (result.hits.length) {
        const positions = result.hits.flatMap(({ lineIndex, matchCount }) =>
          PAYLINES[lineIndex]!.slice(0, matchCount).map((cell, reel) => ({
            reelIndex: reel,
            cellIndex: cell,
          })),
        )
        await reels.spotlight.show(positions)
        tone(900, 0.2)
        await wait(turbo ? 80 : 400)
      }
      if (result.bonusTriggered) {
        showBoard(result.coins)
        tone(1200, 0.3)
        veil.alpha = 0.8
        await winOverlay.announce('HOLD & SPIN', turbo ? 450 : 1100)
        veil.alpha = 0
      } else {
        drawLabels(result.coins)
        status.textContent = result.win
          ? `${format(result.win)} CREDITS. That's a vibe.`
          : result.coins.length
            ? `${result.coins.length} / 6 coins. Six unlock Hold & Spin.`
            : 'Good vibes. Next spin?'
      }
    } else if (result.boost) {
      booster.visible = true

      booster.position.x = 4 + result.boost.column * 140
      const animation = booster.spin()
      booster.setResult([{ visible: [result.boost.kind] }])
      await animation
      for (const coin of result.coins) board.setSymbolAt(coordinate(coin), 'COIN', coin)
      previousCoins = structuredClone(result.coins)
      drawLabels(result.coins)
      tone(result.boost.kind === 'X7' ? 1600 : 640, 0.2)
      if (result.boost.kind !== 'STOP') {
        burst(70 + result.boost.column * 140, 280)
        if (result.boost.kind === 'X7') {
          veil.alpha = 0.65
          await winOverlay.announce('×7 BOOST!', turbo ? 550 : 1400)
          veil.alpha = 0
        }
      }
      status.textContent =
        result.boost.kind === 'X7'
          ? '×7. ABSOLUTE CINEMA.'
          : result.boost.kind === 'STOP'
            ? 'Column banked. Keep the good times rolling.'
            : `${result.boost.kind === 'PLUS1' ? '+1×' : '+2×'} BET TO EVERY COIN IN THE COLUMN`
    } else {
      // Re-seed the presentation ledger from the authoritative previous snapshot.
      showBoard(previousCoins)
      await board.respin(hwCoins(result.newCoins))
      previousCoins = structuredClone(result.coins)
      drawLabels(result.coins)
      for (const coin of result.newCoins) {
        const center = board.cellCenter(coordinate(coin))
        burst(center.x + 4, center.y + 100)
      }
    }
    if (result.type === 'RESPIN') {
      const prize = [...result.newCoins]
        .filter((coin) => coin.tier !== 'CREDIT')
        .sort((a, b) => b.value - a.value)[0]
      if (prize) {
        root.dataset.prize = prize.tier
        tone(1568, 0.22)
        await winOverlay.announce(`${prize.tier} PRIZE!`, turbo ? 350 : 850)
        delete root.dataset.prize
      }
    }
    if (result.win > 0) {
      const counter = { value: 0 }
      const winValue = element('.x7-win strong')
      gsap.to(counter, {
        value: result.win,
        duration: turbo || reducedMotion ? 0.2 : 0.7,
        ease: 'power2.out',
        onUpdate: () => {
          winValue.textContent = format(Math.round(counter.value))
        },
        onComplete: () => {
          winValue.textContent = format(result.win)
        },
      })
      await celebrate(result.win)
    }
    if (result.bonusEnded) {
      tone(1000, 0.3)
      status.textContent = `BONUS BANKED: ${format(result.win)} CREDITS${result.capped ? ' · MAX WIN' : ''}`
      element('.x7-bank').textContent = `LAST BONUS: ${format(result.win)}`
    }
  }
  function showError(error: Error): void {
    errorBox.hidden = false
    errorBox.textContent = error.message
    refresh.hidden = !(error instanceof ClubApiError && [404, 409].includes(error.status))
  }
  async function run(buy = false): Promise<void> {
    if (busy) return
    busy = true
    errorBox.hidden = true
    refresh.hidden = true
    if (lastResponse) sync(lastResponse)
    try {
      const response = api.hasPending
        ? await api.retry()
        : await api.action(
            buy ? 'buybonus' : lastResponse?.state.bonus ? 'next' : 'spin',
            multiplier,
          )
      await present(response)
      sync(response)
    } catch (error) {
      showError(error instanceof Error ? error : new Error('Unable to finish the action'))
    } finally {
      busy = false
      if (api.response) sync(api.response)
      else spinButton.textContent = 'RETRY'
      spinButton.disabled = false
    }
  }
  spinButton.addEventListener('click', () => {
    if (!api.response && !api.hasPending) void restore()
    else void run()
  })
  buyButton.addEventListener('click', () => {
    const dialog = document.createElement('dialog')
    dialog.className = 'x7-dialog'
    dialog.innerHTML = `<h2>MAKE AN ENTRANCE</h2><p>Start Hold & Spin with six locked coins for ${format(config.buyCost * config.baseCost * multiplier)} demo credits (${config.buyCost}× bet).</p><form method="dialog"><button value="cancel">NOT YET</button><button value="buy">LET'S GO</button></form>`
    root.append(dialog)
    dialog.showModal()
    dialog.addEventListener('close', () => {
      const buy = dialog.returnValue === 'buy'
      dialog.remove()
      if (buy) void run(true)
    })
  })
  for (const [selector, delta] of [
    ['.x7-minus', -1],
    ['.x7-plus', 1],
  ] as const)
    element<HTMLButtonElement>(selector).addEventListener('click', () => {
      multiplier = Math.min(10000, Math.max(1, multiplier + delta))
      if (lastResponse) sync(lastResponse)
    })
  element('.x7-sound').addEventListener('click', () => {
    muted = !muted
    element('.x7-sound').textContent = muted ? 'SOUND OFF' : 'SOUND ON'
    element('.x7-sound').setAttribute('aria-label', muted ? 'Enable sound' : 'Mute sound')
    tone(440)
  })
  element('.x7-turbo').addEventListener('click', () => {
    turbo = !turbo
    const speed = turbo ? 'turbo' : 'normal'
    reels.setSpeed(speed)
    booster.setSpeed(speed)
    board.setSpeed(speed)
    winOverlay.syncSpinSpeed(getSpinSpeedProfile(speed))
    element('.x7-turbo').textContent = turbo ? 'TURBO ON' : 'TURBO OFF'
    element('.x7-turbo').setAttribute('aria-pressed', String(turbo))
  })
  element('.x7-rules').addEventListener('click', () => {
    const dialog = document.createElement('dialog')
    dialog.className = 'x7-dialog'
    dialog.innerHTML = `<h2>THE CLUB RULES</h2><p>5 reels × 3 rows. All 20 lines are active. Match 3+ symbols from the left; WILD substitutes for paying symbols. The first non-wild symbol determines the combination; only its longest paying prefix pays. An all-wild line pays as SEVEN.</p><p>6+ COIN symbols trigger Hold & Spin. Coins lock; each new coin resets the counter to 3. Three misses end the feature. MINI / MAJOR / MEGA are fixed 10× / 50× / 250× stake prizes, not progressive pools.</p><p>Every filled column earns one booster. +1× and +2× add stake multiples to its three coins. ×7 multiplies their current values and banks the column. BANK ends the booster; a maximum of 7 pulls also banks it. Boosters don't consume respins. Complete pending boosters before collecting a full board.</p><p>The bonus pays once at the end. The total round cap is 7,777× stake. BUY BONUS costs ${config.buyCost}× stake. This is a demo with ephemeral sessions and virtual credits.</p><table><thead><tr><th>Symbol</th><th>3</th><th>4</th><th>5</th></tr></thead><tbody>${config.paytable.map((pays, id) => `<tr><th><img class="x7-rule-symbol" src="/assets/images/x7-club/${SYMBOLS[id]!.toLowerCase()}.webp" alt=""/>${symbolTitles[SYMBOLS[id]!]}</th>${pays.map((pay) => `<td>${pay}</td>`).join('')}</tr>`).join('')}</tbody></table><p>Paytable values × bet per line (total bet ÷ 20).</p><details><summary>All 20 paylines (rows 1–3, left to right)</summary><ol>${PAYLINES.map((line) => `<li>${line.map((row) => row + 1).join(' → ')}</li>`).join('')}</ol></details><form method="dialog"><button>GOT IT</button></form>`
    root.append(dialog)
    dialog.showModal()
    dialog.addEventListener('close', () => dialog.remove())
  })
  refresh.addEventListener('click', () => {
    api.resetExpiredSession()
    void restore()
  })
  async function restore(): Promise<void> {
    busy = true
    spinButton.disabled = true
    errorBox.hidden = true
    try {
      const response = await api.restore()
      multiplier = response.state.triggeringMultiplier
      await present(response)
      sync(response)
      if (!response.state.bonus && !response.result)
        status.textContent = 'The club is open. Bring your lucky energy.'
    } catch (error) {
      showError(error instanceof Error ? error : new Error('Unable to open the club'))
    } finally {
      busy = false
      if (api.response) sync(api.response)
      spinButton.disabled = false
      if (!api.response) spinButton.textContent = 'RETRY'
    }
  }
  document.addEventListener('keydown', (event) => {
    if (
      event.code === 'Space' &&
      !root.querySelector('dialog[open]') &&
      !(event.target instanceof HTMLButtonElement) &&
      !(event.target instanceof HTMLInputElement)
    ) {
      event.preventDefault()
      spinButton.click()
    }
  })
  window.addEventListener(
    'pagehide',
    () => {
      stopGsap()
      for (const particle of particles.children) gsap.killTweensOf(particle)
      winOverlay.destroy({ children: true })
      board.destroy()
      reels.destroy()
      booster.destroy()
      app.destroy(true, { children: true })
      for (const texture of Object.values(textures)) texture.destroy(true)
      void assetRegistry.unloadGame(manifest.gameId)
      void audio?.close()
    },
    { once: true },
  )
  await restore()
}
