import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { HttpFailure } from '../realtime/RunConnection';
import { snapshotDecision, validSnapshot, terminal, type RunSnapshot } from '../realtime/runProtocol';
import { createExpectationGraph } from '../../games/doghouse/expectationGraph';
import type { GraphAnalysis, GraphRound } from '../../games/doghouse/api';
import type { Reference, RunEvidence, RunPage } from './model';

export async function resultsRequest<T>(path: string, init?: RequestInit, signal?: AbortSignal, timeout = 10000): Promise<T> {
  const response = await fetch('/api/' + path, { ...init, signal: AbortSignal.any([...(signal ? [signal] : []), AbortSignal.timeout(timeout)]) });
  const data = await response.json().catch(() => ({}));
  if (!response.ok) {
    if (response.status === 401) window.dispatchEvent(new Event('slotmath:auth-required'));
    const header = response.headers.get('Retry-After');
    const retry = header ? /^\d+$/.test(header) ? Number(header) * 1000 : Date.parse(header) - Date.now() : 0;
    throw new HttpFailure(data.error ?? data.title ?? `Request failed (${response.status})`, response.status, Number.isFinite(retry) ? retry : 0);
  }
  return data;
}
const retry = (attempt: number, error: Error) => !(error instanceof HttpFailure && [400, 401, 403, 404].includes(error.status)) && attempt < 1;
const retryDelay = (attempt: number, error: Error) => error instanceof HttpFailure && error.status === 429 ? Math.max(1000, error.retryAfterMs) : 1000 * 2 ** attempt;
export function useRunArchive(search: string, status: string) {
  return useInfiniteQuery({ queryKey: ['run-archive', search, status], initialPageParam: null as string | null,
    queryFn: ({ pageParam, signal }) => {
      const query = new URLSearchParams({ limit: '20', status, search }); if (pageParam) query.set('cursor', pageParam);
      return resultsRequest<RunPage>('runs?' + query, undefined, signal);
    }, getNextPageParam: page => page.nextCursor, staleTime: 15000, retry, retryDelay,
    refetchInterval: query => !query.state.error && (query.state.data?.pages[0]?.active ?? 0) > 0 ? 5000 : false });
}
export function useRunEvidence(id: string | undefined) {
  return useQuery({ queryKey: ['run-evidence', id], enabled: !!id, staleTime: Infinity, retry, retryDelay,
    queryFn: async ({ signal }) => {
      const data = await resultsRequest<RunEvidence>(`runs/${encodeURIComponent(id!)}/evidence`, undefined, signal);
      if (!validSnapshot(data.run) || !data.model || typeof data.model.name !== 'string') throw new Error('The server returned an invalid evidence record.');
      if (data.inputVerified && (!data.pinnedConfig || data.computedConfigHash !== data.run.configHash || !data.model.modelHash)) throw new Error('Pinned input verification is inconsistent.');
      return data;
    } });
}
export function useResultSnapshot(id: string | undefined, monitored: RunSnapshot | null) {
  return useQuery({ queryKey: ['result-snapshot', id], enabled: !!id && monitored?.id !== id, staleTime: 1000, retry, retryDelay,
    queryFn: async ({ signal }) => {
      const data = await resultsRequest<RunSnapshot>(`runs/${encodeURIComponent(id!)}`, undefined, signal);
      if (!validSnapshot(data)) throw new Error('The server returned an invalid run snapshot.'); return data;
    }, structuralSharing: (old, incoming) => {
      if (old && ['stale', 'invalid', 'wrong-run', 'epoch-change'].includes(snapshotDecision(old as RunSnapshot, (old as RunSnapshot).progress ?? null, incoming))) return old;
      return incoming;
    }, refetchInterval: query => terminal(query.state.data?.status) ? false : 5000 });
}
export function usePinnedReference(evidence: RunEvidence | undefined) {
  return useQuery({ queryKey: ['pinned-reference', evidence?.run.configHash], enabled: false, staleTime: Infinity,
    // A 429 was rejected before evaluation. Only that rejection is retried;
    // failed or timed-out accepted calculations are not repeated automatically.
    retry: (attempt, error) => error instanceof HttpFailure && error.status === 429 && attempt < 1,
    retryDelay, queryFn: async ({ signal }): Promise<Reference> => {
      if (!evidence?.inputVerified || !evidence.pinnedConfig) throw new Error('Verify the pinned input before calculating a reference.');
      const sourceHash = evidence.run.configHash, calculatedAt = new Date().toISOString();
      let proof: Record<string, unknown> | null = null;
      try { proof = createExpectationGraph(evidence.pinnedConfig); } catch { /* Other models use generic full enumeration. */ }
      if (proof) {
        const result = await resultsRequest<GraphRound>('play/round', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ config: proof, seed: 42, roundIndex: 0 }) }, signal, 16000);
        const value = result.state.expectedRtp as { numerator: string; denominator: string; displayValue: number };
        if (!value || !Number.isFinite(value.displayValue)) throw new Error('The authored expectation proof did not return a valid expectedRtp.');
        const components = ['baseRtp', 'scatterRtp', 'bonusRtp'].map((key, index) => {
          const v = result.state[key] as typeof value;
          if (!v || !Number.isFinite(v.displayValue) || v.displayValue < 0) throw new Error('The proof returned invalid contribution metrics.');
          return { name: ['Base paylines', 'Scatter awards', 'Free spins'][index], rtp: v.displayValue, rational: `${v.numerator}/${v.denominator}` };
        });
        if (Math.abs(components.reduce((sum, item) => sum + item.rtp, 0) - value.displayValue) > 1e-12) throw new Error('The proof contributions do not sum to the expectation.');
        return { sourceHash, calculatedAt, kind: 'ExactExpectation', rtp: value.displayValue, rational: `${value.numerator}/${value.denominator}`, components, proof,
          note: 'Rational expectation from the editable constructor proof under its checked assumptions. The payout distribution remains sampled; an independent external oracle is not attached.' };
      }
      const result = await resultsRequest<GraphAnalysis>('evaluate/graph', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ config: evidence.pinnedConfig, mode: 'Exact', maxBranches: 10000, samples: 1 }) }, signal, 16000);
      if (result.configHash !== sourceHash) throw new Error('Reference input fingerprint differs from the pinned run.');
      if (result.provenance === 'Exact' && result.status === 'Complete' && Number.isFinite(result.rtp))
        return { sourceHash, calculatedAt, kind: 'ExactDistribution', rtp: result.rtp, rational: result.rationalRtp,
          note: 'Full enumeration of the pinned payout model using rational arithmetic. This engine calculation is not an independent external certification.' };
      if (result.provenance === 'ExactInterval' && Number.isFinite(result.lower) && Number.isFinite(result.upper))
        return { sourceHash, calculatedAt, kind: 'ExactInterval', lower: result.lower, upper: result.upper,
          note: `Conservative expectation bounds with discarded probability mass ${result.prunedMass ?? 'unknown'}. No exact point estimate is claimed.` };
      return { sourceHash, calculatedAt, kind: 'Unavailable', note: result.note ?? 'The exact calculation exceeded its budget. No verified reference was produced.' };
    } });
}
