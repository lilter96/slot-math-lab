import type { GameId, ActionType, ActionResponse } from '@tgslots/shared-contracts'
import type { GameRuntime } from './game-client.js'
import type { GameStateMachine } from './state-machine.js'
import type { SessionManager } from './session-manager.js'
import type { GameEventBus } from './event-bus.js'
import type { AutoSpinConfig } from '../types.js'
import { GameUIState } from '../types.js'
import { getSpinSpeedProfile } from './spin-speed.js'
import type { SpinSpeedProfile } from './spin-speed.js'

export interface OrchestratorActions<G extends GameId> {
  spinCost(betMultiplier: number): number
  doSpin(betMultiplier: number): Promise<ActionResponse<G>>
  buyBonusCost?(betMultiplier: number): number
  doBuyBonus?(betMultiplier: number): Promise<ActionResponse<G>>
  doFreeSpin?(): Promise<ActionResponse<G>>
  /** Cost (in credits) of a feature-menu option for the given bet. */
  featureCost?(optionId: string, betMultiplier: number): number
  /** Dispatch a feature-menu purchase; resolves like a spin (may enter free spins). */
  doFeatureBuy?(optionId: string, betMultiplier: number): Promise<ActionResponse<G>>
}

export class SpinOrchestrator<G extends GameId> {
  private _autoState: { config: AutoSpinConfig; remaining: number } | null = null
  private _freeSpinsRemaining = 0
  private _wonThisCycle = false
  private _bonusTriggeredThisCycle = false

  constructor(
    private readonly _fsm: GameStateMachine,
    private readonly _session: SessionManager,
    private readonly _runtime: GameRuntime<G>,
    private readonly _eventBus: GameEventBus,
    private readonly _actions: OrchestratorActions<G>,
    private readonly _getSpinSpeedProfile: () => SpinSpeedProfile = () =>
      getSpinSpeedProfile('normal'),
  ) {
    this._eventBus.on('win:awarded', ({ amount }) => {
      this._session.addWin(amount)
      this._wonThisCycle = true
    })
    this._eventBus.on('free-spins:updated', ({ remaining }) => {
      if (remaining > 0 && this._freeSpinsRemaining === 0) {
        this._bonusTriggeredThisCycle = true
      }
      this._freeSpinsRemaining = remaining
    })
  }

  get isAutoSpin(): boolean {
    return this._autoState !== null
  }

  get autoSpinRemaining(): number {
    return this._autoState?.remaining ?? 0
  }

  get freeSpinsRemaining(): number {
    return this._freeSpinsRemaining
  }

  startAutoSpin(config: AutoSpinConfig): void {
    this._autoState = { config, remaining: config.spins }
    if (this._fsm.state === GameUIState.IDLE) {
      this.spin(this._session.betMultiplier).catch(console.error)
    }
  }

  stopAutoSpin(): void {
    this._autoState = null
  }

  async spin(betMultiplier: number): Promise<void> {
    if (this._fsm.state !== GameUIState.IDLE) return
    if (this._autoState && !this._autoState.config.spins) return // manual spin during unlimited auto-spin

    const cost = this._actions.spinCost(betMultiplier)
    if (!this._session.deductWager(cost)) {
      this._autoState = null
      return
    }

    this._wonThisCycle = false
    this._bonusTriggeredThisCycle = false
    this._fsm.transitionTo(GameUIState.SPINNING)

    let receivedResponse = false
    try {
      const response = await this._actions.doSpin(betMultiplier)
      receivedResponse = true
      this._runtime.applyState(response.state)
      this._fsm.transitionTo(GameUIState.STOPPING)

      if (response.result !== undefined) {
        await this._runtime.presentResult('spin' as ActionType<G>, response.result)
      }

      await this._runFreeSpins()
    } catch (err) {
      if (!receivedResponse) this._session.refundPendingWager(cost)
      console.error('spin failed', err)
      this.stopAutoSpin()
      this._eventBus.emit('error:api', { message: 'Spin request failed' })
    } finally {
      this._finishCycle()
      this._scheduleAutoSpin()
    }
  }

