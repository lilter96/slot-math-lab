import { createRunHub } from './signalRHub';
import { terminal, validProgress, validSnapshot, type Decision, type RunSnapshot } from './runProtocol';
export { HttpFailure } from './httpFailure';

export type ConnectionPhase = 'idle' | 'connecting' | 'live' | 'recovering' | 'offline' | 'auth-required' | 'unavailable';
export interface ConnectionHealth {
  phase: ConnectionPhase; reconnects: number; attempts: number; lastConfirmedAt: number;
  lastSocketAt: number; roundTripMs: number | null; nextRetryAt: number | null;
}
export interface HubPort {
  start(): Promise<void>; stop(): Promise<void>;
  invoke<T>(method: string, id: string): Promise<T>;
  on(method: string, listener: (value: unknown) => void): void;
  onclose(listener: (error?: Error) => void): void;
}
export interface ConnectionEnvironment {
  now(): number; random(): number; online(): boolean;
  timer(callback: () => void, delay: number): number; clear(timer: number): void;
  wake(listener: (reason: 'network' | 'visible' | 'auth') => void): () => void;
}
interface ConnectionOptions {
  runId: string;
  readSnapshot(signal: AbortSignal): Promise<unknown>;
  progress(value: unknown): Decision; snapshot(value: unknown): Decision;
  health(value: ConnectionHealth): void; event(message: string): void; warning(message: string): void;
  hub?: () => HubPort; environment?: ConnectionEnvironment;
}
const maximumTimerDelay = 2147483647;
const browserEnvironment: ConnectionEnvironment = {
  now: () => Date.now(), random: () => Math.random(), online: () => navigator.onLine,
  timer: (callback, delay) => window.setTimeout(callback, delay), clear: timer => window.clearTimeout(timer),
  wake: listener => {
    const network = () => listener('network');
    const visible = () => { if (!document.hidden) listener('visible'); };
    const auth = () => listener('auth');
    window.addEventListener('online', network); window.addEventListener('offline', network);
    window.addEventListener('pageshow', visible); document.addEventListener('visibilitychange', visible);
    window.addEventListener('slotmath:authenticated', auth);
    return () => {
      window.removeEventListener('online', network); window.removeEventListener('offline', network);
      window.removeEventListener('pageshow', visible); document.removeEventListener('visibilitychange', visible);
      window.removeEventListener('slotmath:authenticated', auth);
    };
  },
};

/** One retry supervisor owns the socket, HTTP fallback, heartbeat, and all timers.
 * Connected means subscription acknowledged. Live means the server still answers.
 * Messages are cumulative snapshots, so recovery needs no unbounded replay buffer. */
export class RunConnection {
  private readonly options: ConnectionOptions;
  private readonly env: ConnectionEnvironment;
  private readonly factory: () => HubPort;
  private state: ConnectionHealth = { phase: 'connecting', reconnects: 0, attempts: 0,
    lastConfirmedAt: 0, lastSocketAt: 0, roundTripMs: null, nextRetryAt: null };
  private hub: HubPort | null = null;
  private closed = false;
  private started = false;
  private subscribed = false;
  private hadSubscription = false;
  private probing = false;
  private request: AbortController | null = null;
  private retryTimer: number | null = null;
  private auditTimer: number | null = null;
  private heartbeatTimer: number | null = null;
  private readonly deadlines = new Map<number, () => void>();
  private stopListening: (() => void) | null = null;
  private throttledUntil = 0;
  private httpFailures = 0;

  constructor(options: ConnectionOptions) {
    this.options = options;
    this.env = options.environment ?? browserEnvironment;
    this.factory = options.hub ?? createRunHub;
  }

  start(): void {
    if (this.started || this.closed) return;
    this.started = true;
    this.stopListening = this.env.wake(reason => this.wake(reason));
    if (!this.env.online()) { this.phase('offline'); return; }
    void this.sync(); void this.connect();
  }

  stop(): void {
    if (this.closed) return;
    this.closed = true; this.cancelTimers(); this.request?.abort();
    this.stopListening?.(); this.stopListening = null;
    const hub = this.hub; this.hub = null;
    if (hub) void hub.stop().catch(() => {});
  }

  retry(): void { this.wake('visible'); }

