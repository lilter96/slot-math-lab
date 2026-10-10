export class HttpFailure extends Error {
  readonly status: number;
  readonly retryAfterMs: number;
  readonly resource: 'run' | 'connection';
  constructor(message: string, status: number, retryAfterMs = 0, resource: 'run' | 'connection' = 'run') {
    super(message); this.status = status; this.retryAfterMs = retryAfterMs; this.resource = resource;
  }
}

/** Retry-After permits integer seconds or an HTTP date. Missing/malformed
 * values leave the minimum retry policy to the caller. */
export function retryAfterMilliseconds(header: string | null, now = Date.now()): number {
  if (!header) return 0;
  const value = header.trim();
  const delay = /^\d+$/.test(value) ? Number(value) * 1000 : Date.parse(value) - now;
  return Number.isFinite(delay) ? Math.max(0, delay) : 0;
}
