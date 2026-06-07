import { useAppStore } from '../store';
import ProvBadge from '../components/ProvBadge';

export default function Results() {
  const configName = useAppStore((s) => s.configName);
  const symbols = useAppStore((s) => s.tableSymbols);
  const nodes = useAppStore((s) => s.nodes);
  const edges = useAppStore((s) => s.edges);
  const today = new Date().toISOString().slice(0, 10);

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
          </div>

          {/* ── Key metrics grid ── */}
          <div className="par-grid">
            <div className="par-cell">
              <div className="pl">Return to player <ProvBadge p={{ kind: 'Exact' }} mini /></div>
              <div className="pv" style={{ color: 'var(--exact)' }}>—</div>
            </div>
            <div className="par-cell">
              <div className="pl">Hit frequency <ProvBadge p={{ kind: 'Exact' }} mini /></div>
              <div className="pv">—</div>
            </div>
            <div className="par-cell">
              <div className="pl">Base volatility <ProvBadge p={{ kind: 'Exact' }} mini /></div>
              <div className="pv">—</div>
            </div>
            <div className="par-cell">
              <div className="pl">Feature trigger <ProvBadge p={{ kind: 'Exact' }} mini /></div>
              <div className="pv">—</div>
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
                  <td><ProvBadge p={{ kind: 'Exact' }} mini /></td>
                </tr>
                <tr>
                  <td>Feature contribution</td>
                  <td style={{ color: 'var(--faint)' }}>—</td>
                  <td style={{ color: 'var(--faint)' }}>—</td>
                  <td><ProvBadge p={{ kind: 'Exact' }} mini /></td>
                </tr>
                <tr style={{ fontWeight: 600 }}>
                  <td>Total</td>
                  <td style={{ color: 'var(--exact)' }}>—</td>
                  <td>—</td>
                  <td><ProvBadge p={{ kind: 'Exact' }} mini /></td>
                </tr>
              </tbody>
            </table>
            <div className="hint" style={{ marginTop: 10 }}>
              Run the exact interpreter or a simulation to compute RTP contributions.
              Use the <b>Simulate</b> tab for Monte Carlo estimates.
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
                  <tr><td colSpan={4} style={{ color: 'var(--faint)', fontStyle: 'italic' }}>No symbols defined yet — add them in Build → Tables.</td></tr>
                ) : symbols.map((s) => (
                  <tr key={s.id}>
                    <td><span style={{ width: 14, height: 14, borderRadius: 4, background: s.color, display: 'inline-block', verticalAlign: 'middle' }} /></td>
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
            <div className="par-grid" style={{ gridTemplateColumns: 'repeat(3,1fr)' }}>
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

          {/* ── Provenance note ── */}
          <div className="par-section">
            <h2>Provenance</h2>
            <div className="code-block">{`Exact path  — rational closed-form (BigInteger numerator / denominator).
ε-pruned    — exact within bounds; prunedMass and [lo, hi] interval reported.
Sampled     — Monte Carlo with n samples; stdErr and 95% CI reported.

All displayed metric values carry a provenance tag.
Floats are display-only; the rational ratio is the source of truth on the exact path.`}</div>
          </div>
        </div>
      </div>
    </div>
  );
}
