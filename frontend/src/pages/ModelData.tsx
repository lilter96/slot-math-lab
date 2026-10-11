import { useState } from 'react';
import { Link } from 'react-router-dom';
import WeightedDrawEditor from '../components/editor/WeightedDrawEditor';
import ProjectDataEditor from '../components/editor/ProjectDataEditor';
import { rootProject } from '../lib/projectFiles';
export default function ModelData() {
  const [showRaw, setShowRaw] = useState(false);
  return <section className="model-data-page" aria-labelledby="data-heading"><header className="page-heading"><div><h1 id="data-heading">Model data</h1><p>Source-backed input tables. Validate changes before applying them.</p></div><button className="btn" onClick={() => { rootProject(); setShowRaw(true); }}>State & expressions</button></header>
    <WeightedDrawEditor />
    <details className="data-capability-notice"><summary>Other model capabilities</summary><p>This editor supports finite inline weighted draws. Physical reel strips and payout rules need their own source mappings. The generic state and expression editor remains available; inspect the graph before changing data whose meaning is unclear.</p><Link className="btn" to="/build">Inspect logic graph</Link></details>
    {showRaw && <ProjectDataEditor onClose={() => setShowRaw(false)} />}
  </section>;
}