  async buyBonus(betMultiplier: number): Promise<void> {
    if (this._fsm.state !== GameUIState.IDLE) return
    if (!this._actions.doBuyBonus || !this._actions.buyBonusCost) return

    const cost = this._actions.buyBonusCost(betMultiplier)
    if (!this._session.deductWager(cost)) return

    this._wonThisCycle = false
    this._bonusTriggeredThisCycle = false
    this._fsm.transitionTo(GameUIState.SPINNING)

    let receivedResponse = false
    try {
      const response = await this._actions.doBuyBonus(betMultiplier)
      receivedResponse = true
      this._runtime.applyState(response.state)
      this._fsm.transitionTo(GameUIState.STOPPING)

      if (response.result !== undefined) {
        await this._runtime.presentResult('buybonus' as ActionType<G>, response.result)
      }

      await this._runFreeSpins()
    } catch (err) {
      if (!receivedResponse) this._session.refundPendingWager(cost)
      console.error('buy bonus failed', err)
      this.stopAutoSpin()
      this._eventBus.emit('error:api', { message: 'Bonus request failed' })
    } finally {
      this._finishCycle()
    }
  }

  // Generic feature-menu purchase (bonus-buy tiers or enhanced single spins).
  // Resolves like a base spin: it may enter free spins, which then play out.
  async buyFeature(optionId: string, betMultiplier: number): Promise<void> {
    if (this._fsm.state !== GameUIState.IDLE) return
    if (!this._actions.doFeatureBuy || !this._actions.featureCost) return

    const cost = this._actions.featureCost(optionId, betMultiplier)
    if (!this._session.deductWager(cost)) return

    this._wonThisCycle = false
    this._bonusTriggeredThisCycle = false
    this._fsm.transitionTo(GameUIState.SPINNING)

    let receivedResponse = false
    try {
      const response = await this._actions.doFeatureBuy(optionId, betMultiplier)
      receivedResponse = true
      this._runtime.applyState(response.state)
      this._fsm.transitionTo(GameUIState.STOPPING)

      if (response.result !== undefined) {
        await this._runtime.presentResult('spin' as ActionType<G>, response.result)
      }

      await this._runFreeSpins()
    } catch (err) {
      if (!receivedResponse) this._session.refundPendingWager(cost)
      console.error('feature buy failed', err)
      this.stopAutoSpin()
      this._eventBus.emit('error:api', { message: 'Feature request failed' })
    } finally {
      this._finishCycle()
    }
  }

  async resumeFreeSpins(): Promise<void> {
    if (this._freeSpinsRemaining <= 0 || !this._actions.doFreeSpin) return
    this._fsm.transitionTo(GameUIState.FEATURE_TRANSITION)
    await this._runFreeSpins()
    this._fsm.transitionTo(GameUIState.IDLE)
  }

  private async _runFreeSpins(): Promise<void> {
    if (!this._actions.doFreeSpin) return
    while (this._freeSpinsRemaining > 0) {
      const response = await this._actions.doFreeSpin()
      this._runtime.applyState(response.state)
      if (response.result !== undefined) {
        await this._runtime.presentResult('freespin' as ActionType<G>, response.result)
      }
    }
  }

  // Drive the machine back to IDLE through valid transitions from wherever it
  // is — including SPINNING, when a spin/buy threw before reaching STOPPING.
  // Without this, an error mid-presentation would leave every button disabled.
  private _finishCycle(): void {
    if (this._fsm.state === GameUIState.SPINNING) {
      this._fsm.transitionTo(GameUIState.STOPPING)
    }
    if (this._fsm.state === GameUIState.STOPPING && this._wonThisCycle) {
      this._fsm.transitionTo(GameUIState.WIN_SHOW)
    }
    if (this._fsm.state !== GameUIState.IDLE) {
      this._fsm.transitionTo(GameUIState.IDLE)
    }
  }

  private _scheduleAutoSpin(): void {
    if (!this._autoState || this._fsm.state !== GameUIState.IDLE) return

    const { config } = this._autoState
    if (config.spins > 0) this._autoState.remaining--

    const shouldStop =
      (config.stopOnWin && this._wonThisCycle) ||
      (config.stopOnBonus && this._bonusTriggeredThisCycle) ||
      (config.spins > 0 && this._autoState.remaining <= 0)

    if (shouldStop) {
      this._autoState = null
      return
    }

    setTimeout(() => {
      this.spin(this._session.betMultiplier).catch(console.error)
    }, this._getSpinSpeedProfile().autoSpinDelayMs)
  }
}
