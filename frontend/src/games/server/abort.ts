/** Reject promptly on cancellation even when an injected transport does not observe the signal. */
export function abortable<T>(work: Promise<T>, signal?: AbortSignal | null): Promise<T> {
  if (!signal) return work;
  return new Promise<T>((resolve, reject) => {
    const aborted = () => reject(signal.reason);
    signal.addEventListener('abort', aborted, { once: true });
    const cleanup = () => signal.removeEventListener('abort', aborted);
    work.then(value => { cleanup(); if (signal.aborted) reject(signal.reason); else resolve(value); }, error => { cleanup(); reject(error); });
    if (signal.aborted) { cleanup(); reject(signal.reason); }
  });
}
