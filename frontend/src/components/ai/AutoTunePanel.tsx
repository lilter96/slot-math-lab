import { useState } from 'react';
import { useAppStore } from '../../store';
import ProvBadge from '../ProvBadge';

interface AutoTuneResult {
  achievedRtp: number;
  iterations: number;
  elapsedMs: number;
}

export default function AutoTunePanel() {
  const nodes = useAppStore((s) => s.nodes);
  const edges = useAppStore((s) => s.edges);
  const symbols = useAppStore((s) => s.tableSymbols);

  const [targetRtp, setTargetRtp] = useState('0.96');
  const [maxIter, setMaxIter] = useState('200');
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<AutoTuneResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const tune = async () => {
    if (nodes.length === 0) {
      setError('Build a graph first.');
      return;
    }
    setLoading(true);
    setError(null);
    setResult(null);
    try {
      const config = {
        schemaVersion: '1.0.0',
        symbols,
        paytables: [],
        reelStrips: [],
        nodes: nodes.map((n) => ({
          ...n.data,
          id: n.id,
          inputs: {},
          outputs: {},
        })),
        edges: edges.map((e) => ({
          id: e.id,
          sourceNodeId: e.source,
          targetNodeId: e.target,
          sourcePort: e.sourceHandle ?? 'out',
          targetPort: e.targetHandle ?? 'in',
        })),
      };

      const res = await fetch('/api/ai/auto-tune', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          config,
          targetRtp: parseFloat(targetRtp) || 0.96,
          maxIterations: parseInt(maxIter) || 200,
        }),
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error((body as { error?: string }).error ?? `HTTP ${res.status}`);
      }

      const data = (await res.json()) as AutoTuneResult & { bestConfig?: unknown };
      setResult({ achievedRtp: data.achievedRtp, iterations: data.iterations, elapsedMs: data.elapsedMs });
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  };

  const target = parseFloat(targetRtp);
  const diff = result ? Math.abs(result.achievedRtp - target) : null;

  return (
    <div style={{ padding: '12px 14px' }}>
      <div style={{ fontWeight: 600, fontSize: 12, marginBottom: 10, letterSpacing: '0.04em', textTransform: 'uppercase', color: 'var(--muted)' }}>
        Auto-Tune (G27)
      </div>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        <label style={{ fontSize: 12 }}>
          <span style={{ color: 'var(--muted)' }}>Target RTP</span>
          <input
            type="number"
            min="0.5"
            max="1.0"
            step="0.01"
            value={targetRtp}
            onChange={(e) => setTargetRtp(e.target.value)}
            style={{
              display: 'block', width: '100%', marginTop: 4,
              background: 'var(--surface)', border: '1px solid var(--border)',
              borderRadius: 6, padding: '5px 8px', color: 'inherit', fontSize: 12,
            }}
          />
        </label>
        <label style={{ fontSize: 12 }}>
          <span style={{ color: 'var(--muted)' }}>Max iterations</span>
          <input
            type="number"
            min="10"
            max="500"
            step="10"
            value={maxIter}
            onChange={(e) => setMaxIter(e.target.value)}
            style={{
              display: 'block', width: '100%', marginTop: 4,
              background: 'var(--surface)', border: '1px solid var(--border)',
              borderRadius: 6, padding: '5px 8px', color: 'inherit', fontSize: 12,
            }}
          />
        </label>

        <button
          onClick={tune}
          disabled={loading}
          style={{
            marginTop: 4, padding: '7px 14px', fontSize: 12, fontWeight: 600,
            background: loading ? 'var(--surface-2)' : 'var(--accent)', color: '#fff',
            border: 'none', borderRadius: 6, cursor: loading ? 'not-allowed' : 'pointer',
          }}
        >
          {loading ? 'Tuning…' : 'Tune'}
        </button>

        {error && (
          <div style={{ fontSize: 11, color: 'var(--epsilon)', padding: '6px 8px', background: 'var(--epsilon-dim)', borderRadius: 6 }}>
            {error}
          </div>
        )}

        {result && (
          <div style={{ marginTop: 4, padding: '8px 10px', background: 'var(--surface-2)', borderRadius: 8, fontSize: 12 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6 }}>
              <span style={{ color: 'var(--muted)' }}>Achieved RTP</span>
              <span style={{ fontWeight: 700, color: diff != null && diff < 0.02 ? 'var(--exact)' : 'var(--epsilon)' }}>
                {(result.achievedRtp * 100).toFixed(2)}%
              </span>
            </div>
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 4 }}>
              <span style={{ color: 'var(--muted)' }}>Target</span>
              <span>{(target * 100).toFixed(2)}%</span>
            </div>
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 4 }}>
              <span style={{ color: 'var(--muted)' }}>Δ from target</span>
              <span style={{ color: diff != null && diff < 0.01 ? 'var(--exact)' : 'var(--sampled)' }}>
                {diff != null ? (diff * 100).toFixed(3) + 'pp' : '—'}
              </span>
            </div>
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 4 }}>
              <span style={{ color: 'var(--muted)' }}>Iterations</span>
              <span>{result.iterations}</span>
            </div>
            <div style={{ display: 'flex', justifyContent: 'space-between' }}>
              <span style={{ color: 'var(--muted)' }}>Elapsed</span>
              <span>{result.elapsedMs.toFixed(0)} ms</span>
            </div>
            <div style={{ marginTop: 8 }}>
              <ProvBadge p={{ kind: 'Sampled', n: 5000, stdErr: 0.001 }} mini />
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
