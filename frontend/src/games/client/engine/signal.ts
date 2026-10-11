export type Unsubscribe = () => void

export interface ReadonlySignal<T> {
  readonly value: T
  subscribe(fn: (v: T) => void): Unsubscribe
}

export interface Signal<T> extends ReadonlySignal<T> {
  set(value: T): void
  update(fn: (current: T) => T): void
}

export function signal<T>(initial: T): Signal<T> {
  let current = initial
  const listeners = new Set<(v: T) => void>()

  return {
    get value() {
      return current
    },
    set(value: T) {
      if (value === current) return
      current = value
      for (const fn of listeners) fn(current)
    },
    update(fn: (current: T) => T) {
      this.set(fn(current))
    },
    subscribe(fn: (v: T) => void): Unsubscribe {
      listeners.add(fn)
      return () => listeners.delete(fn)
    },
  }
}
