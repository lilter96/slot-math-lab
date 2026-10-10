import { test, expect } from '@playwright/test';
import { matchesEvidenceSource, simulationEvidence, validateRunEvidence } from '../src/lib/results/evidence';
import { readSimulationEvidence } from '../src/lib/results/api';
import { type RunEvidence } from '../src/lib/results/model';
import { snapshot, progress } from './realtime-fixtures';

function evidence(n = 100, sequence = 1): RunEvidence {
  const run = snapshot({ ...progress(sequence, n), stdErr: 1 / Math.sqrt(n) });
  return { run, model: { name: 'Pinned coin', modelHash: 'b'.repeat(64), targetRtp: .98, winCap: 10000 },
    pinnedConfig: { nodes: [], edges: [] }, inputVerified: true, computedConfigHash: run.configHash };
}
const context = { points: [], reference: null, referenceNote: 'No reference attached.', dashboard: {} };

test('Retained diagnostic identity distinguishes the sample producer from the later calculator', () => {
  const run = evidence().run;
  run.runtimeProvenance = { framework: '.NET 10', numericalMethods: 'v1', measurementContract: 'v1', coreBinarySha256: 'a'.repeat(64), apiBinarySha256: 'b'.repeat(64) };
  const source = { configHash: run.configHash, measurementHash: null, sequence: run.sequence!, paidRounds: run.progress!.sampleCount, producer: run.runtimeProvenance };
  expect(matchesEvidenceSource(source, run)).toBe(true);
  for (const key of ['framework', 'numericalMethods', 'measurementContract', 'coreBinarySha256', 'apiBinarySha256'] as const)
    expect(matchesEvidenceSource({ ...source, producer: { ...source.producer, [key]: 'different' } }, run)).toBe(false);
  expect(matchesEvidenceSource({ ...source, producer: null }, run)).toBe(false);
  expect(matchesEvidenceSource({ ...source, sequence: source.sequence + 1 }, run)).toBe(false);
  expect(matchesEvidenceSource({ ...source, paidRounds: source.paidRounds + 1 }, run)).toBe(false);
});

test('Simulation export uses one authoritative population and labels browser history separately', () => {
  const earlier = evidence(), current = evidence(200, 2);
  current.run.status = current.run.progress!.status = 'cancelled';
  current.run.resultJson = current.run.progress!.resultJson = '{"status":"cancelled","sampleCount":200}';
  const point = (n: number) => ({ n, rtp: .98, stdErr: .1, elapsedMs: 100, rate: 1000 });
  const bundle = simulationEvidence(current, earlier.run, { ...context, points: [point(100), point(200), point(300)] });
  expect(bundle.run).toEqual(current.run); expect(bundle.progress!.sampleCount).toBe(200);
  expect(bundle.convergence.map(p => p.n)).toEqual([100, 200]); expect(bundle.integrity.complete).toBe(false);
  expect(bundle.pinnedGraph).toMatchObject({ inputVerified: true, computedConfigHash: earlier.run.configHash });
  expect(bundle.schemaVersion).toBe('slotmath.simulation.v2');
});

test('Evidence export rejects missing models, changed producers, inconsistent results and wrong or stale populations', () => {
  const expected = evidence().run;
  const mutations: ((v: RunEvidence) => void)[] = [
    v => { v.run.id = v.run.progress!.runId = 'another'; },
    v => { v.inputVerified = false; }, v => { v.pinnedConfig = null; },
    v => { v.computedConfigHash = 'c'.repeat(64); },
    v => { v.run.seed++; }, v => { v.run.progress!.hitFrequency = .3; },
    v => { v.run.sequence = v.run.progress!.sequence = 0; },
    v => { v.run.runtimeProvenance = { framework: 'other', numericalMethods: 'other', measurementContract: 'other', coreBinarySha256: null, apiBinarySha256: null }; },
  ];
  for (const mutate of mutations) { const current = evidence(); mutate(current); expect(() => simulationEvidence(current, expected, context)).toThrow(); }
  const unverified = evidence(); unverified.inputVerified = false; unverified.pinnedConfig = null;
  // Results may inspect an unverified archive; a complete simulation export
  // requires its verified input and never substitutes a file without it.
  expect(validateRunEvidence(unverified, expected.id).inputVerified).toBe(false);
});

test('Evidence reads retry an explicit quota rejection and keep ambiguous errors visible', async () => {
  const original = globalThis.fetch; let calls = 0;
  try {
    globalThis.fetch = async () => ++calls === 1 ? new Response('{}', { status: 429, headers: { 'Retry-After': '0' } }) : Response.json(evidence());
    const messages: string[] = [];
    expect((await readSimulationEvidence('run', new AbortController().signal, m => messages.push(m))).inputVerified).toBe(true);
    expect(calls).toBe(2); expect(messages.some(m => m.includes('Server quota'))).toBe(true);
    for (const fail of [() => Promise.resolve(new Response('{}', { status: 503 })), () => Promise.reject(new TypeError('Connection lost'))]) {
      calls = 0; globalThis.fetch = async () => { calls++; return fail(); };
      await expect(readSimulationEvidence('run', new AbortController().signal, () => {})).rejects.toThrow(); expect(calls).toBe(1);
    }
  } finally { globalThis.fetch = original; }
});

test('Cancelling an evidence quota wait aborts the deferred read', async () => {
  const original = globalThis.fetch, controller = new AbortController(); let calls = 0;
  try {
    globalThis.fetch = async () => { calls++; return new Response('{}', { status: 429, headers: { 'Retry-After': '60' } }); };
    await expect(readSimulationEvidence('run', controller.signal, message => { if (message.includes('Cancel')) controller.abort(); })).rejects.toThrow();
    expect(calls).toBe(1);
  } finally { globalThis.fetch = original; }
});
