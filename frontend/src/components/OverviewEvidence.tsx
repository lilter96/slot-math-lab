import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { apiClient } from '../api/client';
import { useAppStore } from '../store';
import { browserStorageKey } from '../lib/browserIdentity';
import { canonicalJson } from '../lib/hash';
import { verifiedOverviewRun } from '../lib/overviewEvidence';
import { percent } from '../lib/results/model';
import { resultsReadRetry, resultsRetryDelay } from '../lib/results/api';
import { HttpFailure, retryAfterMilliseconds } from '../lib/realtime/httpFailure';
import ProvBadge from './ProvBadge';

export default function OverviewEvidence({ graph }: { graph: Record<string, unknown> }) {
  const projectId = useAppStore(state => state.projectId);
  const query = useQuery({
    queryKey: [browserStorageKey('overview-evidence'), projectId, canonicalJson(graph)],
    queryFn: async ({ signal }) => {
      const { data, response } = await apiClient.POST('/api/runs/latest-model', {
        body: { config: graph }, signal: AbortSignal.any([signal, AbortSignal.timeout(10000)]),
      });
      if (!response.ok) {
        if (response.status === 401) window.dispatchEvent(new Event('slotmath:auth-required'));
        throw new HttpFailure(`Saved results unavailable (HTTP ${response.status}).`, response.status,
          retryAfterMilliseconds(response.headers.get('Retry-After')));
      }
      return verifiedOverviewRun(data);
    },
    retry: resultsReadRetry,
    retryDelay: resultsRetryDelay,
    staleTime: 0,
  });
  const checking = query.isPending || query.isFetching;
  const result = !checking && !query.isError ? query.data : null;
  return <section className="overview-evidence" aria-labelledby="overview-evidence-heading">
    <div>
      <h2 id="overview-evidence-heading">Analysis evidence</h2>
      {checking ? <p role="status">Checking saved results for this model…</p> : query.isError ? <p role="alert">{query.error.message} <button className="btn" onClick={() => void query.refetch()}>Retry saved evidence</button></p> : result ? <>
        <h3>Last verified saved run</h3>
        <p>Matching pinned model · completed Monte Carlo calculation.</p>
        <dl className="overview-run-metrics">
          <div><dt>Return to player</dt><dd>{percent(result.progress.runningRtp)} <ProvBadge p={{ kind: 'Sampled', n: result.progress.sampleCount, stdErr: result.rtpInterval ? result.progress.stdErr : undefined }} /></dd></div>
          <div><dt>Paid rounds</dt><dd>{result.progress.sampleCount.toLocaleString()}</dd></div>
          <div><dt>Seed</dt><dd>{result.evidence.run.seed}</dd></div>
          <div><dt>Completed</dt><dd><time dateTime={result.evidence.run.completedAt!}>{new Date(result.evidence.run.completedAt!).toLocaleString()}</time></dd></div>
        </dl>
        <p>{result.rtpInterval ? `Approximate 95% RTP interval: ${percent(result.rtpInterval[0])} to ${percent(result.rtpInterval[1])}.` : 'Round uncertainty interval unavailable for this sample or execution regime.'}</p>
        <p className="overview-run-source">Run <code>{result.evidence.run.id}</code> · Input v{result.evidence.run.configVersion} · <code>{result.evidence.run.configHash}</code></p>
        <p>This verifies the saved input and observations; sampling does not certify the model.</p>
      </> : <p>No verified completed result for this model yet. Run analysis to create one.</p>}
    </div>
    <div className="overview-evidence-actions">
      {result && <Link className="btn" to={`/results?run=${encodeURIComponent(result.evidence.run.id)}`}>Inspect saved run</Link>}
      {!result && !checking && !query.isError && <Link className="btn primary" to="/simulate">Run analysis</Link>}
      <Link className="btn" to="/results">Review results</Link>
    </div>
  </section>;
}
