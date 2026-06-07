import { useCallback } from 'react';
import { useAppStore, type DrawWeightEntry } from '../store';
import { Ic } from './Icons';
import ExprEditor from './editor/ExprEditor';

export default function Inspector() {
  const selectedNodeId = useAppStore((s) => s.selectedNodeId);
  const nodes = useAppStore((s) => s.nodes);
  const removeNode = useAppStore((s) => s.removeNode);
  const setNodeData = useAppStore((s) => s.setNodeData);

  const node = nodes.find((n) => n.id === selectedNodeId) ?? null;

  const handleDelete = useCallback(() => {
    if (node) {
      removeNode(node.id);
    }
  }, [node, removeNode]);

  if (!node) {
    return (
      <div className="empty-inspector" style={{ flex: 1 }}>
        <Ic.target style={{ width: 32, height: 32, opacity: 0.3 }} />
        <span>Select a node to inspect</span>
        <span style={{ fontSize: 11, color: 'var(--faint)' }}>
          Drag from palette to add nodes
        </span>
      </div>
    );
  }

  const data = node.data;

  return (
    <>
      <div className="panel-h">
        <span className="t">{data.label}</span>
        <span className="s">{data.nodeType}</span>
      </div>
      <div className="panel-body">
        {/* ── Common properties ── */}
        <div className="section-label">Properties</div>
        <div className="field">
          <label>Label</label>
          <input
            className="inp"
            value={typeof data.label === 'string' ? data.label : ''}
            onChange={(e) => setNodeData(node.id, { label: e.target.value })}
          />
        </div>

        {/* ── Draw node config ── */}
        {data.nodeType === 'draw' && (
          <>
            <div className="divider" />
            <div className="section-label">Draw Weights</div>
            <div className="hint" style={{ marginBottom: 6 }}>
              Each outcome has a relative weight and a numeric value. Value can be any integer — points, balance change, etc.
            </div>
            {(() => {
              const weights: DrawWeightEntry[] = Array.isArray(data.drawWeights) ? data.drawWeights as DrawWeightEntry[] : [];
              const totalWeight = weights.reduce((s, w) => s + w.weight, 0);
              const expectedValue = totalWeight > 0
                ? weights.reduce((s, w) => s + w.value * w.weight, 0) / totalWeight
                : 0;
              const updateWeights = (next: DrawWeightEntry[]) =>
                setNodeData(node.id, { drawWeights: next });
              return (
                <>
                  <div style={{ overflowX: 'auto' }}>
                    <table style={{ width: '100%', fontSize: 11, borderCollapse: 'collapse' }}>
                      <thead>
                        <tr style={{ color: 'var(--faint)' }}>
                          <th style={{ textAlign: 'left', padding: '2px 4px', fontWeight: 400 }}>Outcome</th>
                          <th style={{ textAlign: 'right', padding: '2px 4px', fontWeight: 400, width: 52 }}>Weight</th>
                          <th style={{ textAlign: 'right', padding: '2px 4px', fontWeight: 400, width: 52 }}>Value</th>
                          <th style={{ width: 20 }} />
                        </tr>
                      </thead>
                      <tbody>
                        {weights.map((w, i) => (
                          <tr key={i}>
                            <td style={{ padding: '2px 4px' }}>
                              <input
                                className="inp"
                                style={{ fontSize: 11, padding: '2px 4px', width: '100%' }}
                                value={w.outcomeId}
                                onChange={(e) => {
                                  const next = weights.map((x, j) => j === i ? { ...x, outcomeId: e.target.value } : x);
                                  updateWeights(next);
                                }}
                              />
                            </td>
                            <td style={{ padding: '2px 4px' }}>
                              <input
                                className="inp"
                                style={{ fontSize: 11, padding: '2px 4px', width: '100%', textAlign: 'right' }}
                                type="number" min={1}
                                value={w.weight}
                                onChange={(e) => {
                                  const next = weights.map((x, j) => j === i ? { ...x, weight: parseInt(e.target.value) || 1 } : x);
                                  updateWeights(next);
                                }}
                              />
                            </td>
                            <td style={{ padding: '2px 4px' }}>
                              <input
                                className="inp"
                                style={{ fontSize: 11, padding: '2px 4px', width: '100%', textAlign: 'right' }}
                                type="number"
                                value={w.value}
                                onChange={(e) => {
                                  const next = weights.map((x, j) => j === i ? { ...x, value: parseInt(e.target.value) || 0 } : x);
                                  updateWeights(next);
                                }}
                              />
                            </td>
                            <td style={{ padding: '2px 2px', textAlign: 'center' }}>
                              <button
                                style={{ background: 'none', border: 'none', color: 'var(--danger)', cursor: 'pointer', fontSize: 13, lineHeight: 1 }}
                                onClick={() => updateWeights(weights.filter((_, j) => j !== i))}
                              >×</button>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                  <button
                    className="btn sm"
                    style={{ marginTop: 6, width: '100%' }}
                    onClick={() => updateWeights([...weights, { outcomeId: `outcome${weights.length + 1}`, weight: 1, value: 0 }])}
                  >
                    + Add outcome
                  </button>
                  {weights.length > 0 && (
                    <div style={{ display: 'flex', gap: 12, marginTop: 6, fontSize: 11, color: 'var(--faint)', fontFamily: 'var(--mono)' }}>
                      <span>Total weight: {totalWeight}</span>
                      <span>E[value]: {expectedValue.toFixed(4)}</span>
                    </div>
                  )}
                </>
              );
            })()}
            <div className="divider" />
            <div className="field">
              <label>Weight expression <span className="node-lvl b" style={{ marginLeft: 4 }}>expr</span></label>
              <ExprEditor
                value={(data.weightExpressionId as string) ?? ''}
                onChange={(v) => setNodeData(node.id, { weightExpressionId: v })}
                placeholder="e.g. if scatter_count >= 3 then [2,8,4,36] else [1,9,4,36]"
              />
              <div className="hint">Overrides per-outcome weights when set (state-dependent draw).</div>
            </div>
          </>
        )}

        {/* ── Loop node config ── */}
        {data.nodeType === 'loop' && (
          <>
            <div className="divider" />
            <div className="section-label">Loop Configuration</div>
            <div className="field">
              <label>Iterations (max)</label>
              <div className="stepper">
                <button onClick={() => setNodeData(node.id, { iterations: Math.max(1, (data.iterations as number ?? 5) - 1) })}>−</button>
                <input
                  className="inp"
                  type="number"
                  min={1}
                  value={data.iterations as number ?? 5}
                  onChange={(e) => setNodeData(node.id, { iterations: parseInt(e.target.value) || 1 })}
                />
                <button onClick={() => setNodeData(node.id, { iterations: (data.iterations as number ?? 5) + 1 })}>+</button>
              </div>
              <div className="hint">Maximum number of loop iterations before forced exit.</div>
            </div>
            <div className="field">
              <label>Termination predicate (level b)</label>
              <ExprEditor
                value={typeof data.terminationExpr === 'string' ? data.terminationExpr : ''}
                onChange={(v) => setNodeData(node.id, { terminationExpr: v })}
                placeholder="e.g. spins_left == 0"
              />
              <div className="hint">Expression that stops the loop when true. Leave empty for fixed-count only.</div>
            </div>
          </>
        )}

        {/* ── Evaluator node config ── */}
        {data.nodeType === 'evaluator' && (
          <>
            <div className="divider" />
            <div className="section-label">Evaluator Config</div>
            <div className="field">
              <label>Kind</label>
              <select
                className="inp"
                value={typeof data.evaluatorKind === 'string' ? data.evaluatorKind : 'lines'}
                onChange={(e) => setNodeData(node.id, { evaluatorKind: e.target.value as 'lines' | 'ways' | 'cluster' | 'scatter' | 'plugin' })}
              >
                <option value="lines">Lines</option>
                <option value="ways">Ways</option>
                <option value="cluster">Cluster (flood-fill)</option>
                <option value="scatter">Scatter (pays anywhere)</option>
                <option value="plugin">Plugin (level c)</option>
              </select>
            </div>
            {(data.evaluatorKind as string) === 'plugin' && (
              <>
                <div className="field">
                  <label>Plugin ID <span className="node-lvl plugin" style={{ marginLeft: 4 }}>plugin</span></label>
                  <input
                    className="inp"
                    value={(data.pluginId as string) ?? ''}
                    placeholder="e.g. megaways-evaluator"
                    onChange={(e) => setNodeData(node.id, { pluginId: e.target.value })}
                  />
                  <div className="hint" style={{ color: 'var(--sampled)' }}>Plugin forces sampled regime — exact RTP unavailable.</div>
                </div>
              </>
            )}
          </>
        )}

        {/* ── State node config ── */}
        {data.nodeType === 'state' && (
          <>
            <div className="divider" />
            <div className="section-label">State Operation <span className="node-lvl" style={{ marginLeft: 6 }}>L0</span></div>
            <div className="field">
              <label>Operation</label>
              <select
                className="inp"
                value={(data.stateOp as string) ?? 'get'}
                onChange={(e) => setNodeData(node.id, { stateOp: e.target.value as 'get' | 'put' | 'modify' })}
              >
                <option value="get">Get State</option>
                <option value="put">Put State</option>
                <option value="modify">Modify State</option>
              </select>
            </div>
            <div className="field">
              <label>State Key</label>
              <input
                className="inp"
                value={(data.stateKey as string) ?? ''}
                placeholder="e.g. spin_count"
                onChange={(e) => setNodeData(node.id, { stateKey: e.target.value })}
              />
              <div className="hint">Key in the shared state dictionary.</div>
            </div>
            {(data.stateOp as string) === 'modify' && (
              <div className="field">
                <label>Modify expression <span className="node-lvl b" style={{ marginLeft: 4 }}>expr</span></label>
                <ExprEditor
                  value={(data.expression as string) ?? ''}
                  onChange={(v) => setNodeData(node.id, { expression: v })}
                  placeholder="e.g. spin_count + 1"
                />
                <div className="hint">Expression evaluated and stored back to the key.</div>
              </div>
            )}
          </>
        )}

        {/* ── Branch node config ── */}
        {data.nodeType === 'branch' && (
          <>
            <div className="divider" />
            <div className="section-label">Condition <span className="node-lvl b" style={{ marginLeft: 6 }}>expr</span></div>
            <div className="field">
              <label>Predicate expression</label>
              <ExprEditor
                value={typeof data.expression === 'string' ? data.expression : ''}
                onChange={(v) => setNodeData(node.id, { expression: v })}
                placeholder="e.g. scatter_count >= 3"
              />
              <div className="hint">Pass through if true, return 0 if false.</div>
            </div>
          </>
        )}

        {/* ── Map node config ── */}
        {data.nodeType === 'map' && (
          <>
            <div className="divider" />
            <div className="section-label">Transform <span className="node-lvl" style={{ marginLeft: 6 }}>L0/b/c</span></div>
            <div className="field">
              <label>Transform / Evaluator ID</label>
              <input
                className="inp"
                value={(data.transformId as string) ?? ''}
                placeholder="e.g. tumble, or plugin:my-eval"
                onChange={(e) => setNodeData(node.id, { transformId: e.target.value })}
              />
              <div className="hint">Registry ID (level a), or <code>plugin:id</code> (level c). Prefix <code>plugin:</code> forces sampled regime.</div>
            </div>
            <div className="field">
              <label>Output expression <span className="node-lvl b" style={{ marginLeft: 4 }}>expr</span></label>
              <ExprEditor
                value={typeof data.expression === 'string' ? data.expression : ''}
                onChange={(v) => setNodeData(node.id, { expression: v })}
                placeholder="e.g. sum(board, multiplier)"
              />
            </div>
          </>
        )}

        <div className="divider" />
        <div className="row">
          <div>
            <div className="section-label">Node ID</div>
            <div className="mono" style={{ fontSize: 10, color: 'var(--faint)' }}>
              {node.id}
            </div>
          </div>
          <button
            className="btn sm"
            style={{ borderColor: 'var(--danger)', color: 'var(--danger)', flex: '0 0 auto' }}
            onClick={handleDelete}
          >
            <Ic.x style={{ width: 12, height: 12 }} />
            Delete
          </button>
        </div>
      </div>
    </>
  );
}
