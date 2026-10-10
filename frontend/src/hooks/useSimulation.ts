import { validateExecution, sameExecution } from '../lib/measurements/execution';
import { chartMeasurements, serializeCheckpoint } from '../lib/measurements/checkpoints';
import { restoreTrendPoints, type MeasurementTrendPoint } from '../lib/measurements/trends';
import { useEffect } from 'react';
import { create } from 'zustand';
import { RunConnection, HttpFailure, type ConnectionHealth, type ConnectionPhase } from '../lib/realtime/RunConnection';
import { progressDecision, snapshotDecision, terminal, validSnapshot, type Decision, type LiveProgress, type RunSnapshot } from '../lib/realtime/runProtocol';
export { terminal };
export type { LiveProgress, RunSnapshot };
import { useAppStore } from '../store';
import { rootProject, loadProject } from '../lib/projectFiles';
import { createDogHouseGraph } from '../games/doghouse/graph';
import { createExpectationGraph } from '../games/doghouse/expectationGraph';
import { graphRequest, type GraphRound } from '../games/doghouse/api';

import { samePlan, definition, type MeasurementDefinition } from '../lib/measurements/model';
import { useMeasurementWorkspace } from '../lib/measurements/store';

export type LivePoint = MeasurementTrendPoint;
interface Session {
  run: RunSnapshot | null; progress: LiveProgress | null; points: LivePoint[]; reference: number | null;
  target: number | null; referenceNote: string; model: string; error: string; starting: boolean;
  connection: ConnectionPhase; health: ConnectionHealth; receivedAt: number; events: { time: string; text: string }[];
  history: RunSnapshot[]; launchNote: string;
}
const key = 'slotmath-simulation-v2';
const initialHealth: ConnectionHealth = { phase: 'idle', reconnects: 0, attempts: 0, lastConfirmedAt: 0, lastSocketAt: 0, roundTripMs: null, nextRetryAt: null };
const defaults: Session = { run: null, progress: null, points: [], reference: null, target: null,
  referenceNote: 'Run the model to calculate its exact reference.', model: '', error: '', starting: false,
  launchNote: '', connection: 'idle', health: initialHealth, receivedAt: 0, events: [], history: [] };
