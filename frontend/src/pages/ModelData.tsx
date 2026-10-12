import { useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import WeightedDrawEditor from '../components/editor/WeightedDrawEditor';
import ClusterPayoutEditor from '../components/editor/ClusterPayoutEditor';
import ReelStripEditor from '../components/editor/ReelStripEditor';
import ProjectDataEditor from '../components/editor/ProjectDataEditor';
import { rootProject } from '../lib/projectFiles';
import { useAppStore } from '../store';
export default function ModelData() {
  const projectName = useAppStore(state => String(state.graphTrail[0]?.graph.name ?? state.configName));
  const [showRaw, setShowRaw] = useState(false);
  const [params, setParams] = useSearchParams();
  const requested = params.get('view');
  const view = requested === 'reels' || requested === 'payouts' ? requested : 'weights';
  const setView = (value: string) => setParams(previous => { const next = new URLSearchParams(previous); next.set('view', value); return next; });
  const views = [{ id: 'weights', name: 'Weighted draws' }, { id: 'reels', name: 'Reel strips' }, { id: 'payouts', name: 'Cluster payouts' }];
  return <section className="model-data-page" aria-labelledby="data-heading"><header className="page-heading"><div><h1 id="data-heading">Model data</h1><p><strong>{projectName}</strong> · Source-backed inputs. Validate before applying.</p></div><button className="btn" onClick={() => { rootProject(); setShowRaw(true); }}>State & expressions</button></header>
    <div className="data-view-switch" role="tablist" aria-label="Model data views" onKeyDown={event => {
      if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
      event.preventDefault();
      const index = event.key === 'Home' ? 0 : event.key === 'End' ? views.length - 1 : (views.findIndex(item => item.id === view) + (event.key === 'ArrowLeft' ? -1 : 1) + views.length) % views.length;
      setView(views[index].id); event.currentTarget.querySelectorAll<HTMLButtonElement>('button')[index]?.focus();
    }}>{views.map(item => <button key={item.id} className="btn" role="tab" tabIndex={view === item.id ? 0 : -1} aria-selected={view === item.id} onClick={() => setView(item.id)}>{item.name}</button>)}</div>
    {view === 'weights' ? <WeightedDrawEditor /> : view === 'reels' ? <ReelStripEditor /> : <ClusterPayoutEditor />}
    <details className="data-capability-notice"><summary>Other model capabilities</summary><p>Finite inline draws, guarded uniform reel/stop mappings and a guarded cluster payout mapping are supported. Customized or conditioned sources require a complete source mapping. The generic state and expression editor remains available.</p><Link className="btn" to="/build">Inspect logic graph</Link></details>
    {showRaw && <ProjectDataEditor onClose={() => setShowRaw(false)} />}
  </section>;
}
