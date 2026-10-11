import { Application } from 'pixi.js'
import { BET_CONFIG as WW_BET_CONFIG, BUY_BONUS_COST_MULTIPLIER } from '@tgslots/woodland-whisper'
import { BET_CONFIG as AD_BET_CONFIG } from '@tgslots/ancient-dragon'
import {
  BET_CONFIG as LM_BET_CONFIG,
  getFeatureBuyCost as getLMFeatureBuyCost,
  DEFAULT_MODE as LM_DEFAULT_MODE,
} from '@tgslots/le-militare'
import type { ModeId as LMModeId } from '@tgslots/le-militare'
import { Wager } from '@tgslots/slots-core'
import type { GameId } from '@tgslots/shared-contracts'
import type { WoodlandWhisperSerializedState } from '@tgslots/shared-contracts/states'
import type { AncientDragonSerializedState } from '@tgslots/shared-contracts/states'
import type { LeMilitareSerializedState } from '@tgslots/shared-contracts/states'
import { GameStateMachine } from './engine/state-machine.js'
import { SessionManager } from './engine/session-manager.js'
import { HUD } from './engine/hud.js'
import { AutoSpinPanel } from './engine/auto-spin-panel.js'
import { AssetRegistry } from './engine/asset-registry.js'
import { GameDispatcher } from './engine/dispatcher.js'
import { GameEventBus } from './engine/event-bus.js'
import { PixiScene } from './engine/scene.js'
import { SoundManager } from './engine/sound-manager.js'
import { SpinOrchestrator } from './engine/spin-orchestrator.js'
import type { OrchestratorActions } from './engine/spin-orchestrator.js'
import type { GameRuntime } from './engine/game-client.js'
import { getResponsiveLayout } from './engine/layout.js'
import { SpinSpeedController } from './engine/spin-speed.js'
import { gameRegistry } from './games/registry.js'
import { GamePicker } from './app/game-picker.js'
import { GameShell } from './app/game-shell.js'
import { GameLoader } from './app/game-loader.js'
import type { AssetManifest } from '@tgslots/shared-contracts'
import type { AutoSpinConfig } from './types.js'
import { GameUIState } from './types.js'

// Import game registrations so declaration-merging activates before any dispatch call
import './games/woodland-whisper/index.js'
import './games/ancient-dragon/index.js'
import './games/le-militare/index.js'

// Empty string → relative URL, forwarded by the Vite dev proxy to :3001.
// Set VITE_API_URL to an absolute URL in production.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const BASE_URL: string = (import.meta as any).env?.VITE_API_URL ?? ''

function resolveLoaderBackground(assets: AssetManifest): string | undefined {
  return assets.images?.BACKGROUND_16_9 ?? assets.images?.BACKGROUND ?? assets.images?.HERO
}

