import ModelNumberInput from './editor/ModelNumberInput';
import { LOOP } from '../constants';
import { checkDrawRows, drawRowSummary } from '../lib/drawRows';
import { useCallback, useState } from 'react';
import { useAppStore, type DrawWeightEntry } from '../store';
import { Ic } from './Icons';
import ExprEditor from './editor/ExprEditor';
import NativeInspector from './editor/NativeInspector';
import { confirmGraphDelete } from '../lib/confirmGraphDelete';

export default function Inspector() {
  const scope = useAppStore(s => JSON.stringify([s.projectId, s.graphTrail.map(entry => entry.mechanic), s.selectedNodeId]));
  const [rowError, setRowError] = useState<{ scope: string; message: string } | null>(null);
  const selectedNodeId = useAppStore((s) => s.selectedNodeId);
  const nodes = useAppStore((s) => s.nodes);
  const removeNode = useAppStore((s) => s.removeNode);
  const setNodeData = useAppStore((s) => s.setNodeData);

  const node = nodes.find((n) => n.id === selectedNodeId) ?? null;

  const handleDelete = useCallback(() => {
    if (node && confirmGraphDelete(1, 0)) {
      removeNode(node.id);
    }
  }, [node, removeNode]);

  if (!node) {
    return (
      <div className="empty-inspector" style={{ flex: 1 }}>
        <Ic.target style={{ width: 32, height: 32, opacity: 0.3 }} />
        <span>Select a node to inspect</span>
        <span style={{ fontSize: 12, color: 'var(--faint)' }}>
          Use Add nodes to extend the graph
        </span>
      </div>
    );
  }

  const data = node.data;
  if (data.backendNode) return <NativeInspector key={scope} node={node} />;

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

        {data.nodeType === 'sink' && <div className="field">
          <label htmlFor="win-cap">Round win cap</label>
          <ModelNumberInput key={`${scope}:cap`} id="win-cap" integer min={1} value={data.winCap as number ?? 10000}
            onChange={value => setNodeData(node.id, { winCap: value })} />
        </div>}
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
              const summary = drawRowSummary(weights);
              const updateWeights = (next: DrawWeightEntry[]) => {
                checkDrawRows(next);
                setNodeData(node.id, { drawWeights: next }); setRowError(null);
              };
              const rowOperation = (next: DrawWeightEntry[]) => {
                try { updateWeights(next); } catch (error) { setRowError({ scope, message: error instanceof Error ? error.message : 'This table edit has not been applied.' }); }
              };
              return (
                <>
                  <div style={{ overflowX: 'auto' }}>
                    <table style={{ width: '100%', minWidth: 448, tableLayout: 'fixed', fontSize: 12, borderCollapse: 'collapse' }}>
                      <thead>
                        <tr style={{ color: 'var(--faint)' }}>
                          <th style={{ textAlign: 'left', padding: '2px 4px', fontWeight: 400 }}>Outcome</th>
                          <th style={{ textAlign: 'right', padding: '2px 4px', fontWeight: 400, width: 160 }}>Weight</th>
                          <th style={{ textAlign: 'right', padding: '2px 4px', fontWeight: 400, width: 160 }}>Value</th>
                          <th style={{ width: 36 }} />
                        </tr>
                      </thead>
                      <tbody>
                        {weights.map((w, i) => (
                          <tr key={i}>
                            <td style={{ padding: '2px 4px' }}>
                              <input
                                className="inp"
                                aria-label={`Outcome ID ${i + 1}`}
                                style={{ fontSize: 12, padding: '2px 4px', width: '100%' }}
                                value={w.outcomeId}
                                onChange={(e) => {
                                  const next = weights.map((x, j) => j === i ? { ...x, outcomeId: e.target.value } : x);
                                  rowOperation(next);
                                }}
                              />
                            </td>
                            <td style={{ padding: '2px 4px' }}>
                              <ModelNumberInput key={`${scope}:weight:${i}`} label={`Weight for ${w.outcomeId}`} integer min={0}
                                style={{ fontSize: 12, padding: '2px 4px', width: '100%', textAlign: 'right' }} value={w.weight}
                                onChange={value => updateWeights(weights.map((x, j) => j === i ? { ...x, weight: value } : x))} />
                            </td>
                            <td style={{ padding: '2px 4px' }}>
                              <ModelNumberInput key={`${scope}:value:${i}`} label={`Value for ${w.outcomeId}`} integer
                                style={{ fontSize: 12, padding: '2px 4px', width: '100%', textAlign: 'right' }} value={w.value}
                                onChange={value => updateWeights(weights.map((x, j) => j === i ? { ...x, value } : x))} />
                            </td>
                            <td style={{ padding: '2px 2px', textAlign: 'center' }}>
                              <button
                                aria-label={`Remove outcome ${w.outcomeId || i + 1}`}
                                style={{ background: 'none', border: 'none', color: 'var(--danger)', cursor: 'pointer', fontSize: 13, lineHeight: 1, minWidth: 36, minHeight: 36 }}
                                onClick={() => { if (window.confirm(`Remove outcome ${w.outcomeId || i + 1}? This changes the draw distribution.`)) rowOperation(weights.filter((_, j) => j !== i)); }}
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
                    onClick={() => rowOperation([...weights, { outcomeId: `outcome${weights.length + 1}`, weight: 1, value: 0 }])}
                  >
                    + Add outcome
                  </button>
                  {rowError?.scope === scope && <p role="alert">{rowError.message}</p>}
                  {summary && (
                    <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap', overflowWrap: 'anywhere', marginTop: 6, fontSize: 12, color: 'var(--faint)', fontFamily: 'var(--mono)' }}>
                      <span>Static total weight: {summary.total}</span>
                      <span>Static table mean: {summary.mean}</span>
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
            <div className="field">
              <label>State write key <span className="node-lvl" style={{ marginLeft: 4 }}>optional</span></label>
              <input
                className="inp"
                value={(data.stateWriteKey as string) ?? ''}
                placeholder="e.g. outcome (writes drawn outcomeId to state)"
                onChange={(e) => setNodeData(node.id, { stateWriteKey: e.target.value || undefined })}
              />
              <div className="hint">After drawing, writes the chosen outcome ID to this state key. Enables string-based routing via Branch.</div>
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
                <button aria-label="Decrease iteration cap" disabled={(data.iterations as number ?? 5) <= 1} onClick={() => setNodeData(node.id, { iterations: Math.max(1, (data.iterations as number ?? 5) - 1) })}>−</button>
                <ModelNumberInput key={`${scope}:iterations`} label="Iteration cap" integer min={1} max={LOOP.capMax}
                  value={data.iterations as number ?? 5} onChange={value => setNodeData(node.id, { iterations: value })} />
                <button aria-label="Increase iteration cap" disabled={(data.iterations as number ?? 5) >= LOOP.capMax} onClick={() => setNodeData(node.id, { iterations: Math.min(LOOP.capMax, (data.iterations as number ?? 5) + 1) })}>+</button>
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
            <div className="hint" style={{ marginTop: 6 }}>Connect <strong>body</strong> port to loop body (dead-end sub-graph that accumulates to state). Connect <strong>exit</strong> port to continuation after loop.</div>
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
              <div className="hint">Condition routes to <strong>true</strong> or <strong>false</strong> output port. Connect each port to a different sub-graph.</div>
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
            <div className="mono" style={{ fontSize: 12, color: 'var(--faint)' }}>
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
