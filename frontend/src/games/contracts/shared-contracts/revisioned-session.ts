export type RevisionedAction = 'state' | 'spin' | 'buybonus' | 'next'
export interface RevisionedCommand {
  sessionId?: string
  requestId?: string
  expectedRevision?: number
  payload?: { multiplier?: number }
}
export interface RevisionedResponse<S, R> {
  sessionId: string
  balance: number
  revision: number
  state: S
  result?: R
}
