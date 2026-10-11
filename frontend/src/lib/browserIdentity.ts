import { retryAfterMilliseconds } from './realtime/httpFailure';
/** Set once from the validated server session before importing persisted stores. */
export interface BrowserIdentity { authenticated: boolean; required: boolean; userId?: string | null }
let identity: BrowserIdentity = { authenticated: false, required: false };
export function setBrowserIdentity(value: BrowserIdentity) { identity = value; }
export function getBrowserIdentity() { return identity; }
export function browserStorageKey(base: string) {
  return identity.authenticated && identity.userId ? `${base}:user:${encodeURIComponent(identity.userId)}` : base;
}

/** Only explicit quota rejections are retried. Stores stay unopened until verified. */
export async function readBrowserIdentity(url: string, waiting?: (milliseconds: number) => void): Promise<BrowserIdentity> {
  for (let attempt = 0; attempt < 3; attempt++) {
    const response = await fetch(url, { signal: AbortSignal.timeout(10000) });
    if (response.status === 429 && attempt < 2) {
      const pause = Math.max(1000, retryAfterMilliseconds(response.headers.get('Retry-After')));
      if (pause > 65000) throw new Error('Session retry requires a later connection');
      waiting?.(pause);
      await new Promise(resolve => setTimeout(resolve, pause));
      continue;
    }
    if (!response.ok) throw new Error('Session status unavailable');
    const identity: BrowserIdentity = await response.json();
    if (identity.authenticated && (typeof identity.userId !== 'string' || !identity.userId.trim())) throw new Error('Session identity unavailable');
    return identity;
  }
  throw new Error('Session status unavailable');
}
