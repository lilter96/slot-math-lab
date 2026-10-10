import { test, expect } from '@playwright/test';
import { HttpFailure } from '../src/lib/realtime/RunConnection';
import { completed, flush, harness, progress, snapshot } from './realtime-fixtures';

test('one subscription owns the transport and stop clears every timer and listener', async () => {
  const h = harness(); h.owner.start(); h.owner.start(); await flush();
  expect(h.hubs).toHaveLength(1); expect(h.hubs[0].calls).toEqual(['SubscribeToRun']);
  expect(h.health.at(-1)?.phase).toBe('live');
  h.owner.stop(); await flush();
  expect(h.hubs[0].stops).toBe(1); expect(h.clock.timers.size).toBe(0); expect(h.clock.listener).toBeNull();
  const states = h.health.length; h.hubs[0].listener(progress(100, 100)); await h.clock.advance(60000);
  expect(h.health).toHaveLength(states);
});
test('failed subscription is never live and retries without the five-attempt ceiling', async () => {
  const h = harness();
  h.configure = hub => { if (h.hubs.length < 7) hub.read = async () => { throw new Error('subscription refused'); }; };
  h.owner.start(); await flush(); expect(h.health.some(s => s.phase === 'live')).toBe(false);
  await h.clock.advance(180000);
  expect(h.hubs).toHaveLength(8); expect(h.health.at(-1)?.phase).toBe('live');
  expect(h.hubs.slice(0, 7).every(hub => hub.stops === 1)).toBe(true);
  h.owner.stop();
});
test('silent socket is replaced after a bounded heartbeat, HTTP keeps metrics advancing', async () => {
  const h = harness(); h.owner.start(); await flush();
  h.hubs[0].read = async () => new Promise(() => {});
  h.server = snapshot(progress(2, 100));
  await h.clock.advance(15000); expect(h.state.progress?.sampleCount).toBe(100);
  await h.clock.advance(4000);
  expect(h.hubs).toHaveLength(2); expect(h.hubs[0].stops).toBe(1);
  expect(h.health.at(-1)?.phase).toBe('live'); expect(h.health.at(-1)?.reconnects).toBe(1);
  h.owner.stop(); expect(h.clock.timers.size).toBe(0);
});
test('valid subscription and heartbeat acknowledgements can trail a newer broadcast', async () => {
  const h = harness(); h.configure = hub => { hub.read = async () => { hub.listener(progress(3, 100)); return snapshot(); }; };
  h.owner.start(); await flush(); expect(h.health.at(-1)?.phase).toBe('live');
  expect(h.state.progress?.sequence).toBe(3);
  await h.clock.advance(10000);
  expect(h.hubs).toHaveLength(1); expect(h.health.at(-1)?.phase).toBe('live');
  expect(h.state.progress?.sequence).toBe(3); h.owner.stop();
});
test('network loss suspends traffic; wake recovers completion missed while offline', async () => {
  const h = harness(); h.owner.start(); await flush();
  h.clock.available = false; h.clock.listener?.('network'); await flush();
  const reads = h.reads, sockets = h.hubs.length;
  await h.clock.advance(120000); expect(h.reads).toBe(reads); expect(h.hubs).toHaveLength(sockets);
  expect(h.health.at(-1)?.phase).toBe('offline');
  h.server = completed(); h.clock.available = true; h.clock.listener?.('network'); await flush();
  expect(h.state.status).toBe('completed'); expect(h.health.at(-1)?.phase).toBe('idle');
  expect(h.clock.timers.size).toBe(0); expect(h.clock.listener).toBeNull();
});
test('expired authentication suspends retry traffic until explicit authentication wake', async () => {
  const h = harness(); h.http = async () => { throw new HttpFailure('expired', 401); };
  h.owner.start(); await flush(); expect(h.health.at(-1)?.phase).toBe('auth-required');
  const reads = h.reads, sockets = h.hubs.length;
  await h.clock.advance(300000); h.clock.listener?.('visible'); await flush();
  expect(h.reads).toBe(reads); expect(h.hubs).toHaveLength(sockets);
  h.http = async () => snapshot(); h.clock.listener?.('auth'); await flush();
  expect(h.health.at(-1)?.phase).toBe('live'); h.owner.stop();
});
test('rate limiting honors Retry-After for both polling and reconnect attempts', async () => {
  const h = harness(); h.configure = hub => { hub.startError = new HttpFailure('limited', 429, 60000); };
  h.http = async () => { throw new HttpFailure('limited', 429, 60000); };
  h.owner.start(); await flush(); await h.clock.advance(59000);
  expect(h.reads).toBe(1); expect(h.hubs).toHaveLength(1);
  h.configure = () => {}; h.http = async () => snapshot();
  await h.clock.advance(1000); expect(h.health.at(-1)?.phase).toBe('live'); h.owner.stop();
});
test('a missing negotiation endpoint recovers without declaring the retained run unavailable', async () => {
  const h = harness();
  h.configure = hub => { hub.startError = new HttpFailure('Proxy endpoint unavailable', 404, 0, 'connection'); };
  h.owner.start(); await flush();
  expect(h.health.at(-1)?.phase).toBe('recovering');
  h.configure = () => {}; await h.clock.advance(1000);
  expect(h.health.at(-1)?.phase).toBe('live'); h.owner.stop();
});
test('out-of-order negotiation and HTTP rejections cannot shorten a quota pause', async () => {
  const h = harness();
  h.http = async () => { throw new HttpFailure('HTTP quota', 429, 60000); };
  h.configure = hub => { hub.startError = new HttpFailure('Negotiation quota', 429, 5000, 'connection'); };
  h.owner.start(); await flush();
  h.configure = () => {}; h.http = async () => snapshot();
  await h.clock.advance(59000); expect(h.hubs).toHaveLength(1); expect(h.reads).toBe(1);
  await h.clock.advance(1000); expect(h.health.at(-1)?.phase).toBe('live'); h.owner.stop();
});
test('long HTTP-date pauses use bounded timers without releasing the quota early', async () => {
  const h = harness(), pause = 60 * 86400000;
  h.http = async () => { throw new HttpFailure('Long pause', 429, pause); };
  h.configure = hub => { hub.startError = new HttpFailure('Shorter pause', 429, 5000, 'connection'); };
  h.owner.start(); await flush();
  expect(h.health.at(-1)?.nextRetryAt).toBe(h.clock.time + pause);
  expect([...h.clock.timers.values()].every(timer => timer.due - h.clock.time <= 2147483647)).toBe(true);
  await h.clock.advance(2147483647); expect(h.hubs).toHaveLength(1); expect(h.reads).toBe(1);
  expect(h.health.at(-1)?.nextRetryAt).toBe(100000 + pause); h.owner.stop();
});
test('HTTP timeout releases single flight even if an adapter ignores abort', async () => {
  const h = harness(); h.configure = hub => { hub.startError = new Error('no socket'); };
  h.http = async () => new Promise(() => {}); h.owner.start(); await flush();
  await h.clock.advance(13000); expect(h.reads).toBe(2);
  h.owner.stop(); await flush(); expect(h.clock.timers.size).toBe(0);
});
test('missing runs preserve their observations without inventing terminal failure or retry storms', async () => {
  const h = harness(); h.http = async () => { throw new HttpFailure('missing', 404); };
  h.owner.start(); await flush(); expect(h.health.at(-1)?.phase).toBe('unavailable');
  expect(h.state.status).toBe('pending'); await h.clock.advance(300000); expect(h.reads).toBe(1); h.owner.stop();
});
