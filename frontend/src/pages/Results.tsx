import { useMemo } from 'react';
import { useAppStore } from '../store';
import ProvBadge, { type Provenance } from '../components/ProvBadge';
import { useLiveMetrics } from '../hooks/useLiveMetrics';
import { LintPanel, ExplainPanel } from '../components/ai/LintExplainPanel';

export default function Results() {
  const configName = useAppStore((s) => s.configName);
  const symbols = useAppStore((s) => s.tableSymbols);
  const nodes = useAppStore((s) => s.nodes);
  const edges = useAppStore((s) => s.edges);
  const today = new Date().toISOString().slice(0, 10);

  // Live metrics from POST /api/evaluate/light (debounced, falls back gracefully)
  const { overall, loading, error: metricsError } = useLiveMetrics(symbols);

  // Config object for lint endpoint
  const lintConfig = useMemo(() => ({
    schemaVersion: '1.0.0',
    symbols,
    paytables: [],
    reelStrips: [],
    nodes: nodes.map((n) => ({ ...n.data, id: n.id, inputs: {}, outputs: {} })),
    edges: edges.map((e) => ({
      id: e.id,
      sourceNodeId: e.source,
      targetNodeId: e.target,
      sourcePort: e.sourceHandle ?? 'out',
      targetPort: e.targetHandle ?? 'in',
    })),
  }), [symbols, nodes, edges]);

  // Map backend provenance to ProvBadge shape
  const prov: Provenance | null =
    overall?.provenance === 'Exact' ? { kind: 'Exact' }
    : overall?.provenance === 'Sampled' ? { kind: 'Sampled', n: overall.sampleCount, stdErr: overall.stdErr }
    : null;

  const fmtPct = (v?: number, digits = 2) =>
    v != null ? (v * 100).toFixed(digits) + '%' : '—';
  const fmtNum = (v?: number, digits = 2) =>
    v != null ? v.toFixed(digits) : '—';

  const paySymbols = symbols.filter((s) => s.kind === 'Standard' || s.kind === 'pay');

  return (
    <div className="workspace">
      <div className="doc">
        <div className="doc-inner">
          {/* ── Header ── */}
          <div className="par-h">
            <h1>{configName ?? 'Untitled'}</h1>
            <span className="prov exact"><span className="pdot" />PAR sheet</span>
          </div>
          <div className="par-meta">
            {nodes.length} nodes · {edges.length} edges · {symbols.length} symbols · generated {today} · schema v1.0.0
            {loading && <span style={{ marginLeft: 8, color: 'var(--sampled)' }}>· computing…</span>}
          </div>

          {metricsError && (
            <div style={{
              marginBottom: 16, padding: '8px 12px', background: 'var(--epsilon-dim)',
              border: '1px solid var(--epsilon)', borderRadius: 6,
              fontSize: 11, fontFamily: 'var(--mono)', color: 'var(--epsilon)',
            }}>
              {metricsError} — add nodes on the Build tab to compute metrics
            </div>
          )}

          {overall?.provenance === 'NeedsFullRun' && (
            <div style={{
              marginBottom: 16, padding: '8px 12px', background: 'var(--epsilon-dim)',
              border: '1px solid var(--epsilon)', borderRadius: 6,
              fontSize: 11, color: 'var(--epsilon)',
            }}>
              Graph is too expensive for a light evaluation — use the <b>Simulate</b> tab for a full run.
            </div>
          )}

          {/* ── Key metrics grid ── */}
          <div className="par-grid">
            <div className="par-cell">
              <div className="pl">Return to player {prov && <ProvBadge p={prov} mini />}</div>
              <div className="pv" style={{ color: overall ? 'var(--exact)' : undefined }}>
                {fmtPct(overall?.rtp)}
              </div>
            </div>
            <div className="par-cell">
              <div className="pl">Hit frequency {prov && <ProvBadge p={prov} mini />}</div>
              <div className="pv">{fmtPct(overall?.hitFrequency)}</div>
            </div>
            <div className="par-cell">
              <div className="pl">Base volatility {prov && <ProvBadge p={prov} mini />}</div>
              <div className="pv">{fmtNum(overall?.volatility)}<span style={{ fontSize: 13, color: 'var(--muted)' }}>{overall ? ' σ' : ''}</span></div>
            </div>
            <div className="par-cell">
              <div className="pl">95% CI</div>
              <div className="pv" style={{ fontSize: 14 }}>
                {overall?.ci95 ?? '—'}
              </div>
            </div>
          </div>

          {/* ── RTP decomposition ── */}
          <div className="par-section">
            <h2>RTP decomposition</h2>
            <table className="par-table">
              <thead>
                <tr>
                  <th>Component</th>
                  <th>Contribution</th>
                  <th>Share</th>
                  <th>Provenance</th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td>Base game</td>
                  <td style={{ color: 'var(--faint)' }}>—</td>
                  <td style={{ color: 'var(--faint)' }}>—</td>
                  <td>{prov ? <ProvBadge p={prov} mini /> : <span style={{ color: 'var(--faint)' }}>—</span>}</td>
                </tr>
                <tr>
                  <td>Feature contribution</td>
                  <td style={{ color: 'var(--faint)' }}>—</td>
                  <td style={{ color: 'var(--faint)' }}>—</td>
                  <td>{prov ? <ProvBadge p={prov} mini /> : <span style={{ color: 'var(--faint)' }}>—</span>}</td>
                </tr>
                <tr style={{ fontWeight: 600 }}>
                  <td>Total</td>
                  <td style={{ color: overall ? 'var(--exact)' : undefined }}>
                    {fmtPct(overall?.rtp)}
                  </td>
                  <td style={{ color: 'var(--faint)' }}>100.0%</td>
                  <td>{prov ? <ProvBadge p={prov} mini /> : <span style={{ color: 'var(--faint)' }}>—</span>}</td>
                </tr>
              </tbody>
            </table>
            <div className="hint" style={{ marginTop: 10 }}>
              Per-component breakdown requires the exact interpreter. Run the Simulate tab for a full Monte Carlo estimate.
            </div>
          </div>

          {/* ── Paytable ── */}
          {paySymbols.length > 0 && (
            <div className="par-section">
              <h2>Paytable</h2>
              <table className="par-table">
                <thead>
                  <tr>
                    <th>Symbol</th>
                    <th>Kind</th>
                    <th>3-of-a-kind</th>
                    <th>4-of-a-kind</th>
                    <th>5-of-a-kind</th>
                  </tr>
                </thead>
                <tbody>
                  {paySymbols.map((s) => (
                    <tr key={s.id}>
                      <td>
                        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
                          <span style={{ width: 14, height: 14, borderRadius: 4, background: s.color, display: 'inline-block', flexShrink: 0 }} />
                          {s.name}
                        </span>
                      </td>
                      <td style={{ color: 'var(--faint)' }}>{s.kind}</td>
                      <td style={{ color: 'var(--faint)' }}>—</td>
                      <td style={{ color: 'var(--faint)' }}>—</td>
                      <td style={{ color: 'var(--faint)' }}>—</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {/* ── All symbols ── */}
          <div className="par-section">
            <h2>Symbols</h2>
            <table className="par-table">
              <thead>
                <tr>
                  <th>Symbol</th>
                  <th>ID</th>
                  <th>Name</th>
                  <th>Kind</th>
                </tr>
              </thead>
              <tbody>
                {symbols.length === 0 ? (
                  <tr>
                    <td colSpan={4} style={{ color: 'var(--faint)', fontStyle: 'italic' }}>
                      No symbols defined — add them in Build → Tables.
                    </td>
                  </tr>
                ) : symbols.map((s) => (
                  <tr key={s.id}>
                    <td>
                      <span style={{ width: 14, height: 14, borderRadius: 4, background: s.color, display: 'inline-block', verticalAlign: 'middle' }} />
                    </td>
                    <td>{s.id}</td>
                    <td>{s.name}</td>
                    <td>{s.kind}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* ── Graph summary ── */}
          <div className="par-section">
            <h2>Graph summary</h2>
            <div className="par-grid" style={{ gridTemplateColumns: 'repeat(3, 1fr)' }}>
              <div className="par-cell">
                <div className="pl">Nodes</div>
                <div className="pv">{nodes.length}</div>
              </div>
              <div className="par-cell">
                <div className="pl">Edges</div>
                <div className="pv">{edges.length}</div>
              </div>
              <div className="par-cell">
                <div className="pl">Symbols</div>
                <div className="pv">{symbols.length}</div>
              </div>
            </div>
          </div>

          {/* ── Provenance legend ── */}
          <div className="par-section">
            <h2>Provenance</h2>
            <div className="code-block">{`Exact           — rational closed-form (BigInteger numerator / denominator).
ε-pruned        — exact within bounds; prunedMass and [lo, hi] interval reported.
Sampled         — Monte Carlo with n samples; stdErr and 95% CI reported.

All displayed metric values carry a provenance tag.
Floats are display-only; the rational ratio is the source of truth on the exact path.`}</div>
          </div>

          {/* ── AI: explain + lint (G28) ── */}
          {overall && overall.rtp > 0 && (
            <ExplainPanel
              rtp={overall.rtp}
              hitFrequency={overall.hitFrequency ?? 0}
              volatility={overall.volatility ?? 0}
              ci95={overall.ci95 ?? undefined}
            />
          )}
          <LintPanel config={lintConfig} rtp={overall?.rtp} />
        </div>
      </div>
    </div>
  );
}
