import { test, expect } from '@playwright/test';
import { QueryClient } from '@tanstack/react-query';
import { resultsRequest, resultsReadRetry, resultsCalculationRetry, resultsRetryDelay } from '../src/lib/results/api';

test('Results query survives consecutive empty quota rejections and honors both Retry-After deadlines', async () => {
  const original = globalThis.fetch, client = new QueryClient(), attempts: number[] = [];
  try {
    globalThis.fetch = async () => {
      attempts.push(Date.now());
      return attempts.length <= 2 ? new Response('', { status: 429, headers: { 'Retry-After': '1' } }) : Response.json({ runId: 'pinned', value: '1/3' });
    };
    const result = await client.fetchQuery({ queryKey: ['pinned-evidence'], queryFn: () => resultsRequest('runs/pinned/evidence'), retry: resultsReadRetry, retryDelay: resultsRetryDelay });
    expect(result).toEqual({ runId: 'pinned', value: '1/3' }); expect(attempts).toHaveLength(3);
    expect(attempts[1] - attempts[0]).toBeGreaterThanOrEqual(900); expect(attempts[2] - attempts[1]).toBeGreaterThanOrEqual(900);
  } finally { globalThis.fetch = original; client.clear(); }
});
test('Results calculation retries only rejected requests; accepted errors and excessive quota windows stop immediately', async () => {
  const original = globalThis.fetch, client = new QueryClient(); let calls = 0;
  try {
    for (const rejection of [new Response('', { status: 503 }), new Response('', { status: 403 }), new Response('', { status: 429, headers: { 'Retry-After': '120' } })]) {
      calls = 0; globalThis.fetch = async () => { calls++; return rejection.clone(); };
      await expect(client.fetchQuery({ queryKey: ['reference', rejection.status, rejection.headers.get('Retry-After')],
        queryFn: () => resultsRequest('evaluate/graph', { method: 'POST' }), retry: resultsCalculationRetry, retryDelay: resultsRetryDelay })).rejects.toThrow();
      expect(calls).toBe(1);
    }
    calls = 0; globalThis.fetch = async () => { calls++; throw new TypeError('Accepted outcome is unknown'); };
    await expect(client.fetchQuery({ queryKey: ['ambiguous-reference'], queryFn: () => resultsRequest('evaluate/graph', { method: 'POST' }), retry: resultsCalculationRetry, retryDelay: resultsRetryDelay })).rejects.toThrow('Accepted outcome is unknown');
    expect(calls).toBe(1);
  } finally { globalThis.fetch = original; client.clear(); }
});
test('A persistent Results quota rejection ends after its bounded retry budget', async () => {
  const original = globalThis.fetch, client = new QueryClient(); let calls = 0;
  try {
    globalThis.fetch = async () => { calls++; return new Response('', { status: 429, headers: { 'Retry-After': '0' } }); };
    await expect(client.fetchQuery({ queryKey: ['persistent-rejection'], queryFn: () => resultsRequest('runs/pinned/evidence'), retry: resultsReadRetry, retryDelay: resultsRetryDelay }))
      .rejects.toMatchObject({ status: 429 });
    expect(calls).toBe(3);
  } finally { globalThis.fetch = original; client.clear(); }
});