async function mountGame(gameId: string): Promise<void> {
  const gameClient = gameRegistry[gameId]
  if (gameClient === undefined) return
  if ('launch' in gameClient) {
    await gameClient.launch()
    return
  }
  const client = gameClient

  document.title = client.manifest.displayName

  const loader = new GameLoader()
  loader.theme({
    theme: client.manifest.theme,
    displayName: client.manifest.displayName,
    backgroundUrl: resolveLoaderBackground(client.assets),
  })

  const app = new Application()
  await app.init({
    background: '#060e04',
    resizeTo: window,
    antialias: true,
    preference: 'webgl',
    resolution: window.devicePixelRatio || 1,
    autoDensity: true,
    hello: true,
    roundPixels: true,
  })
  document.getElementById('game-container')?.appendChild(app.canvas)

  const scene = new PixiScene(app.stage)
  const fsm = new GameStateMachine()
  const session = new SessionManager(1_000_000)
  const eventBus = new GameEventBus()
  const assetRegistry = new AssetRegistry()
  const spinSpeed = new SpinSpeedController()
  const soundManager = new SoundManager(eventBus)

  const assets = await assetRegistry.loadGame(client.manifest, client.assets, (loaded, total) =>
    loader.setProgress(loaded, total),
  )

  const betConfig =
    gameId === 'ancient-dragon'
      ? AD_BET_CONFIG
      : gameId === 'le-militare'
        ? LM_BET_CONFIG
        : WW_BET_CONFIG
  const hud = new HUD(
    session,
    fsm,
    eventBus,
    (multiplier) => new Wager(multiplier, betConfig).totalWager,
  )
  scene.hud.addChild(hud)

  const autoSpinPanel = new AutoSpinPanel()
  scene.overlays.addChild(autoSpinPanel)

  const dispatcher = new GameDispatcher(gameId as GameId, BASE_URL)
  const ctx = {
    scene,
    eventBus,
    dispatcher,
    assets,
    fsm,
    session,
    hud,
    sound: soundManager,
    // eslint-disable-next-line @typescript-eslint/no-restricted-types
  } as unknown as Parameters<typeof client.mount>[0]
  const runtime = await client.mount(ctx)

  // Configure sound mapping if the game provides one
  if (client.soundMapping) {
    soundManager.setMapping(client.soundMapping)
  }

  let orchestrator:
    | SpinOrchestrator<'woodland-whisper'>
    | SpinOrchestrator<'ancient-dragon'>
    | SpinOrchestrator<'le-militare'>
  let lmMode: LMModeId = LM_DEFAULT_MODE
  if (gameId === 'ancient-dragon') {
    const d = dispatcher as GameDispatcher<'ancient-dragon'>
    const adActions: OrchestratorActions<'ancient-dragon'> = {
      spinCost: (m) => new Wager(m, AD_BET_CONFIG).totalWager,
      doSpin: (m) => d.dispatch('spin', { multiplier: m }),
      doFreeSpin: () => d.dispatch('freespin', {}),
    }
    orchestrator = new SpinOrchestrator(
      fsm,
      session,
      runtime as GameRuntime<'ancient-dragon'>,
      eventBus,
      adActions,
      () => spinSpeed.profile,
    )
  } else if (gameId === 'le-militare') {
    const d = dispatcher as GameDispatcher<'le-militare'>
    lmMode = LM_DEFAULT_MODE
    const lmFeatureCost = (optionId: string, m: number): number => {
      const bet = new Wager(m, LM_BET_CONFIG).totalWager
      const cost = getLMFeatureBuyCost(lmMode, optionId)
      return bet * cost
    }
    const lmActions: OrchestratorActions<'le-militare'> = {
      spinCost: (m) => new Wager(m, LM_BET_CONFIG).totalWager,
      doSpin: (m) => d.dispatch('spin', { multiplier: m, mode: lmMode }),
      buyBonusCost: (m) =>
        new Wager(m, LM_BET_CONFIG).totalWager * getLMFeatureBuyCost(lmMode, 'standard'),
      doBuyBonus: (m) => d.dispatch('buybonus', { multiplier: m, mode: lmMode }),
      doFreeSpin: () => d.dispatch('freespin', {}),
      featureCost: lmFeatureCost,
      doFeatureBuy: (optionId, m) => {
        if (optionId === 'chance') return d.dispatch('chancespin', { multiplier: m, mode: lmMode })
        if (optionId === 'airraid')
          return d.dispatch('airraidspin', { multiplier: m, mode: lmMode })
        return d.dispatch('buybonus', {
          multiplier: m,
          option: optionId as 'standard' | 'elite' | 'super',
          mode: lmMode,
        })
      },
    }
    eventBus.on('volatility:selected', ({ mode }) => {
      lmMode = mode as LMModeId
    })
    orchestrator = new SpinOrchestrator(
      fsm,
      session,
      runtime as GameRuntime<'le-militare'>,
      eventBus,
      lmActions,
      () => spinSpeed.profile,
    )
  } else {
    const d = dispatcher as GameDispatcher<'woodland-whisper'>
    const wwActions: OrchestratorActions<'woodland-whisper'> = {
      spinCost: (m) => new Wager(m, WW_BET_CONFIG).totalWager,
      doSpin: (m) => d.dispatch('spin', { multiplier: m }),
      buyBonusCost: (m) => new Wager(m, WW_BET_CONFIG).totalWager * BUY_BONUS_COST_MULTIPLIER,
      doBuyBonus: (m) => d.dispatch('buybonus', { multiplier: m }),
      doFreeSpin: () => d.dispatch('freespin', {}),
    }
    orchestrator = new SpinOrchestrator(
      fsm,
      session,
      runtime as GameRuntime<'woodland-whisper'>,
      eventBus,
      wwActions,
      () => spinSpeed.profile,
    )
  }

  const toggleSound = () => {
    soundManager.setMuted(!soundManager.isMuted)
    shell.syncSound(soundManager.isMuted)
  }
  const shell = new GameShell(
    client.manifest,
    {
      spin: () => {
        if (
          fsm.state === GameUIState.IDLE &&
          !scene.overlays.children.some((layer) => layer.visible && layer.alpha > 0)
        )
          void orchestrator.spin(session.betMultiplier)
      },
      toggleSound,
      stopAuto: () => {
        orchestrator.stopAutoSpin()
        syncAutoSpinState()
      },
      stepBet: (delta) => {
        if (fsm.state !== GameUIState.IDLE || orchestrator.isAutoSpin) return
        eventBus.emit('bet:changed', { multiplier: Math.max(1, session.betMultiplier + delta) })
      },
    },
    client.assets,
    soundManager,
  )
  eventBus.on('error:api', () =>
    shell.showError('The spin could not be completed. Please try again.'),
  )

  const syncSpinSpeed = () => {
    const { mode, profile } = spinSpeed.state
    hud.syncSpinSpeed(mode)
    runtime.syncSpinSpeed?.(profile)
  }

  // Sync free-spins badge in HUD whenever remaining count changes
  eventBus.on('free-spins:updated', ({ remaining, awarded }) => {
    hud.syncFreeSpinsStatus({ active: remaining > 0, remaining, awarded: awarded ?? null })
  })

  // Restore session state
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const initResponse = await (dispatcher as any).dispatch('state', {})
  if (gameId === 'ancient-dragon') {
    const initState = initResponse.state as AncientDragonSerializedState
    runtime.applyState(initState as Parameters<typeof runtime.applyState>[0])
    orchestrator.resumeFreeSpins().catch(console.error)
  } else if (gameId === 'le-militare') {
    const initState = initResponse.state as LeMilitareSerializedState
    if (initState.mode) lmMode = initState.mode as LMModeId
    runtime.applyState(initState as Parameters<typeof runtime.applyState>[0])
    if (initState.lastGrid && runtime.restoreGrid) {
      runtime.restoreGrid(initState.lastGrid)
    }
    orchestrator.resumeFreeSpins().catch(console.error)
  } else {
    const initState = initResponse.state as WoodlandWhisperSerializedState
    runtime.applyState(initState as Parameters<typeof runtime.applyState>[0])

    if (initState.pickBonus && runtime.resumeFeatures) {
      fsm.transitionTo(GameUIState.FEATURE_TRANSITION)
      runtime
        .resumeFeatures()
        .then(() => {
          if (fsm.state === GameUIState.FEATURE_TRANSITION) {
            fsm.transitionTo(GameUIState.IDLE)
          }
          return orchestrator.resumeFreeSpins()
        })
        .catch(console.error)
    } else {
      orchestrator.resumeFreeSpins().catch(console.error)
    }
  }

  // Expose whether the canvas is busy to assistive tools and browser verification.
  app.canvas.dataset.gameState = fsm.state
  app.canvas.setAttribute('aria-busy', String(fsm.state !== GameUIState.IDLE))
  fsm.addListener((state) => {
    app.canvas.dataset.gameState = state
    app.canvas.setAttribute('aria-busy', String(state !== GameUIState.IDLE))
  })

  // Layout — driven by per-game manifest dimensions
  function doLayout(W: number, H: number) {
    const layout = getResponsiveLayout(
      W,
      H,
      client.manifest.reelNaturalWidth,
      client.manifest.reelNaturalHeight,
      gameId === 'le-militare' ? 92 : 52,
    )
    runtime.resize(layout)
    hud.resize(layout)
    autoSpinPanel.resize(layout)
  }

  doLayout(app.screen.width, app.screen.height)
  app.renderer.on('resize', (W: number, H: number) => doLayout(W, H))

  const syncAutoSpinState = () => {
    hud.syncAutoSpin(orchestrator.isAutoSpin, orchestrator.autoSpinRemaining)
    eventBus.emit('auto-spin:updated', {
      active: orchestrator.isAutoSpin,
      remaining: orchestrator.autoSpinRemaining,
    })
  }

  // HUD event wiring
  hud.on('spin', () => orchestrator.spin(session.betMultiplier).catch(console.error))
  hud.on('autoSpin', () => autoSpinPanel.show())
  hud.on('toggleFastSpin', () => {
    spinSpeed.toggleFast()
    syncSpinSpeed()
  })
  hud.on('toggleTurboSpin', () => {
    spinSpeed.toggleTurbo()
    syncSpinSpeed()
  })
  hud.on('stopAutoSpin', () => {
    orchestrator.stopAutoSpin()
    syncAutoSpinState()
  })
  eventBus.on('buy-bonus:requested', () => {
    orchestrator.buyBonus(session.betMultiplier).catch(console.error)
  })
  eventBus.on('feature-buy:requested', ({ optionId }) => {
    orchestrator.buyFeature(optionId, session.betMultiplier).catch(console.error)
  })
  eventBus.on('bet:changed', ({ multiplier }) => {
    session.setBetMultiplier(multiplier)
    hud.updateTexts()
  })

  autoSpinPanel.on('start', (config: AutoSpinConfig) => {
    orchestrator.startAutoSpin(config)
    syncAutoSpinState()
  })

  fsm.addListener((state) => {
    if (state === GameUIState.IDLE) {
      syncAutoSpinState()
    }
  })

  syncAutoSpinState()
  syncSpinSpeed()
  shell.syncSound(soundManager.isMuted)

  console.log('Game initialized.')

  await loader.awaitTap()
  soundManager.unlock()
  await loader.dismiss()
}

function showPicker(): void {
  const games = Object.entries(gameRegistry).map(([id, client]) => ({
    id,
    displayName: client.manifest.displayName,
  }))
  new GamePicker(games, (gameId) => {
    mountGame(gameId).catch(showStartupError)
  })
}

async function init() {
  const params = new URLSearchParams(location.search)
  const gameId = params.get('game')

  if (!gameId || !gameRegistry[gameId]) {
    showPicker()
    return
  }

  await mountGame(gameId)
}

function showStartupError(error: Error) {
  console.error('Game startup failed', error)
  const loader = document.getElementById('game-loader')
  const status = loader?.querySelector('.loader-status')
  if (status) status.textContent = 'Unable to load the game. Please retry.'
  if (loader) {
    loader.style.visibility = 'visible'
    const retry = document.createElement('button')
    retry.textContent = 'Retry loading'
    retry.className = 'loader-retry'
    retry.addEventListener('click', () => location.reload())
    loader.querySelector('.loader-content')?.append(retry)
  }
}

init().catch(showStartupError)
