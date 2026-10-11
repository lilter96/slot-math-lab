import { test, expect } from '@playwright/test';
import { GameServer, type SessionStore, type PlayRound } from '../src/games/server/server';
import type { Game, PlayedRound } from '../src/games/server/game';
import { GameDispatcher } from '../src/games/client/engine/dispatcher';
import { playThrough } from '../src/games/server/play';
import { SpinOrchestrator } from '../src/games/client/engine/spin-orchestrator';
import { SessionManager } from '../src/games/client/engine/session-manager';
import { GameStateMachine } from '../src/games/client/engine/state-machine';
import { GameEventBus } from '../src/games/client/engine/event-bus';
import type { GameRuntime } from '../src/games/client/engine/game-client';

const game: Game<{ count: number }, { win: number }> = {
  id: 'test', initialState: () => ({ count: 0 }), validate: () => null,
  graph: () => ({ id: 'editable' }),
  steps: played => [{ action: 'spin', reply: (_, state) => ({ state: { count: state.count + 1 }, result: { win: played.win } }) }],
  wallet: {
    initialBalance: 100, cost: () => 10, award: result => result.win,
    identified: (id, revision) => !!id && Number.isSafeInteger(revision),
    fingerprint: (action, payload, revision) => [action, payload, revision],
    messages: { noSession: 'session required', expired: 'expired', identity: 'identity required', reused: 'reused', stale: 'stale' },
  },
};
function store(): SessionStore {
  const entries = new Map<string, string>();
  return { getItem: key => entries.get(key) ?? null, setItem: (key, value) => void entries.set(key, value), removeItem: key => void entries.delete(key) };
}
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(yes => { resolve = yes; });
  return { promise, resolve };
}
const round = { win: 2, trace: {} };
const open = async (server: GameServer) => (await server.handle('test', 'state', {})).body as { sessionId: string };
const request = (sessionId: string, requestId: string) => ({ sessionId, requestId, expectedRevision: 0, payload: {} });

for (const sameRequest of [false, true]) {
  test(`Concurrent actions across server instances ${sameRequest ? 'replay one command' : 'reject the stale command'} without overwriting the wallet`, async () => {
    const storage = store(), entered = deferred<void>(), finish = deferred<PlayedRound>();
    let calls = 0;
    const play: PlayRound = async () => { calls++; entered.resolve(); return finish.promise; };
    const first = new GameServer(play, storage, () => 42).register(game);
    const second = new GameServer(play, storage, () => 99).register(game);
    const { sessionId } = await open(first);
    const a = first.handle('test', 'spin', request(sessionId, 'one'));
    await entered.promise;
    const b = second.handle('test', 'spin', request(sessionId, sameRequest ? 'one' : 'two'));
    finish.resolve(round);
    const [accepted, concurrent] = await Promise.all([a, b]);
    expect(accepted.status).toBe(200);
    expect(concurrent).toEqual(sameRequest ? accepted : { status: 409, body: { error: 'stale' } });
    expect(calls).toBe(1);
    expect((await second.handle('test', 'state', { sessionId })).body).toMatchObject({ balance: 92, revision: 1, state: { count: 1 } });
    expect(await second.handle('test', 'spin', request(sessionId, 'one'))).toEqual(accepted);
  });
}

test('Cancellation rejects a stalled play promptly, leaves the wallet unchanged and permits retry with the same identity', async () => {
  const storage = store(), entered = deferred<void>(), stalled = deferred<PlayedRound>();
  let calls = 0, forwarded: AbortSignal | undefined;
  const server = new GameServer(async (_, seed, index, signal) => {
    expect([seed, index]).toEqual([42, 0]); forwarded = signal; calls++; entered.resolve();
    return calls === 1 ? stalled.promise : round;
  }, storage, () => 42).register(game);
  const { sessionId } = await open(server), controller = new AbortController();
  const pending = server.handle('test', 'spin', request(sessionId, 'one'), controller.signal);
  const rejection = expect(pending).rejects.toThrow('cancelled');
  await entered.promise;
  controller.abort(new Error('cancelled'));
  await rejection;
  expect(forwarded).toBe(controller.signal);
  expect((await server.handle('test', 'state', { sessionId })).body).toMatchObject({ balance: 100, revision: 0 });
  expect((await server.handle('test', 'spin', request(sessionId, 'one'))).body).toMatchObject({ balance: 92, revision: 1 });
  stalled.resolve({ win: 999, trace: {} });
  await Promise.resolve();
  expect((await server.handle('test', 'state', { sessionId })).body).toMatchObject({ balance: 92, revision: 1 });
});

