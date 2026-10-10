import { test, expect } from '@playwright/test';
import * as signalR from '@microsoft/signalr';
import { createRunHub, RunHubHttpClient } from '../src/lib/realtime/signalRHub';
import { HttpFailure, retryAfterMilliseconds } from '../src/lib/realtime/httpFailure';

for (const status of [401, 403, 404, 429]) test(`real SignalR start preserves HTTP ${status} negotiation metadata`, async () => {
  const hub = createRunHub('http://localhost/hubs/runs', async () => new Response('', {
    status, headers: { 'Retry-After': '60' },
  }));
  await expect(hub.start()).rejects.toMatchObject({ status, retryAfterMs: 60000, resource: 'connection' });
  await hub.stop();
});

test('a later network failure never inherits a previous negotiation rejection', async () => {
  let attempts = 0;
  const hub = createRunHub('http://localhost/hubs/runs', async () => {
    if (++attempts === 1) return new Response('', { status: 429, headers: { 'Retry-After': '60' } });
    throw new TypeError('Network disconnected');
  });
  await expect(hub.start()).rejects.toBeInstanceOf(HttpFailure);
  try { await hub.start(); throw new Error('Expected a network failure.'); }
  catch (error) {
    expect(error).not.toBeInstanceOf(HttpFailure);
    expect(String(error)).toContain('Network disconnected');
  }
  await hub.stop();
});

test('HTTP adapter preserves credentials, bodies and response types without leaving abort handlers', async () => {
  const calls: RequestInit[] = [];
  const client = new RunHubHttpClient(async (_, init) => { calls.push(init!); return new Response('payload'); });
  const abortSignal: signalR.AbortSignal = { aborted: false, onabort: null };
  const response = await client.send({ method: 'POST', url: 'http://localhost/negotiate',
    content: '', withCredentials: true, abortSignal, timeout: 1000 });
  expect(response.content).toBe('payload'); expect(calls[0].credentials).toBe('include');
  expect(calls[0].body).toBeUndefined(); expect(abortSignal.onabort).toBeNull();
  const binary = await client.send({ method: 'POST', url: 'http://localhost/negotiate',
    content: new Uint8Array([1, 2]).buffer, responseType: 'arraybuffer' });
  expect(binary.content).toBeInstanceOf(ArrayBuffer);
  expect(new Headers(calls[1].headers).get('Content-Type')).toBe('application/octet-stream');
  expect(calls[1].credentials).toBe('same-origin'); expect(client.failure).toBeNull();
});

test('HTTP adapter abort and timeout release requests and never invent an HTTP rejection', async () => {
  const client = new RunHubHttpClient(async (_, init) => new Promise((_, reject) => {
    init!.signal!.addEventListener('abort', () => reject(init!.signal!.reason), { once: true });
  }));
  const abortSignal: signalR.AbortSignal = { aborted: false, onabort: null };
  const pending = client.send({ method: 'POST', url: 'http://localhost/negotiate', abortSignal, timeout: 1000 });
  abortSignal.aborted = true; abortSignal.onabort!();
  await expect(pending).rejects.toBeInstanceOf(signalR.AbortError); expect(abortSignal.onabort).toBeNull();
  await expect(client.send({ method: 'POST', url: 'http://localhost/negotiate', timeout: 10 })).rejects.toBeInstanceOf(signalR.TimeoutError);
  await expect(client.send({ method: 'POST', url: 'http://localhost/negotiate', abortSignal })).rejects.toBeInstanceOf(signalR.AbortError);
  expect(client.failure).toBeNull();
});

test('Retry-After parses both wire formats and rejects nonfinite or elapsed delays', () => {
  const now = Date.parse('2026-10-10T00:00:00Z');
  expect(retryAfterMilliseconds(' 60 ', now)).toBe(60000);
  expect(retryAfterMilliseconds('Sat, 10 Oct 2026 00:01:00 GMT', now)).toBe(60000);
  for (const value of [null, '', 'invalid', '9'.repeat(400), 'Sat, 10 Oct 2026 00:00:00 GMT'])
    expect(retryAfterMilliseconds(value, now)).toBe(0);
});
