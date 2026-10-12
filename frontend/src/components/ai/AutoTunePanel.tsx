import { useState, useEffect, useRef } from 'react';
import { useAppStore } from '../../store';
import { useModelSnapshot } from '../../hooks/useModelProjection';
import { parseModelJson, parseModelNumber } from '../../lib/modelJson';
import { generatedModel } from '../../lib/generatedModel';
import { projectContentScope, switchProject } from '../../lib/projectFiles';

interface AutoTuneResult {
  targetRtp: number;
  achievedRtp: number;
  iterations: number;
  elapsedMs: number;
  bestConfig: Record<string, unknown>;
  scope: string;
}

export default function AutoTunePanel() {
  const snapshot = useModelSnapshot();
  useAppStore(s => s.projectId);
  const pending = useRef<{ controller: AbortController; scope: string } | null>(null);
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    const unsubscribe = useAppStore.subscribe(() => { const task = pending.current; if (task && task.scope !== projectContentScope()) task.controller.abort(); });
    return () => { mounted.current = false; pending.current?.controller.abort(); pending.current = null; unsubscribe(); };
  }, []);

  const [targetRtp, setTargetRtp] = useState('0.96');
  const [maxIter, setMaxIter] = useState('200');
  const [loading, setLoading] = useState(false);
  const [storedResult, setResult] = useState<AutoTuneResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const result = storedResult?.scope === projectContentScope() ? storedResult : null;
  const tune = async () => {
    if (pending.current) return;
    const task = { controller: new AbortController(), scope: projectContentScope() }; pending.current = task;
    const current = () => mounted.current && pending.current === task && !task.controller.signal.aborted && task.scope === projectContentScope();
    setLoading(true);
    setError(null);
    setResult(null);
    try {
      const config = snapshot.config;
      if (!config) throw new Error(snapshot.error ?? 'Build a graph first.');
      const targetValue = parseModelNumber(targetRtp), iterations = parseModelNumber(maxIter);
      if (targetValue < 0 || !Number.isInteger(iterations) || iterations < 1 || iterations > 500) throw new Error('Use a non-negative target RTP and 1–500 whole iterations.');

      const res = await fetch('/api/ai/auto-tune', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          config,
          targetRtp: targetValue,
          maxIterations: iterations,
        }),
        signal: task.controller.signal,
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error((body as { error?: string }).error ?? `HTTP ${res.status}`);
      }

      const data = parseModelJson(await res.text());
      if (!current()) return;
      if (typeof data.achievedRtp !== 'number' || !Number.isFinite(data.achievedRtp) || !Number.isSafeInteger(data.iterations) || Number(data.iterations) < 0 || typeof data.elapsedMs !== 'number' || !Number.isFinite(data.elapsedMs) || data.elapsedMs < 0) throw new Error('Tuning response is missing a valid candidate report.');
      const bestConfig = generatedModel(data.bestConfig);
      setResult({ targetRtp: targetValue, achievedRtp: data.achievedRtp, iterations: Number(data.iterations), elapsedMs: data.elapsedMs, bestConfig, scope: task.scope });
    } catch (e) {
      if (mounted.current && pending.current === task) setError(task.controller.signal.aborted ? 'The source changed while tuning. Tune again for the current model.' : e instanceof Error ? e.message : String(e));
    } finally {
      if (mounted.current) setLoading(false);
      if (pending.current === task) pending.current = null;
    }
  };

  const target = result?.targetRtp ?? 0;
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
            min="0"
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
            min="1"
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
          disabled={loading || !snapshot.config}
          style={{
            marginTop: 4, padding: '7px 14px', fontSize: 12, fontWeight: 600,
            background: loading ? 'var(--surface-2)' : 'var(--accent)', color: '#fff',
            border: 'none', borderRadius: 6, cursor: loading ? 'not-allowed' : 'pointer',
          }}
        >
          {loading ? 'Tuning…' : 'Tune'}
        </button>

        {error && (
          <div role="alert" style={{ fontSize: 12, color: 'var(--epsilon)', padding: '6px 8px', background: 'var(--epsilon-dim)', borderRadius: 6 }}>
            {error}
          </div>
        )}

        {result && (
          <div style={{ marginTop: 4, padding: '8px 10px', background: 'var(--surface-2)', borderRadius: 8, fontSize: 12 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6 }}>
              <span style={{ color: 'var(--muted)' }}>Reported candidate RTP</span>
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
              <p className="hint">Sampling details are not included in this response. The current draft is unchanged.</p><button className="btn" onClick={() => switchProject(result.bestConfig)}>Open tuned candidate as a new project</button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
