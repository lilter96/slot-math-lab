import type { GameId, ActionType, ActionPayload, ActionResponse } from '@tgslots/shared-contracts'
import { serveGame } from '../../server'

const SESSION_KEY_PREFIX = 'tgslots:session:'

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

export class GameDispatcher<G extends GameId> {
  private readonly _sessionKey: string
  private _sessionId: string | null = null
  private _sessionLoaded = false

  constructor(
    private readonly _gameId: G,
    private readonly _baseUrl: string,
    private readonly _serve = serveGame,
  ) {
    this._sessionKey = `${SESSION_KEY_PREFIX}${_gameId}`
  }

  get sessionId(): string | null {
    if (this._sessionLoaded) return this._sessionId
    this._sessionLoaded = true
    try {
      const stored = localStorage.getItem(this._sessionKey)
      if (stored !== null) this._sessionId = stored
      return this._sessionId
    } catch {
      return this._sessionId
    }
  }

  async dispatch<A extends ActionType<G>>(
    action: A,
    payload: ActionPayload<G, A>,
  ): Promise<ActionResponse<G>> {
    const sessionId = this.sessionId
    const body: { payload: ActionPayload<G, A>; sessionId?: string } = { payload }
    if (sessionId) body.sessionId = sessionId

    const controller = new AbortController()
    const timeoutId = setTimeout(() => controller.abort(), 30_000)

    let res: Response
    try {
      res = await this._serve(`${this._baseUrl}/game/${this._gameId}/${String(action)}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
        signal: controller.signal,
      })
    } finally {
      clearTimeout(timeoutId)
    }

    // Session expired or server restarted — clear the stale id and retry to create a new session
    if (res.status === 404 && sessionId) {
      this.clearSession()
      return this.dispatch(action, payload)
    }

    if (!res.ok) {
      const errBody = await res.json().catch(() => ({ error: res.statusText }))
      throw new ApiError(
        res.status,
        typeof errBody === 'object' && errBody !== null && 'error' in errBody
          ? String((errBody as { error: string }).error)
          : `HTTP ${res.status}`,
      )
    }

    const data = (await res.json()) as ActionResponse<G>
    this._sessionId = data.sessionId
    this._sessionLoaded = true
    try {
      localStorage.setItem(this._sessionKey, data.sessionId)
    } catch {
      // localStorage unavailable (private browsing, etc.) — session is still valid in memory
    }
    return data
  }

  clearSession(): void {
    this._sessionId = null
    this._sessionLoaded = true
    try {
      localStorage.removeItem(this._sessionKey)
    } catch {
      // localStorage unavailable
    }
  }
}
