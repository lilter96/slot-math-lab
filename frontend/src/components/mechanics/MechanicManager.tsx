import { useState, useCallback } from 'react';
import { useAppStore, type CustomMechanic, type GraphNode, type GraphEdge } from '../../store';
import { useSaveConfig } from '../../api/hooks';
import { Ic } from '../Icons';

export default function MechanicManager() {
  const mechanics = useAppStore((s) => s.mechanics);
  const nodes = useAppStore((s) => s.nodes);
  const edges = useAppStore((s) => s.edges);
  const backendAvailable = useAppStore((s) => s.backendAvailable);
  const saveConfig = useSaveConfig();
  const [configId] = useState<string | undefined>();
  const selectedNodeId = useAppStore((s) => s.selectedNodeId);
  const addMechanic = useAppStore((s) => s.addMechanic);
  const removeMechanic = useAppStore((s) => s.removeMechanic);
  const addNode = useAppStore((s) => s.addNode);
  const [name, setName] = useState('');
  const [desc, setDesc] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [savedMsg, setSavedMsg] = useState<string | null>(null);

  const handleSave = useCallback(() => {
    if (!name.trim()) {
      setError('Mechanic name is required');
      return;
    }
    if (!nodes.length) {
      setError('Add nodes to the canvas first');
      return;
    }
    const id = name.toLowerCase().replace(/\s+/g, '-').replace(/[^a-z0-9-]/g, '');

    // Collect the selected node and its connected subgraph
    let subNodes: GraphNode[];
    let subEdges: GraphEdge[];
    if (selectedNodeId) {
      const connectedNodes = new Set<string>([selectedNodeId]);
      // BFS to find connected subgraph
      const queue = [selectedNodeId];
      while (queue.length) {
        const current = queue.shift()!;
        for (const e of edges) {
          if (e.source === current && !connectedNodes.has(e.target)) {
            connectedNodes.add(e.target);
            queue.push(e.target);
          }
          if (e.target === current && !connectedNodes.has(e.source)) {
            connectedNodes.add(e.source);
            queue.push(e.source);
          }
        }
      }
      subNodes = nodes.filter((n) => connectedNodes.has(n.id));
      subEdges = edges.filter((e) => connectedNodes.has(e.source) && connectedNodes.has(e.target));
    } else {
      subNodes = [...nodes];
      subEdges = [...edges];
    }

    addMechanic({
      id,
      name: name.trim(),
      description: desc.trim() || undefined,
      nodes: subNodes.map((n) => ({ ...n, position: { x: 100 + Math.random() * 300, y: 100 + Math.random() * 200 } })),
      edges: subEdges.map((e) => ({ ...e })),
      createdAt: new Date().toISOString(),
    });
    // Save to backend config API (async, fire-and-forget)
    saveConfig.mutate({
      id: configId,
      name: name.trim(),
      mechanics: [...mechanics, { id, name: name.trim(), description: desc.trim() || undefined, nodes: subNodes, edges: subEdges, createdAt: new Date().toISOString() }],
    });
    setName('');
    setDesc('');
    setError(null);
    setSavedMsg(`Mechanic "${name.trim()}" saved! Drag it from the palette to reuse.`);
    setTimeout(() => setSavedMsg(null), 3000);
  }, [name, desc, nodes, edges, selectedNodeId, addMechanic, configId, mechanics, saveConfig]);

  const handleAddToCanvas = useCallback((mechanic: CustomMechanic) => {
    const offset = 50 + Math.random() * 100;
    const idMap = new Map<string, string>();
    const newNodes = mechanic.nodes.map((n) => {
      const newId = `${n.id}-${Date.now()}-${Math.random().toString(36).slice(2)}`;
      idMap.set(n.id, newId);
      return { ...n, id: newId, position: { x: n.position.x + offset, y: n.position.y + offset } };
    });
    const newEdges: GraphEdge[] = mechanic.edges.map((e) => ({
      ...e,
      id: `e-${Date.now()}-${Math.random().toString(36).slice(2)}`,
      source: idMap.get(e.source) ?? e.source,
      target: idMap.get(e.target) ?? e.target,
    }));
    const { onConnect } = useAppStore.getState();
    for (const node of newNodes) addNode(node);
    for (const edge of newEdges) {
      onConnect({
        source: edge.source,
        target: edge.target,
        sourceHandle: edge.sourceHandle ?? null,
        targetHandle: edge.targetHandle ?? null,
      });
    }
    setSavedMsg(`Added "${mechanic.name}" to canvas.`);
    setTimeout(() => setSavedMsg(null), 3000);
  }, [addNode]);

  return (
    <div>
      <div className="panel-h">
        <span className="t">Custom Mechanics</span>
        <span className="s">
          {backendAvailable ? (
            <span style={{ color: 'var(--exact)' }}>● api</span>
          ) : (
            <span style={{ color: 'var(--faint)' }}>○ offline</span>
          )}
          {' · '}{mechanics.length} saved
        </span>
      </div>
      <div className="panel-body">
        {/* ── Save current subgraph ── */}
        <div className="section-label">Save as Custom Mechanic</div>
        <div className="field">
          <label>Name</label>
          <input
            className="inp"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Hold & Win, Free Spins Cascade"
          />
        </div>
        <div className="field">
          <label>Description (optional)</label>
          <input
            className="inp"
            value={desc}
            onChange={(e) => setDesc(e.target.value)}
            placeholder="What this mechanic does"
          />
        </div>
        <div className="hint" style={{ marginBottom: 8 }}>
          {selectedNodeId
            ? `Saving subgraph connected to "${nodes.find((n) => n.id === selectedNodeId)?.data?.label || selectedNodeId}"`
            : 'Select a node to save its subgraph, or save all nodes'}
        </div>
        {error && <div style={{ color: 'var(--danger)', fontSize: 11, marginBottom: 8 }}>{error}</div>}
        {savedMsg && <div style={{ color: 'var(--exact)', fontSize: 11, marginBottom: 8, fontFamily: 'var(--mono)' }}>{savedMsg}</div>}
        <button className="btn primary sm" onClick={handleSave} disabled={!name.trim() || !nodes.length}>
          <Ic.check style={{ width: 12, height: 12 }} /> Save Mechanic
        </button>

        <div className="divider" />

        {/* ── Saved mechanics ── */}
        <div className="section-label">Saved Mechanics</div>
        {mechanics.length === 0 ? (
          <div className="hint">No mechanics saved yet. Build a subgraph and save it.</div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
            {mechanics.map((m) => (
              <div key={m.id} className="pal-item" style={{ margin: 0, alignItems: 'flex-start', flexDirection: 'column', gap: 2, padding: '8px 12px' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, width: '100%' }}>
                  <div className="pic" style={{ color: 'var(--n-loop)', background: 'oklch(0.78 0.15 305 / 0.16)' }}>
                    <Ic.loop />
                  </div>
                  <div style={{ flex: 1 }}>
                    <div className="pt">{m.name}</div>
                    <div className="ps">{m.nodes.length} nodes · {m.edges.length} edges</div>
                  </div>
                  <button
                    className="btn primary sm"
                    style={{ fontSize: 10, padding: '3px 8px' }}
                    onClick={() => handleAddToCanvas(m)}
                    title="Add to canvas"
                  >
                    <Ic.plus style={{ width: 10, height: 10 }} /> Use
                  </button>
                  <button
                    className="btn ghost sm icon"
                    onClick={() => removeMechanic(m.id)}
                    title="Delete mechanic"
                  >
                    <Ic.x style={{ width: 12, height: 12, color: 'var(--danger)' }} />
                  </button>
                </div>
                {m.description && (
                  <div style={{ fontSize: 10, color: 'var(--faint)', marginLeft: 34 }}>{m.description}</div>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
