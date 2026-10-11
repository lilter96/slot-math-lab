import { useState } from 'react';
import { Link } from 'react-router-dom';
import WeightedDrawEditor from '../components/editor/WeightedDrawEditor';
import ClusterPayoutEditor from '../components/editor/ClusterPayoutEditor';
import ProjectDataEditor from '../components/editor/ProjectDataEditor';
import { rootProject } from '../lib/projectFiles';
export default function ModelData() {
  const [showRaw, setShowRaw] = useState(false);
  const [view, setView] = useState('weights');
  return <section className="model-data-page" aria-labelledby="data-heading"><header className="page-heading"><div><h1 id="data-heading">Model data</h1><p>Source-backed input tables. Validate changes before applying them.</p></div><button className="btn" onClick={() => { rootProject(); setShowRaw(true); }}>State & expressions</button></header>
    <div className="data-view-switch" role="tablist" aria-label="Model data views" onKeyDown={event => { if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return; event.preventDefault(); const next = event.key === 'Home' ? 'weights' : event.key === 'End' ? 'payouts' : view === 'weights' ? 'payouts' : 'weights'; setView(next); event.currentTarget.querySelectorAll<HTMLButtonElement>('button')[next === 'weights' ? 0 : 1]?.focus(); }}><button className="btn" role="tab" aria-selected={view === 'weights'} onClick={() => setView('weights')}>Weighted draws</button><button className="btn" role="tab" aria-selected={view === 'payouts'} onClick={() => setView('payouts')}>Cluster payouts</button></div>
    {view === 'weights' ? <WeightedDrawEditor /> : <ClusterPayoutEditor />}
    <details className="data-capability-notice"><summary>Other model capabilities</summary><p>Finite inline draws and a guarded cluster payout mapping are supported. Physical reel strips need a separate source mapping. Models may draw cells or strip positions; inspect the actual source before editing. The generic state and expression editor remains available.</p><Link className="btn" to="/build">Inspect logic graph</Link></details>
    {showRaw && <ProjectDataEditor onClose={() => setShowRaw(false)} />}
  </section>;
}