test('A cancelled queued action never overtakes or commits beside the active action', async () => {
  const entered = deferred<void>(), finish = deferred<PlayedRound>();
  let calls = 0;
  const server = new GameServer(async () => { calls++; entered.resolve(); return finish.promise; }, store(), () => 42).register(game);
  const { sessionId } = await open(server);
  const active = server.handle('test', 'spin', request(sessionId, 'one'));
  await entered.promise;
  const controller = new AbortController();
  const waiter = server.handle('test', 'spin', request(sessionId, 'two'), controller.signal);
  const rejection = expect(waiter).rejects.toThrow('queue cancelled');
  controller.abort(new Error('queue cancelled'));
  await rejection;
  const state = server.handle('test', 'state', { sessionId });
  finish.resolve(round);
  await active;
  expect((await state).body).toMatchObject({ balance: 92, revision: 1 });
  expect(calls).toBe(1);
});

test('Cancellation reaches the transport and interrupts a retry delay without sending another round', async () => {
  const controller = new AbortController(), waiting = deferred<void>();
  let calls = 0;
  const play = playThrough(async (_, signal) => {
    expect(signal).toBe(controller.signal); calls++;
    return new Response('{}', { status: 429 });
  }, async () => { waiting.resolve(); await new Promise(() => {}); });
  const pending = play({}, 42, 0, controller.signal);
  const rejection = expect(pending).rejects.toThrow('retry cancelled');
  await waiting.promise; controller.abort(new Error('retry cancelled')); await rejection;
  expect(calls).toBe(1);
});

for (const failure of ['read', 'write', 'all']) {
  test(`Dispatcher retains the current session when storage ${failure} fails and clear does not revive it`, async () => {
    const descriptor = Object.getOwnPropertyDescriptor(globalThis, 'localStorage');
    Object.defineProperty(globalThis, 'localStorage', { configurable: true, value: {
      getItem: () => { if (failure !== 'write') throw new Error('unavailable'); return 'old'; },
      setItem: () => { if (failure !== 'read') throw new Error('unavailable'); },
      removeItem: () => { throw new Error('unavailable'); },
    } });
    try {
      const asked: { sessionId?: string }[] = [];
      const dispatcher = new GameDispatcher('woodland-whisper', '', async (_, init) => {
        asked.push(JSON.parse(init.body));
        return Response.json({ sessionId: 'current', state: {} });
      });
      await dispatcher.dispatch('state', {});
      await dispatcher.dispatch('freespin', {});
      expect(asked[1].sessionId).toBe('current');
      dispatcher.clearSession();
      await dispatcher.dispatch('state', {});
      expect(asked[2].sessionId).toBeUndefined();
    } finally {
      if (descriptor) Object.defineProperty(globalThis, 'localStorage', descriptor);
      else Reflect.deleteProperty(globalThis, 'localStorage');
    }
  });
}

for (const spins of [0, 2]) {
  test(`Auto-spin ${spins === 0 ? 'unlimited' : 'finite'} starts, prevents manual overlap and cancels its scheduled continuation`, async () => {
    const fsm = new GameStateMachine(), session = new SessionManager(100), bus = new GameEventBus();
    let calls = 0;
    const completed = deferred<void>();
    const runtime = { applyState: () => {}, presentResult: async () => {} } as unknown as GameRuntime<'woodland-whisper'>;
    const orchestrator = new SpinOrchestrator(fsm, session, runtime, bus, {
      spinCost: () => 1, doSpin: async () => {
        calls++; completed.resolve(); return { sessionId: 'session', state: {} } as never;
      },
    }, () => ({ autoSpinDelayMs: 100 } as never));
    orchestrator.startAutoSpin({ spins, stopOnWin: false, stopOnBonus: false });
    await completed.promise;
    await expect.poll(() => session.balance).toBe(99);
    await orchestrator.spin(1);
    expect(calls).toBe(1);
    orchestrator.stopAutoSpin();
    await new Promise(resolve => setTimeout(resolve, 150));
    expect(calls).toBe(1);
    await orchestrator.spin(1);
    expect(calls).toBe(2);
  });
}


test('Cancellation between presentation reply and commit does not save the wallet or reuse an uncommitted layout', async () => {
  const controller = new AbortController();
  let calls = 0;
  const replying = { ...game, steps: () => [{ action: 'spin', reply: () => {
    queueMicrotask(() => controller.abort(new Error('cancel before commit')));
    return { state: { count: 1 }, result: { win: 2 } };
  } }] };
  const storage = store();
  const server = new GameServer(async () => { calls++; return round; }, storage, () => 42).register(replying);
  const { sessionId } = await open(server);
  await expect(server.handle('test', 'spin', request(sessionId, 'one'), controller.signal)).rejects.toThrow('cancel before commit');
  expect((await server.handle('test', 'state', { sessionId })).body).toMatchObject({ balance: 100, revision: 0, state: { count: 0 } });
  server.register(game);
  expect((await server.handle('test', 'spin', request(sessionId, 'one'))).body).toMatchObject({ balance: 92, revision: 1 });
  expect(calls).toBe(2);
});