  private emit(): void { if (!this.closed) this.options.health({ ...this.state }); }
  private phase(phase: ConnectionPhase): void { this.state.phase = phase; this.emit(); }
  private suspended(): boolean { return this.closed || !this.env.online() || this.state.phase === 'auth-required' || this.state.phase === 'unavailable'; }
  private clear(kind: 'retryTimer' | 'auditTimer' | 'heartbeatTimer'): void {
    if (this[kind] !== null) this.env.clear(this[kind]!);
    this[kind] = null;
  }
  private cancelTimers(): void {
    this.clear('retryTimer'); this.clear('auditTimer'); this.clear('heartbeatTimer');
    for (const cancel of [...this.deadlines.values()]) cancel();
  }
  private deadline<T>(promise: Promise<T>): Promise<T> {
    return new Promise((resolve, reject) => {
      const cleanup = () => { this.env.clear(timer); this.deadlines.delete(timer); };
      const timer = this.env.timer(() => { cleanup(); reject(new Error('Server acknowledgement timed out')); }, 8000);
      this.deadlines.set(timer, () => { cleanup(); reject(new Error('Connection stopped')); });
      promise.then(value => { cleanup(); resolve(value); }, error => { cleanup(); reject(error); });
    });
  }

  private finish(value: unknown): boolean {
    if (validProgress(value) && terminal(value.status) && value.resultJson
      || validSnapshot(value) && terminal(value.status) && value.resultJson) {
      this.phase('idle'); this.stop(); return true;
    }
    return false;
  }

  private acknowledge(socket: boolean, rtt?: number): void {
    this.state.lastConfirmedAt = this.env.now();
    if (socket) this.state.lastSocketAt = this.env.now();
    if (rtt !== undefined) this.state.roundTripMs = Math.max(0, rtt);
    this.emit();
  }

  private async connect(): Promise<void> {
    if (this.suspended() || this.hub) return;
    if (this.env.now() < this.throttledUntil) { this.scheduleRetry(); return; }
    this.clear('retryTimer'); this.state.nextRetryAt = null;
    this.phase(this.hadSubscription ? 'recovering' : 'connecting');
    const hub = this.factory(); this.hub = hub;
    hub.on('ProgressUpdate', value => {
      if (this.closed || this.hub !== hub || !this.env.online()) return;
      const decision = this.options.progress(value);
      if (decision === 'epoch-change' || decision === 'invalid') { void this.sync(); return; }
      if (decision === 'wrong-run' || decision === 'stale') return;
      this.acknowledge(true);
      this.finish(value);
    });
    hub.onclose(() => {
      if (this.closed || this.hub !== hub) return;
      this.hub = null; this.subscribed = false; this.probing = false;
      this.phase(this.env.online() ? 'recovering' : 'offline');
      void this.sync(); this.scheduleRetry();
    });
    const started = this.env.now();
    try {
      await this.deadline(hub.start());
      if (this.closed || this.hub !== hub) return;
      const snapshot = await this.deadline(hub.invoke<RunSnapshot>('SubscribeToRun', this.options.runId));
      if (this.closed || this.hub !== hub) return;
      const decision = this.options.snapshot(snapshot);
      if (decision === 'invalid' || decision === 'wrong-run' || decision === 'epoch-change') throw new Error('Subscription snapshot could not be reconciled');
      // A newer broadcast can overtake the invocation response. Keep its data
      // while accepting the valid response as acknowledgement of subscription.
      if (decision !== 'stale' && this.finish(snapshot)) return;
      if (this.hadSubscription) this.state.reconnects++;
      this.hadSubscription = true; this.subscribed = true; this.state.attempts = 0;
      this.acknowledge(true, this.env.now() - started); this.phase('live');
      this.options.event('WebSocket subscription acknowledged · snapshot synchronized');
      this.scheduleHeartbeat(); this.scheduleAudit();
    } catch (error) {
      if (this.closed || this.hub !== hub) return;
      this.detach();
      this.failure(error); this.scheduleRetry(); this.scheduleAudit();
    }
  }

  private detach(): void {
    const hub = this.hub; this.hub = null; this.subscribed = false;
    this.clear('heartbeatTimer'); this.probing = false;
    if (hub) void hub.stop().catch(() => {});
  }

  private failure(error: unknown): void {
    if (this.closed) return;
    if (this.state.phase === 'auth-required' || this.state.phase === 'unavailable') return;
    const record = typeof error === 'object' && error ? error as { status?: number; statusCode?: number; retryAfterMs?: number; resource?: string } : {};
    const status = record.status ?? record.statusCode;
    if (status === 401 || status === 403) {
      this.detach(); this.cancelTimers(); this.phase('auth-required');
      this.options.warning('Session expired. Sign in to reconnect to the existing run.'); return;
    }
    if (status === 404 && record.resource !== 'connection' || String(error).includes('RUN_NOT_FOUND')) {
      this.detach(); this.cancelTimers(); this.phase('unavailable');
      this.options.warning('This run is unavailable on the server. Its last observations and replay seed are retained.'); return;
    }
    if (status === 429) {
      // HTTP recovery and negotiation can finish out of order. A shorter
      // rejection must not release an already acknowledged longer quota pause.
      this.throttledUntil = Math.max(this.throttledUntil, this.env.now() + Math.max(5000, record.retryAfterMs ?? 0));
      this.clear('retryTimer'); this.scheduleRetry();
    }
    if (!this.env.online()) this.phase('offline');
    else if (!this.subscribed) this.phase('recovering');
  }

