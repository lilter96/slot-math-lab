import { test, expect } from '@playwright/test';
import { calculationRequest } from '../src/lib/measurements/requests';

test('Explicit quota rejection can retry, while ambiguous calculation failures cannot', async () => {
  const original = globalThis.fetch; let calls = 0;
  try {
    globalThis.fetch = async () => ++calls === 1 ? new Response('{}', { status: 429, headers: { 'Retry-After': '0' } }) : Response.json({ result: 7 });
    const status: string[] = [];
    expect(await calculationRequest('/calculation', { seed: 42 }, new AbortController().signal, s => status.push(s))).toEqual({ result: 7 });
    expect(calls).toBe(2); expect(status.some(s => s.includes('rejected calculation'))).toBe(true);
    for (const failure of [() => Promise.reject(new TypeError('Network disappeared')), () => Promise.resolve(new Response('{}', { status: 500 }))]) {
      calls = 0; globalThis.fetch = async () => { calls++; return failure(); };
      await expect(calculationRequest('/calculation', {}, new AbortController().signal, () => {})).rejects.toThrow();
      expect(calls).toBe(1);
    }
  } finally { globalThis.fetch = original; }
});

test('Cancelling a quota wait never sends the deferred calculation', async () => {
  const original = globalThis.fetch; let calls = 0; const controller = new AbortController();
  try {
    globalThis.fetch = async () => { calls++; return new Response('{}', { status: 429, headers: { 'Retry-After': '60' } }); };
    await expect(calculationRequest('/calculation', {}, controller.signal, message => { if (message.includes('Cancel')) controller.abort(); })).rejects.toThrow();
    expect(calls).toBe(1);
  } finally { globalThis.fetch = original; }
});

test('Validation rejection retains actionable compiler details and is never retried', async () => {
  const original = globalThis.fetch; let calls = 0;
  try {
    globalThis.fetch = async () => { calls++; return Response.json({ error: 'Graph validation failed.', errors: [{ code: 'INVALID_MEASUREMENT', message: 'Measurement plan exceeds the 32768-cell storage budget.' }] }, { status: 400 }); };
    await expect(calculationRequest('/validation', {}, new AbortController().signal, () => {})).rejects.toThrow('32768-cell storage budget');
    expect(calls).toBe(1);
  } finally { globalThis.fetch = original; }
});