function restore(): Session {
  try {
    const raw = localStorage.getItem(key) ?? 'null';
    if (raw.length > 1_500_000) return defaults;
    const saved = JSON.parse(raw);
    if (!saved || !Array.isArray(saved.points) || !Array.isArray(saved.history)) return defaults;
    return { ...defaults, ...saved, points: restoreTrendPoints(saved.points), starting: false, connection: 'idle', health: initialHealth, receivedAt: 0 };
  } catch { return defaults; }
}
const useSession = create<Session>(() => restore());
let lastSave = 0;
useSession.subscribe((s, previous) => {
  const transition = s.run?.id !== previous.run?.id || s.run?.status !== previous.run?.status
    || s.connection !== previous.connection && ['offline', 'auth-required', 'unavailable'].includes(s.connection);
  if (!transition && Date.now() - lastSave < 500 && !terminal(s.run?.status)) return;
  lastSave = Date.now();
  try { localStorage.setItem(key, serializeCheckpoint(s)); } catch { /* The running job remains available on the server. */ }
});
// Flush the latest observation before a suspended tab or page reload. Recovery
// still verifies this browser checkpoint against the pinned server snapshot.
function flushSession() { try { localStorage.setItem(key, serializeCheckpoint(useSession.getState())); } catch { /* Server retains the run. */ } }
window.addEventListener('pagehide', flushSession);
document.addEventListener('visibilitychange', () => { if (document.hidden) flushSession(); });
function log(text: string) {
  useSession.setState(s => ({ events: [...s.events, { time: new Date().toLocaleTimeString(), text }].slice(-12) }));
}
async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, { ...init, signal: init?.signal ?? AbortSignal.timeout(15000) });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) {
    const retry = res.headers.get('Retry-After');
    const retryAfterMs = retry ? /^\d+$/.test(retry) ? Number(retry) * 1000 : Math.max(0, Date.parse(retry) - Date.now()) : 0;
    throw new HttpFailure(res.status === 429 ? `Server rate limit. Retry in ${Math.ceil((retryAfterMs || 5000) / 1000)} seconds.` : data.error ?? data.title ?? `HTTP ${res.status}`, res.status, retryAfterMs);
  }
  return data;
}
let launchController: AbortController | null = null;
/** Retry only a explicitly rejected request, never an accepted or ambiguously failed POST. */
async function launchRequest<T>(path: string, body: unknown, signal: AbortSignal): Promise<T> {
  signal.throwIfAborted();
  const init = { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) };
  try { return await request<T>(path, init); }
  catch (error) {
    if (!(error instanceof HttpFailure) || error.status !== 429) throw error;
    const delay = error.retryAfterMs || 5000;
    if (delay > 60000) throw error;
    signal.throwIfAborted();
    const note = `Server rate limit · retrying the rejected request in ${Math.ceil(delay / 1000)}s. No run has been queued by this request.`;
    useSession.setState({ launchNote: note }); log(note);
    await new Promise<void>((resolve, reject) => {
      const aborted = () => { clearTimeout(timer); reject(signal.reason); };
      const timer = setTimeout(() => { signal.removeEventListener('abort', aborted); resolve(); }, delay);
      signal.addEventListener('abort', aborted, { once: true });
    });
    signal.throwIfAborted(); useSession.setState({ launchNote: 'Retrying the rejected launch request…' });
    return request<T>(path, init);
  }
}
function recordTerminal(run: RunSnapshot) {
  if (!terminal(run.status) || !run.resultJson) return;
  let error = '';
  if (run.status === 'failed') {
    try { error = JSON.parse(run.resultJson).error ?? 'Run failed.'; } catch { error = 'Run failed.'; }
  }
  useSession.setState(s => ({ error, history: [run, ...s.history.filter(r => r.id !== run.id)].slice(0, 8) }));
}
function accept(value: unknown): Decision {
  const state = useSession.getState();
  const decision = progressDecision(state.run, state.progress, value);
  if (decision !== 'accept' && decision !== 'duplicate') return decision;
  const msg = value as LiveProgress;
  const run: RunSnapshot = { ...state.run!, status: msg.status, sequence: msg.sequence, streamEpoch: msg.streamEpoch,
    progress: msg, resultJson: msg.resultJson ?? state.run?.resultJson, completedAt: msg.completedAt ?? state.run?.completedAt };
  if (decision === 'duplicate') {
    // A complete HTTP snapshot may enrich the final frame at the same revision.
    if (msg.resultJson && !state.run?.resultJson || msg.measurements?.some((m, i) => m.analysis && !state.progress?.measurements?.[i]?.analysis)) {
      const points = state.points.map(point => point.n === msg.sampleCount ? { ...point, measurements: chartMeasurements(msg.measurements) } : point);
      useSession.setState({ run, progress: msg, points }); recordTerminal(run);
    }
    return decision;
  }
  const last = state.points.at(-1);
  let points = state.points;
  if (msg.sampleCount > 0) {
    // Parallel batches can arrive a millisecond apart. Measure over at
    // least a second instead of turning delivery bursts into speed spikes.
    const baseline = state.points.findLast(point => msg.elapsedMs - point.elapsedMs >= 1000);
    const rate = baseline ? (msg.sampleCount - baseline.n) * 1000 / (msg.elapsedMs - baseline.elapsedMs)
      : msg.sampleCount * 1000 / Math.max(1, msg.elapsedMs);
    const point = { n: msg.sampleCount, rtp: msg.runningRtp, stdErr: msg.stdErr, elapsedMs: msg.elapsedMs, rate: Math.max(0, rate), measurements: chartMeasurements(msg.measurements) };
    points = last?.n === msg.sampleCount ? [...points.slice(0, -1), point] : [...points, point];
    const pointBudget = Math.min(600, Math.max(80, Math.floor(2400 / Math.max(1, msg.measurements?.length ?? 0))));
    if (points.length > pointBudget) points = points.filter((_, i) => i % 2 === 0 || i === points.length - 1);
  }
  useSession.setState({ progress: msg, points, receivedAt: Date.now(), run });
  if (msg.status !== state.run!.status) log(`Run ${msg.status} · ${msg.sampleCount.toLocaleString()} rounds`);
  recordTerminal(run);
  return decision;
}
function acceptSnapshot(value: unknown): Decision {
  const current = useSession.getState();
  const decision = snapshotDecision(current.run, current.progress, value);
  if (decision !== 'accept' && decision !== 'duplicate' && decision !== 'reset') return decision;
  const run = value as RunSnapshot;
  if (decision === 'reset') {
    // Crash recovery is an explicit new epoch. Display only the durable prefix.
    const p = run.progress!;
    const prefix = current.points.filter(point => point.n < p.sampleCount);
    const points = p.sampleCount ? [...prefix, { n: p.sampleCount, rtp: p.runningRtp, stdErr: p.stdErr,
      elapsedMs: p.elapsedMs, rate: p.sampleCount * 1000 / Math.max(1, p.elapsedMs), measurements: chartMeasurements(p.measurements) }] : [];
    useSession.setState({ run, progress: p, points, receivedAt: Date.now() });
    log('Server restart · recovered checkpoint; interrupted run can be replayed with its recorded seed');
  } else {
    accept(run.progress);
    useSession.setState({ run: { ...run, resultJson: run.resultJson ?? useSession.getState().run?.resultJson } });
  }
  recordTerminal(useSession.getState().run!);
  return decision;
}
export async function startSimulation(seed: number, samples: number, workers: number, execution?: import('../lib/measurements/execution').ExecutionOptions) {
  if (useSession.getState().starting || (useSession.getState().run && !terminal(useSession.getState().run?.status) && useSession.getState().connection !== 'unavailable')) return;
  if (!Number.isSafeInteger(seed) || !Number.isInteger(samples) || samples < 1 || samples > 10_000_000 || !Number.isInteger(workers) || workers < 1 || workers > 4) {
    useSession.setState({ error: 'Use a safe integer seed, 1–10,000,000 complete rounds and 1–4 workers.' }); return;
  }
  const controller = new AbortController();
  launchController = controller;
  try {
    if (useAppStore.getState().verificationSource) {
      loadProject(useAppStore.getState().verificationSource!);
      useAppStore.setState({ verificationSource: null, graphTrail: [] });
    }
    if (!useAppStore.getState().nodes.length) loadProject(createDogHouseGraph());
    const config = rootProject();
    if (!config) throw new Error('Build a valid model before running.');
    const measurements = useMeasurementWorkspace.getState().metrics.map(definition);
    if (execution) validateExecution(execution, samples, workers);
    const target = Number((config.initialState as Record<string, unknown>)?.targetRtpPercent) / 100;
    const history = useSession.getState().history;
    useSession.setState({ starting: true, error: '', launchNote: '' });
    log('Saving and pinning the full constructor graph');
    const saved = await launchRequest<{ id: string }>('/api/configs', { config }, controller.signal);
    const run = await launchRequest<RunSnapshot>('/api/runs', { configId: saved.id, seed, sampleSize: samples, degreeOfParallelism: workers, progressBatchSize: 1000, measurements, execution }, controller.signal);
    if (controller.signal.aborted) {
      if (validSnapshot(run)) await request(`/api/runs/${encodeURIComponent(run.id)}`, { method: 'DELETE' }).catch(() => {});
      controller.signal.throwIfAborted();
    }
    if (!validSnapshot(run) || run.configId !== saved.id || run.seed !== seed || run.degreeOfParallelism !== workers || run.progress?.totalSamples !== samples
      || !samePlan(measurements, run.measurements ?? []) || !sameExecution(execution, run.execution)) {
      if (validSnapshot(run)) await request(`/api/runs/${encodeURIComponent(run.id)}`, { method: 'DELETE' }).catch(() => {});
      throw new Error('The server did not pin the requested measurement plan and run inputs.');
    }
    useSession.setState({ ...defaults, history, target: Number.isFinite(target) ? target : null, model: String(config.name), run, starting: false, connection: 'connecting' });
    log(`Queued ${samples.toLocaleString()} rounds · seed ${seed} · ${workers} workers`);
    // This is an authored AST proof, evaluated by the generic graph engine.
    // Never substitute a sampled preflight result for an exact reference.
    void (async () => {
      try {
        let reference: number | null = null;
        let note = 'Exact evaluation exceeded its budget; target is not a verified reference.';
        try {
          const proof = createExpectationGraph(config);
          const result = await graphRequest<GraphRound>('play/round', { config: proof, seed: 42, roundIndex: 0 });
          const value = result.state.expectedRtp as { displayValue: number };
          reference = value.displayValue;
          note = 'Exact expectation from the editable constructor proof; not an exact payout distribution.';
        } catch {
          const result = await graphRequest<{ rtp?: number; provenance?: string }>('evaluate/light', { config, seed });
          if (result.provenance === 'Exact' && result.rtp !== undefined) { reference = result.rtp; note = 'Exact expectation of this pinned graph.'; }
        }
        if (useSession.getState().run?.id === run.id) useSession.setState({ reference, referenceNote: note });
      } catch (e) {
        if (useSession.getState().run?.id === run.id) useSession.setState({ referenceNote: `Reference unavailable: ${String(e)}` });
      }
    })();
  } catch (e) { useSession.setState({ starting: false, launchNote: '', error: controller.signal.aborted ? '' : String(e) }); }
  finally { if (launchController === controller) launchController = null; }
}
export async function cancelSimulation() {
  if (useSession.getState().starting && launchController) { launchController.abort(new DOMException('Launch cancelled.', 'AbortError')); useSession.setState({ launchNote: 'Cancelling launch…' }); return; }
  const run = useSession.getState().run;
  if (!run || terminal(run.status)) return;
  try {
    acceptSnapshot(await request<RunSnapshot>(`/api/runs/${run.id}`, { method: 'DELETE' }));
    log('Cancellation requested · preserving partial results');
  } catch (e) {
    // Completion may race with cancellation. Reconcile the authoritative status.
    try { acceptSnapshot(await request<RunSnapshot>(`/api/runs/${run.id}`)); }
    catch { if (useSession.getState().run?.id === run.id) useSession.setState({ error: String(e) }); }
  }
}
export async function openSimulation(run: RunSnapshot, metadata?: { model: string; target: number | null }) {
  const progress = run.progress ?? null;
  const points: LivePoint[] = progress?.sampleCount ? [{ n: progress.sampleCount, rtp: progress.runningRtp, stdErr: progress.stdErr,
    elapsedMs: progress.elapsedMs, rate: progress.sampleCount * 1000 / Math.max(1, progress.elapsedMs), measurements: chartMeasurements(progress.measurements) }] : [];
  useSession.setState({ ...defaults, history: useSession.getState().history, run, progress, points, model: metadata?.model ?? `Saved run #${run.id}`, target: metadata?.target ?? null });
  try { acceptSnapshot(await request<RunSnapshot>(`/api/runs/${run.id}`)); }
  catch (error) { if (useSession.getState().run?.id === run.id) useSession.setState({ error: String(error) }); }
}
export function useSimulation() { return useSession(); }
let connection: RunConnection | null = null;
export function reconnectSimulation() { connection?.retry(); }

