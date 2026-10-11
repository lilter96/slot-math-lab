import { useAppStore } from '../../store';
import { loadProject, openMechanic } from '../../lib/projectFiles';
import { gameModelLibrary, starterLibrary } from '../../lib/starterLibrary';

export default function StarterLibrary() {
  const draft = useAppStore(s => s.authoringDraft);
  const open = (example: { create: () => Record<string, unknown>; mechanic?: string }, inspect: boolean) => {
    const current = useAppStore.getState();
    if (!current.authoringDraft) {
      const { nodes, edges, tables, graphTrail, verificationSource, configName, selectedNodeId } = current;
      useAppStore.setState({ authoringDraft: { nodes, edges, tables, graphTrail, verificationSource, configName, selectedNodeId } });
    }
    loadProject(example.create());
    useAppStore.setState({ graphTrail: [], verificationSource: null });
    if (inspect && example.mechanic) openMechanic(example.mechanic);
  };
  return <details className="grp" data-testid="starter-library">
    <summary className="section-label">Ready subgraphs</summary>
    <p className="hint">Open a working example or inspect its reusable parts. Your current model is kept.</p>
    {starterLibrary.map(example => <div key={example.id} style={{ margin: '12px 0' }}>
      <b>{example.name}</b><p className="hint">{example.summary}</p>
      <button className="btn sm" onClick={() => open(example, false)}>Open {example.name} example</button>
      <button className="btn sm" onClick={() => open(example, true)}>Inspect {example.name} subgraph</button>
    </div>)}
    <p className="section-label">Complete game models</p>
    <p className="hint">Open a migrated game's paid-round model, including its triggered features and shared definitions.</p>
    {gameModelLibrary.map(example => <button key={example.name} className="btn sm" onClick={() => open(example, false)}>Open {example.name} model</button>)}
    {draft && <button className="btn sm" onClick={() => useAppStore.setState({ ...draft, authoringDraft: null })}>Restore my model</button>}
    <p className="hint">Example RTP is uncalibrated. Edit the weights and payouts in Data tables. Use “Reuse this subgraph” to add another instance.</p>
  </details>;
}
