import { abortable } from './abort';

// Shared between server instances in one realm. Persistent cross-tab sessions use Web Locks.
const pending = new Map<string, Promise<void>>();

export async function withSessionLock<T>(id: string, action: () => Promise<T>, signal?: AbortSignal): Promise<T> {
  signal?.throwIfAborted();
  if (typeof navigator !== 'undefined' && navigator.locks) {
    return navigator.locks.request(`slot-math-lab:session:${id}`, { signal }, () => {
      signal?.throwIfAborted();
      return action();
    });
  }
  const previous = pending.get(id) ?? Promise.resolve();
  let release!: () => void;
  const turn = new Promise<void>(resolve => { release = resolve; });
  const tail = previous.then(() => turn);
  pending.set(id, tail);
  try {
    await abortable(previous, signal);
    signal?.throwIfAborted();
    return await action();
  } finally {
    // A cancelled waiter must not let its successors overtake the active predecessor.
    void previous.then(() => {
      release();
      if (pending.get(id) === tail) pending.delete(id);
    });
  }
}
