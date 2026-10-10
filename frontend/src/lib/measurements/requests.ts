import { HttpFailure } from '../realtime/RunConnection';
import { retryAfterMilliseconds } from '../realtime/httpFailure';

export function waitForRetry(delay: number, signal: AbortSignal): Promise<void> {
  signal.throwIfAborted();
  return new Promise((resolve, reject) => {
    const abort = () => { clearTimeout(timer); reject(signal.reason); };
    const timer = setTimeout(() => { signal.removeEventListener('abort', abort); resolve(); }, delay);
    signal.addEventListener('abort', abort, { once: true });
  });
}

/** A calculation may be repeated only after an explicit rejection before execution.
 * Each accepted attempt has its own timeout; waiting for quota never consumes that
 * execution budget. Network errors, timeouts and server errors remain ambiguous. */
export async function calculationRequest<T>(path: string, input: unknown, signal: AbortSignal,
  status: (message: string) => void, timeout = 25000): Promise<T> {
  const body = JSON.stringify(input);
  for (let attempt = 0; ; attempt++) {
    signal.throwIfAborted();
    status(attempt ? 'Retrying the rejected calculation…' : 'Calculating…');
    const response = await fetch(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body,
      signal: AbortSignal.any([signal, AbortSignal.timeout(timeout)]) });
    const data = await response.json().catch(() => ({}));
    if (response.ok) return data;
    if (response.status === 401) window.dispatchEvent(new Event('slotmath:auth-required'));
    const delay = Math.max(1000, retryAfterMilliseconds(response.headers.get('Retry-After')));
    if (response.status !== 429 || attempt >= 2 || delay > 60000) {
      const validation = Array.isArray(data.errors) ? data.errors.filter((e: unknown): e is { message: string } => !!e && typeof e === 'object' && 'message' in e && typeof e.message === 'string') : [];
      const detail = validation.slice(0, 8).map((e: { message: string }) => e.message.slice(0, 512)).join('; ');
      throw new HttpFailure(detail ? detail + (validation.length > 8 ? `; ${validation.length - 8} more validation errors.` : '') : data.error ?? data.title ?? (response.status === 429 ? 'Calculation quota is busy. Try again shortly.' : `HTTP ${response.status}`), response.status, delay);
    }
    status(`Server quota · retrying the rejected calculation in ${Math.ceil(delay / 1000)}s. Cancel to stop waiting.`);
    await waitForRetry(delay, signal);
  }
}
