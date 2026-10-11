import { Link, useNavigate } from 'react-router-dom';
import { useAppStore } from '../store';
import { rootProject, openMechanic } from '../lib/projectFiles';
import { modelCapabilities, mechanicLabel } from '../lib/modelProjection';
import { manifest, assets } from '../games/nine-lives/manifest';
import { DEFAULT_FEATURES, useFeaturesQuery } from '../api/hooks';
import { useModelProjection } from '../hooks/useModelProjection';
import StarterLibrary from '../components/editor/StarterLibrary';

export default function Overview() {
  // Subscribe to the actual draft, including nested edits. Projection never mutates it.
  const name = useAppStore(s => s.configName);
  const navigate = useNavigate();
  const features = useFeaturesQuery().data ?? DEFAULT_FEATURES;
  const graph = useModelProjection();
  const nineLives = String(graph?.id ?? '').startsWith('nine-lives');
  const board = graph?.boardConfig as { rows?: number; columns?: number } | undefined;
  const rows = board?.rows, columns = board?.columns;
  const geometry = rows != null && columns != null && rows > 0 && columns > 0 && rows * columns <= 120;
  const capabilities = modelCapabilities(graph ?? {});
  const mechanics = graph?.mechanics ?? {};
  const symbols = graph?.symbols as { id: string; name: string }[] | undefined;
  const open = (id: string) => { rootProject(); openMechanic(id); navigate('/build'); };
  return <section className="overview-page" aria-labelledby="overview-heading">
    <header className="page-heading"><div><h1 id="overview-heading">{String(graph?.name ?? name ?? 'Your model')}</h1><p>{nineLives ? 'Cluster wins, cascading symbols and free spins.' : 'Explore the inputs and logic of your current model.'}</p></div><Link className="btn" to="/build">Explore logic graph</Link></header>
    <div className="overview-model">
      <div className="model-board-region">
        <div className="board-heading"><h2>Game structure</h2><span>{geometry ? `${columns} reels × ${rows} rows` : 'No board geometry defined'}</span></div>
        {geometry && <div className="structure-board" role="img" aria-label={`${columns} reels by ${rows} rows; model geometry, not a spin result`} style={{ gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))` }}>{Array.from({ length: rows * columns }, (_, index) => <span key={index}>{Math.floor(index / columns) + 1}:{index % columns + 1}</span>)}</div>}
        <p className="hint">Configured geometry. No spin has been generated in this view.</p>
        {symbols?.length ? <div className="symbol-reference" aria-label="Model symbols">{symbols.map(symbol => <div key={symbol.id}>{nineLives && assets.images?.[symbol.name] && <img src={assets.images?.[symbol.name]} alt="" width="44" height="44" />}<span>{symbol.name}</span><small>ID {symbol.id}</small></div>)}</div> : <p className="hint">This model does not define symbol metadata.</p>}
      </div>
      <div className="model-summary"><h2>Model inputs</h2><dl><div><dt>Logic definitions</dt><dd>{Object.keys(mechanics).length}</dd></div><div><dt>Weighted draws</dt><dd>{capabilities.weightedDraws}</dd></div><div><dt>Reel strips</dt><dd>{capabilities.reelStrips ? 'Defined in model' : 'Not exposed by this model'}</dd></div><div><dt>Paytable metadata</dt><dd>{capabilities.paytables ? 'Available; see source mapping' : 'Not exposed by this model'}</dd></div></dl>
        {nineLives && <p className="model-notice">Uncalibrated research model. Configured targets are not measured RTP.</p>}
        <Link className="btn" to="/model/data">Edit model inputs</Link>
        {features.play && nineLives && <Link className="btn" to="/play">Test spin</Link>}
      </div>
    </div>
    <section className="overview-mechanics"><h2>Explore mechanics</h2><p>Open the definition that implements each part of the game.</p><div className="feature-list">{Object.keys(mechanics).filter(id => nineLives ? ['base-spin', 'cascade-step', 'bonus', 'free-spin', 'round-entry'].includes(id) : true).slice(0, 8).map(id => <button key={id} onClick={() => open(id)}><strong>{mechanicLabel(id)}</strong><span>{mechanics[id].description ?? `${mechanics[id].nodes.length} nodes`}</span><small>Open logic</small></button>)}</div>{nineLives && <p className="hint">Presentation features: {manifest.features.join(', ')}. Definitions above come from the editable graph.</p>}</section>
    <section className="overview-evidence"><div><h2>Analysis evidence</h2><p>This view has no verified result for the current model. Review saved runs with their model hashes, seeds and calculation methods.</p></div><Link className="btn" to="/results">Review results</Link></section>
    <section className="overview-library"><h2>Create or open a model</h2><StarterLibrary /></section>
  </section>;
}