  private scheduleRetry(): void {
    if (this.suspended() || this.hub || this.retryTimer !== null) return;
    this.state.attempts++;
    const backoff = Math.min(30000, 1000 * 2 ** Math.min(5, this.state.attempts - 1));
    const delay = Math.max(backoff * (0.5 + this.env.random() * 0.5), this.throttledUntil - this.env.now());
    this.state.nextRetryAt = this.env.now() + delay; this.emit();
    // Browser timers overflow above signed int32 milliseconds. Wake in bounded
    // segments; connect checks the original quota deadline before sending.
    this.retryTimer = this.env.timer(() => { this.retryTimer = null; void this.connect(); }, Math.min(delay, maximumTimerDelay));
  }

  private scheduleAudit(): void {
    this.clear('auditTimer');
    if (this.suspended()) return;
    const cadence = this.subscribed ? 15000 : 2500;
    const backoff = this.httpFailures ? Math.min(30000, cadence * 2 ** Math.min(4, this.httpFailures)) * (0.5 + this.env.random() * 0.5) : cadence;
    const delay = Math.max(backoff, this.throttledUntil - this.env.now());
    this.auditTimer = this.env.timer(() => { this.auditTimer = null; void this.sync(); }, Math.min(delay, maximumTimerDelay));
  }

  private async sync(): Promise<void> {
    if (this.suspended() || this.request || this.env.now() < this.throttledUntil) { this.scheduleAudit(); return; }
    const controller = new AbortController(); this.request = controller;
    const timer = this.env.timer(() => controller.abort(), 8000);
    try {
      const value = await this.deadline(this.options.readSnapshot(controller.signal));
      if (this.closed || controller.signal.aborted || this.request !== controller) return;
      const decision = this.options.snapshot(value);
      if (decision === 'invalid' || decision === 'wrong-run' || decision === 'epoch-change') throw new Error('Invalid status snapshot');
      this.httpFailures = 0;
      this.acknowledge(false);
      if (decision !== 'stale') this.finish(value);
    } catch (error) {
      if (!this.closed && this.request === controller) { this.httpFailures++; this.failure(error); }
    } finally {
      this.env.clear(timer); controller.abort();
      if (this.request === controller) this.request = null;
      this.scheduleAudit();
    }
  }

  private scheduleHeartbeat(): void {
    this.clear('heartbeatTimer');
    if (this.closed || !this.subscribed) return;
    this.heartbeatTimer = this.env.timer(() => { this.heartbeatTimer = null; void this.probe(); }, 10000);
  }

  private async probe(): Promise<void> {
    const hub = this.hub;
    if (this.suspended() || !hub || !this.subscribed || this.probing) return;
    this.probing = true;
    const started = this.env.now();
    try {
      const value = await this.deadline(hub.invoke<RunSnapshot>('GetRunSnapshot', this.options.runId));
      if (this.closed || this.hub !== hub) return;
      const decision = this.options.snapshot(value);
      if (decision === 'invalid' || decision === 'wrong-run' || decision === 'epoch-change') throw new Error('Heartbeat snapshot could not be reconciled');
      this.acknowledge(true, this.env.now() - started);
      if (decision !== 'stale') this.finish(value);
    } catch (error) {
      if (!this.closed && this.hub === hub) {
        this.options.event('Socket liveness check failed · recovering through snapshots');
        this.detach(); this.failure(error); void this.sync(); this.scheduleRetry();
      }
    } finally { if (this.hub === hub) { this.probing = false; this.scheduleHeartbeat(); } }
  }

  private wake(reason: 'network' | 'visible' | 'auth'): void {
    if (this.closed) return;
    if (!this.env.online()) {
      this.detach(); this.cancelTimers(); this.request?.abort(); this.phase('offline'); return;
    }
    if (this.state.phase === 'unavailable' || this.state.phase === 'auth-required' && reason !== 'auth') return;
    if (reason === 'auth') { this.throttledUntil = 0; this.phase('recovering'); }
    this.clear('retryTimer'); this.state.attempts = 0; this.state.nextRetryAt = null;
    this.httpFailures = 0;
    void this.sync();
    if (this.hub && this.subscribed) void this.probe(); else void this.connect();
  }
}
