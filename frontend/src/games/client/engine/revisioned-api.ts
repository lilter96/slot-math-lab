import type {
  RevisionedAction,
  RevisionedResponse,
} from '@tgslots/shared-contracts/revisioned-session'
import { serveGame } from '../../server'

interface PendingRequest {
  action: Exclude<RevisionedAction, 'state'>
  body: {
    sessionId: string
    requestId: string
    expectedRevision: number
    payload: { multiplier?: number }
  }
}
export class RevisionedApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message)
  }
}
/** A failed action retains its request identity, including across reloads. */
export class RevisionedGameApi<S, R> {
  response: RevisionedResponse<S, R> | null = null
  private sessionId = ''
  private pending: PendingRequest | null = null
  private readonly sessionKey: string
  private readonly pendingKey: string
  constructor(
    private readonly gameId: string,
    private readonly baseUrl = '',
  ) {
    this.sessionKey = `tgslots:${gameId}:session`
    this.pendingKey = `tgslots:${gameId}:pending`
    try {
      this.sessionId = localStorage.getItem(this.sessionKey) ?? ''
      const saved = localStorage.getItem(this.pendingKey)
      if (saved) this.pending = JSON.parse(saved) as PendingRequest
    } catch {
      /* In-memory sessions still work when storage is unavailable. */
    }
  }
  get hasPending(): boolean {
    return this.pending !== null
  }
  async restore(): Promise<RevisionedResponse<S, R>> {
    if (this.pending) return this.retry()
    try {
      return await this.fetch('state', { sessionId: this.sessionId, payload: {} })
    } catch (error) {
      if (!(error instanceof RevisionedApiError) || error.status !== 404) throw error
      this.sessionId = ''
      return this.fetch('state', { payload: {} })
    }
  }
  async action(
    action: Exclude<RevisionedAction, 'state'>,
    multiplier: number,
  ): Promise<RevisionedResponse<S, R>> {
    if (this.pending) throw new Error('Retry the pending action first')
    if (!this.response) throw new Error('Session is not ready')
    this.pending = {
      action,
      body: {
        sessionId: this.sessionId,
        requestId: crypto.randomUUID(),
        expectedRevision: this.response.revision,
        payload: action === 'next' ? {} : { multiplier },
      },
    }
    this.savePending()
    return this.retry()
  }
  async retry(): Promise<RevisionedResponse<S, R>> {
    if (!this.pending) return this.restore()
    try {
      const response = await this.fetch(this.pending.action, this.pending.body)
      this.pending = null
      this.savePending()
      return response
    } catch (error) {
      // 400 is a validated, unexecuted command. Transport/502/503 errors remain ambiguous.
      if (error instanceof RevisionedApiError && error.status === 400) {
        this.pending = null
        this.savePending()
      }
      throw error
    }
  }
  resetExpiredSession(): void {
    this.pending = null
    this.sessionId = ''
    this.response = null
    try {
      localStorage.removeItem(this.sessionKey)
      localStorage.removeItem(this.pendingKey)
    } catch {
      /* optional storage */
    }
  }
  private savePending(): void {
    try {
      if (this.pending) localStorage.setItem(this.pendingKey, JSON.stringify(this.pending))
      else localStorage.removeItem(this.pendingKey)
    } catch {
      /* In-memory pending identity remains available. */
    }
  }
  private async fetch(action: RevisionedAction, body: object): Promise<RevisionedResponse<S, R>> {
    const response = await serveGame(`${this.baseUrl}/game/${this.gameId}/${action}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(12_000),
    })
    if (!response.ok) {
      const error = (await response.json()) as { error?: string }
      throw new RevisionedApiError(response.status, error.error ?? `HTTP ${response.status}`)
    }
    const data = (await response.json()) as RevisionedResponse<S, R>
    this.sessionId = data.sessionId
    this.response = data
    try {
      localStorage.setItem(this.sessionKey, data.sessionId)
    } catch {
      /* optional storage */
    }
    return data
  }
}
