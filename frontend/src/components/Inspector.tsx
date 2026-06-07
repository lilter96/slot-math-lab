import { Ic } from './Icons';

interface InspectorProps {
  node?: { id: string; label?: string; type?: string } | null;
}

export default function Inspector({ node }: InspectorProps) {
  if (!node) {
    return (
      <div className="panel" style={{ width: 320, flexShrink: 0 }}>
        <div className="empty-inspector">
          <Ic.target style={{ width: 32, height: 32, opacity: 0.3 }} />
          <span>Select a node to inspect</span>
        </div>
      </div>
    );
  }

  return (
    <div className="panel" style={{ width: 320, flexShrink: 0 }}>
      <div className="panel-h">
        <span className="t">{node.label ?? node.id}</span>
        {node.type && <span className="s">{node.type}</span>}
      </div>
      <div className="panel-body">
        <div className="section-label">Properties</div>
        <div className="field">
          <label>Label</label>
          <input className="inp" defaultValue={node.label ?? node.id} />
        </div>
        <div className="hint">Full inspector UI lands in G19.</div>
      </div>
    </div>
  );
}
