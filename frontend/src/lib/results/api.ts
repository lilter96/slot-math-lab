import { parseModelJson } from '../modelJson';
import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { HttpFailure } from '../realtime/RunConnection';
import { retryAfterMilliseconds } from '../realtime/httpFailure';
import { snapshotDecision, validSnapshot, terminal, type RunSnapshot } from '../realtime/runProtocol';
import type { GraphAnalysis } from '../../lib/graphApi';
import type { Reference, RunEvidence, RunPage } from './model';
import { validateRunEvidence } from './evidence';
import { waitForRetry } from '../measurements/requests';

export async function resultsRequest<T>(path: string, init?: RequestInit, signal?: AbortSignal, timeout = 10000): Promise<T> {
  const response = await fetch('/api/' + path, { ...init, signal: AbortSignal.any([...(signal ? [signal] : []), AbortSignal.timeout(timeout)]) });
  const data = response.ok && (path.startsWith('configs/') || /^runs\/[^/]+\/evidence$/.test(path))
    ? parseModelJson(await response.text())
    : await response.json().catch(() => ({}));
  if (!response.ok) {
    if (response.status === 401) window.dispatchEvent(new Event('slotmath:auth-required'));
    throw new HttpFailure(data.error ?? data.title ?? `Request failed (${response.status})`, response.status,
      retryAfterMilliseconds(response.headers.get('Retry-After')));
  }
  return data;
}
/** Reads and explicitly rejected calculations tolerate two bounded quota windows.
 * Other read failures keep one retry; accepted calculations never auto-repeat. */
export const resultsCalculationRetry = (attempt: number, error: Error) => error instanceof HttpFailure
  && error.status === 429 && error.retryAfterMs <= 60000 && attempt < 2;
export const resultsReadRetry = (attempt: number, error: Error) => error instanceof HttpFailure && error.status === 429
  ? resultsCalculationRetry(attempt, error) : !(error instanceof HttpFailure && [400, 401, 403, 404].includes(error.status)) && attempt < 1;
export const resultsRetryDelay = (attempt: number, error: Error) => error instanceof HttpFailure && error.status === 429 ? Math.max(1000, error.retryAfterMs) : 1000 * 2 ** attempt;
const retry = resultsReadRetry, retryDelay = resultsRetryDelay;
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
      return validateRunEvidence(data, id!);
    } });
}
export async function readSimulationEvidence(id: string, signal: AbortSignal, status: (message: string) => void): Promise<RunEvidence> {
  for (let attempt = 0; ; attempt++) {
    signal.throwIfAborted();
    status(attempt ? 'Retrying the rejected evidence request…' : 'Fetching the authoritative run and pinned model…');
    try {
      return validateRunEvidence(await resultsRequest<unknown>(`runs/${encodeURIComponent(id)}/evidence`, undefined, signal), id);
    } catch (error) {
      if (!(error instanceof HttpFailure) || error.status !== 429 || attempt >= 2 || error.retryAfterMs > 60000) throw error;
      const delay = Math.max(1000, error.retryAfterMs);
      status(`Server quota · retrying evidence in ${Math.ceil(delay / 1000)}s. Cancel to stop waiting.`);
      await waitForRetry(delay, signal);
    }
  }
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
    retry: resultsCalculationRetry,
    retryDelay, queryFn: async ({ signal }): Promise<Reference> => {
      if (!evidence?.inputVerified || !evidence.pinnedConfig) throw new Error('Verify the pinned input before calculating a reference.');
      const sourceHash = evidence.run.configHash, calculatedAt = new Date().toISOString();
      const result = await resultsRequest<GraphAnalysis>('evaluate/graph', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ config: evidence.pinnedConfig, mode: 'Exact', maxBranches: 10000, samples: 1 }) }, signal, 16000);
      if (result.configHash !== sourceHash) throw new Error('Reference input fingerprint differs from the pinned run.');
      if (result.provenance === 'Exact' && result.status === 'Complete' && Number.isFinite(result.rtp) && result.method === 'structured')
        return { sourceHash, calculatedAt, kind: result.distribution === 'NotBuilt' ? 'ExactExpectation' : 'ExactDistribution', rtp: result.rtp, rational: result.rationalRtp,
          note: (result.distribution === 'NotBuilt'
            ? 'Exact rational expectation of the pinned payout model, computed from the graph structure without enumerating outcomes. The payout distribution remains sampled.'
            : 'Exact payout distribution of the pinned model, computed from the graph structure using rational arithmetic.') + ' This engine calculation is not an independent external certification.' };
      if (result.provenance === 'Exact' && result.status === 'Complete' && Number.isFinite(result.rtp))
        return { sourceHash, calculatedAt, kind: 'ExactDistribution', rtp: result.rtp, rational: result.rationalRtp,
          note: 'Full enumeration of the pinned payout model using rational arithmetic. This engine calculation is not an independent external certification.' };
      if (result.provenance === 'ExactInterval' && Number.isFinite(result.lower) && Number.isFinite(result.upper))
        return { sourceHash, calculatedAt, kind: 'ExactInterval', lower: result.lower, upper: result.upper,
          note: `Conservative expectation bounds with discarded probability mass ${result.prunedMass ?? 'unknown'}. No exact point estimate is claimed.` };
      return { sourceHash, calculatedAt, kind: 'Unavailable', note: result.note ?? 'The exact calculation exceeded its budget. No verified reference was produced.' };
    } });
}
