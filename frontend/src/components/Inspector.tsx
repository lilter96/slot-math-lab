import { useCallback } from 'react';
import { useAppStore } from '../store';
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
      <div className="panel" style={{ width: 320, flexShrink: 0 }}>
        <div className="empty-inspector">
          <Ic.target style={{ width: 32, height: 32, opacity: 0.3 }} />
          <span>Select a node to inspect</span>
          <span style={{ fontSize: 11, color: 'var(--faint)' }}>
            Drag from palette to add nodes
          </span>
        </div>
      </div>
    );
  }

  const data = node.data;

  return (
    <div className="panel" style={{ width: 320, flexShrink: 0 }}>
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
                onChange={(e) => setNodeData(node.id, { evaluatorKind: e.target.value as 'lines' | 'ways' | 'cluster' | 'scatter' })}
              >
                <option value="lines">Lines</option>
                <option value="ways">Ways</option>
                <option value="cluster">Cluster (flood-fill)</option>
                <option value="scatter">Scatter (pays anywhere)</option>
              </select>
            </div>
          </>
        )}

        {/* ── Expression config ── */}
        {(data.nodeType === 'branch' || data.nodeType === 'map') && (
          <>
            <div className="divider" />
            <div className="section-label">Expression (level b)</div>
            <div className="field">
              <label>{data.nodeType === 'branch' ? 'Predicate' : 'Transform expression'}</label>
              <ExprEditor
                value={typeof data.expression === 'string' ? data.expression : ''}
                onChange={(v) => setNodeData(node.id, { expression: v })}
                placeholder={data.nodeType === 'branch' ? 'e.g. scatter_count >= 3' : 'e.g. sum(board, multiplier)'}
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
    </div>
  );
}
