import { progressDecision, snapshotDecision, type LiveProgress, type RunSnapshot } from '../src/lib/realtime/runProtocol';
import { RunConnection, type ConnectionEnvironment, type ConnectionHealth, type HubPort } from '../src/lib/realtime/RunConnection';
export const epoch = 'a'.repeat(32);
export function progress(sequence = 1, sampleCount = 10): LiveProgress {
  return { runId: 'run', streamEpoch: epoch, sequence, status: 'running', sampleCount, totalSamples: 1000,
    runningRtp: 0.98, stdErr: .1, elapsedMs: 100, hitFrequency: .2, nonZeroCount: Math.floor(sampleCount / 5),
    volatility: 1, maxWin: 2, capHits: 0, histogram: [{ lo: 0, hi: null, count: sampleCount }] };
}
export function snapshot(p = progress()): RunSnapshot {
  return { id: 'run', streamEpoch: p.streamEpoch, sequence: p.sequence, seed: 42, configId: 'graph', configHash: 'f'.repeat(64),
    configVersion: 1, degreeOfParallelism: 2, streamScheme: 'splitmix64-chunk-65536', createdAt: '2026-10-09T00:00:00Z',
    status: p.status, progress: p, resultJson: p.resultJson };
}
export function completed(sequence = 10, sampleCount = 1000) {
  return snapshot({ ...progress(sequence, sampleCount), status: 'completed', resultJson: JSON.stringify({ status: 'completed', sampleCount }) });
}
export const flush = async () => { for (let i = 0; i < 30; i++) await Promise.resolve(); };
export class Clock implements ConnectionEnvironment {
  time = 100000; available = true; serial = 0;
  timers = new Map<number, { callback: () => void; due: number }>();
  listener: ((reason: 'network' | 'visible' | 'auth') => void) | null = null;
  now = () => this.time; random = () => 1; online = () => this.available;
  timer = (callback: () => void, delay: number) => { const id = ++this.serial; this.timers.set(id, { callback, due: this.time + delay }); return id; };
  clear = (id: number) => { this.timers.delete(id); };
  wake = (listener: (reason: 'network' | 'visible' | 'auth') => void) => { this.listener = listener; return () => { this.listener = null; }; };
  async advance(ms: number) {
    const target = this.time + ms;
    for (;;) {
      const first = [...this.timers].filter(([, t]) => t.due <= target).sort((a, b) => a[1].due - b[1].due)[0];
      if (!first) break;
      this.time = first[1].due; this.timers.delete(first[0]); first[1].callback(); await flush();
    }
    this.time = target; await flush();
  }
}
export class Hub implements HubPort {
  starts = 0; stops = 0; calls: string[] = [];
  listener: (value: unknown) => void = () => {};
  close: () => void = () => {};
  startError: unknown = null;
  read: (method: string) => Promise<unknown> = async () => snapshot();
  async start() { this.starts++; if (this.startError) throw this.startError; }
  async stop() { this.stops++; this.close(); }
  async invoke<T>(method: string): Promise<T> { this.calls.push(method); return await this.read(method) as T; }
  on(_method: string, listener: (value: unknown) => void) { this.listener = listener; }
  onclose(listener: () => void) { this.close = listener; }
}
export function harness() {
  const clock = new Clock(), hubs: Hub[] = [], health: ConnectionHealth[] = [], events: string[] = [], warnings: string[] = [];
  let state = snapshot({ ...progress(0, 0), status: 'pending' });
  let server: unknown = snapshot();
  let http: (signal: AbortSignal) => Promise<unknown> = async () => server;
  let configure: (hub: Hub) => void = () => {};
  let reads = 0;
  const owner = new RunConnection({ runId: 'run', environment: clock,
    hub: () => { const hub = new Hub(); hub.read = async () => server; configure(hub); hubs.push(hub); return hub; },
    readSnapshot: signal => { reads++; return http(signal); },
    progress: value => { const d = progressDecision(state, state.progress ?? null, value); if (d === 'accept') state = snapshot(value as LiveProgress); return d; },
    snapshot: value => { const d = snapshotDecision(state, state.progress ?? null, value); if (['accept', 'duplicate', 'reset'].includes(d)) state = value as RunSnapshot; return d; },
    health: h => health.push(h), event: e => events.push(e), warning: w => warnings.push(w),
  });
  return { owner, clock, hubs, health, events, warnings, get state() { return state; }, get reads() { return reads; },
    set server(value: unknown) { server = value; }, set http(value: typeof http) { http = value; }, set configure(value: typeof configure) { configure = value; } };
}