/** Mounted once in Layout, so route changes neither reconnect nor lose updates. */
export function useSimulationConnection() {
  const id = useSession(s => s.run?.id);
  const settled = useSession(s => terminal(s.run?.status) && !!s.run?.resultJson);
  useEffect(() => {
    if (!id || settled) return;
    const owner = new RunConnection({
      runId: id,
      readSnapshot: signal => request(`/api/runs/${id}`, { signal }),
      progress: accept, snapshot: acceptSnapshot,
      event: message => { if (useSession.getState().run?.id === id) log(message); },
      health: health => {
        if (useSession.getState().run?.id !== id) return;
        useSession.setState(s => ({ health, connection: health.phase, error: health.phase === 'live' && !terminal(s.run?.status) ? '' : s.error }));
        if (health.phase === 'auth-required') window.dispatchEvent(new Event('slotmath:auth-required'));
      },
      warning: error => { if (useSession.getState().run?.id === id) useSession.setState({ error }); },
    });
    connection = owner;
    owner.start();
    return () => { owner.stop(); if (connection === owner) connection = null; };
  }, [id, settled]);
}

/** Replay/extend an immutable saved version, without saving the editor or repinning latest. */
export async function startPinnedSimulation(input: { configId: string; configVersion: number; configHash: string; model: string;
  target: number | null; seed: number; samples: number; workers: number; reference?: number | null; referenceNote?: string; measurements?: MeasurementDefinition[]; measurementHash?: string | null; execution?: import('../lib/measurements/execution').ExecutionOptions | null }) {
  const state = useSession.getState();
  if (state.starting || state.run && !terminal(state.run.status) && state.connection !== 'unavailable') throw new Error('Another run is active. Finish or cancel it in Simulate before launching a new run.');
  if (!Number.isSafeInteger(input.seed) || !Number.isInteger(input.samples) || input.samples < 1 || input.samples > 10_000_000
    || !Number.isInteger(input.workers) || input.workers < 1 || input.workers > 4) throw new Error('Use a safe integer seed, 1–10,000,000 rounds and 1–4 workers.');
  if (input.execution) validateExecution(input.execution, input.samples, input.workers);
  const controller = new AbortController(); launchController = controller;
  useSession.setState({ starting: true, error: '', launchNote: '' });
  try {
    const run = await launchRequest<RunSnapshot>('/api/runs', { configId: input.configId, configVersion: input.configVersion, seed: input.seed, sampleSize: input.samples,
        degreeOfParallelism: input.workers, progressBatchSize: 1000, measurements: input.measurements ?? [], execution: input.execution }, controller.signal);
    if (controller.signal.aborted) {
      if (validSnapshot(run)) await request(`/api/runs/${encodeURIComponent(run.id)}`, { method: 'DELETE' }).catch(() => {});
      controller.signal.throwIfAborted();
    }
    if (!validSnapshot(run) || run.configId !== input.configId || run.configHash !== input.configHash || run.configVersion !== input.configVersion
      || !sameExecution(input.execution, run.execution) || (run.measurementHash ?? null) !== (input.measurementHash ?? null) || run.seed !== input.seed || run.degreeOfParallelism !== input.workers || run.progress?.totalSamples !== input.samples) {
      if (validSnapshot(run)) await request(`/api/runs/${encodeURIComponent(run.id)}`, { method: 'DELETE' }).catch(() => {});
      throw new Error('The server did not pin the requested model, seed and round budget. The earlier run is retained.');
    }
    useSession.setState({ ...defaults, history: state.history, model: input.model, target: input.target,
      reference: input.reference ?? null, referenceNote: input.referenceNote ?? defaults.referenceNote,
      run, starting: false, connection: 'connecting' });
    log(`Pinned v${run.configVersion} · ${input.samples.toLocaleString()} rounds · seed ${input.seed}`);
    return run;
  } catch (error) { useSession.setState({ starting: false, launchNote: '', error: controller.signal.aborted ? '' : String(error) }); throw error; }
  finally { if (launchController === controller) launchController = null; }
}
