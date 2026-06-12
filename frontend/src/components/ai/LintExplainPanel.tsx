import { useState } from 'react';

interface LintIssue {
  ruleId: string;
  severity: 'error' | 'warning' | 'info';
  message: string;
  nodeId?: string;
}

interface LintPanelProps {
  config: object;
  rtp?: number;
}

interface ExplainPanelProps {
  rtp: number;
  hitFrequency: number;
  volatility: number;
  ci95?: string;
}

export function LintPanel({ config, rtp }: LintPanelProps) {
  const [loading, setLoading] = useState(false);
  const [issues, setIssues] = useState<LintIssue[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const runLint = async () => {
    setLoading(true);
    setError(null);
    setIssues(null);
    try {
      const res = await fetch('/api/ai/lint', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ config, rtp }),
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const data = (await res.json()) as { issues: LintIssue[] };
      setIssues(data.issues);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  };

  const severityColor = (s: string) =>
    s === 'error' ? 'var(--epsilon)' : s === 'warning' ? 'var(--sampled)' : 'var(--muted)';

  const severityIcon = (s: string) =>
    s === 'error' ? '✗' : s === 'warning' ? '⚠' : 'ℹ';

  return (
    <div className="par-section">
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 10 }}>
        <h2 style={{ margin: 0 }}>Compliance Lint (G28)</h2>
        <button
          onClick={runLint}
          disabled={loading}
          style={{
            padding: '4px 12px', fontSize: 11, fontWeight: 600,
            background: 'var(--surface-2)', border: '1px solid var(--border)',
            borderRadius: 5, cursor: loading ? 'not-allowed' : 'pointer', color: 'inherit',
          }}
        >
          {loading ? 'Checking…' : 'Run Lint'}
        </button>
      </div>

      {error && (
        <div style={{ fontSize: 11, color: 'var(--epsilon)', padding: '6px 8px', background: 'var(--epsilon-dim)', borderRadius: 6, marginBottom: 8 }}>
          {error}
        </div>
      )}

      {issues === null && !loading && (
        <div style={{ color: 'var(--muted)', fontSize: 12 }}>Click Run Lint to check the current graph.</div>
      )}

      {issues !== null && issues.length === 0 && (
        <div style={{ color: 'var(--exact)', fontSize: 12, display: 'flex', alignItems: 'center', gap: 6 }}>
          <span>✓</span> No issues found.
        </div>
      )}

      {issues !== null && issues.length > 0 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          {issues.map((issue, i) => (
            <div
              key={i}
              style={{
                padding: '8px 10px', borderRadius: 6,
                background: 'var(--surface-2)',
                borderLeft: `3px solid ${severityColor(issue.severity)}`,
                fontSize: 12,
              }}
            >
              <div style={{ display: 'flex', gap: 6, alignItems: 'flex-start' }}>
                <span style={{ color: severityColor(issue.severity), flexShrink: 0, fontWeight: 700 }}>
                  {severityIcon(issue.severity)}
                </span>
                <div>
                  <span style={{ fontWeight: 600, color: severityColor(issue.severity) }}>[{issue.ruleId}]</span>
                  {' '}
                  <span>{issue.message}</span>
                  {issue.nodeId && (
                    <div style={{ color: 'var(--muted)', fontSize: 10, marginTop: 2 }}>node: {issue.nodeId}</div>
                  )}
                </div>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export function ExplainPanel({ rtp, hitFrequency, volatility, ci95 }: ExplainPanelProps) {
  const [loading, setLoading] = useState(false);
  const [explanation, setExplanation] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const explain = async () => {
    setLoading(true);
    setError(null);
    setExplanation(null);
    try {
      const res = await fetch('/api/ai/explain', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ rtp, hitFrequency, volatility, ci95 }),
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const data = (await res.json()) as { explanation: string };
      setExplanation(data.explanation);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  };

  if (rtp == null || rtp === 0) return null;

  return (
    <div className="par-section">
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 10 }}>
        <h2 style={{ margin: 0 }}>AI Explain (G28)</h2>
        <button
          onClick={explain}
          disabled={loading}
          style={{
            padding: '4px 12px', fontSize: 11, fontWeight: 600,
            background: 'var(--surface-2)', border: '1px solid var(--border)',
            borderRadius: 5, cursor: loading ? 'not-allowed' : 'pointer', color: 'inherit',
          }}
        >
          {loading ? 'Explaining…' : 'Explain'}
        </button>
      </div>

      {error && (
        <div style={{ fontSize: 11, color: 'var(--epsilon)', padding: '6px 8px', background: 'var(--epsilon-dim)', borderRadius: 6, marginBottom: 8 }}>
          {error}
        </div>
      )}

      {explanation === null && !loading && (
        <div style={{ color: 'var(--muted)', fontSize: 12 }}>
          Click Explain to get an AI-generated plain-language summary of your game's math.
        </div>
      )}

      {explanation && (
        <div style={{
          fontSize: 13, lineHeight: 1.6, padding: '12px 14px',
          background: 'var(--surface-2)', borderRadius: 8,
          borderLeft: '3px solid var(--exact)',
        }}>
          {explanation}
        </div>
      )}
    </div>
  );
}
