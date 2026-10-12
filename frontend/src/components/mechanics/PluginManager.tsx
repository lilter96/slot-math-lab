import { useState } from 'react';
import { usePluginsQuery } from '../../api/hooks';

export default function PluginManager() {
  const registry = usePluginsQuery();
  const [selected, setSelected] = useState<string | null>(null);
  const plugins = registry.isError || registry.isFetching ? [] : registry.data ?? [];
  const selectedPlugin = plugins.find(plugin => plugin.pluginId === selected);
  return <div>
    <div className="panel-h"><span className="t">Plugin registry</span></div>
    <div className="panel-body">
      <p className="hint">Plugins are managed by the server configuration. Registration and removal are unavailable in this workbench.</p>
      {registry.isFetching && <p role="status">Loading plugin registry…</p>}
      {registry.isError && <p role="alert">{registry.error.message} <button className="btn" onClick={() => void registry.refetch()}>Retry plugin registry</button></p>}
      {!registry.isPending && !registry.isFetching && !registry.isError && !plugins.length && <p className="hint">No server plugins are registered. Your graph remains available for editing.</p>}
      {plugins.map(plugin => <div className="field" key={plugin.pluginId}>
        <button className="btn" aria-label={`Inspect plugin ${plugin.pluginId}`} onClick={() => setSelected(plugin.pluginId)}>{plugin.pluginId}</button>
        <p className="hint">{plugin.contract} · {plugin.version ? `v${plugin.version}` : 'Version not provided'} · {plugin.isConformant ? 'Contract checks passed' : 'Contract checks failed'}</p>
        <button className="btn" disabled aria-label={`Remove plugin ${plugin.pluginId}`} title="Server plugin removal is unavailable">Remove plugin</button>
      </div>)}
      {selectedPlugin && <section aria-label="Selected plugin details">
        <h3>{selectedPlugin.pluginId}</h3>
        <dl><dt>Contract</dt><dd>{selectedPlugin.contract}</dd><dt>Version</dt><dd>{selectedPlugin.version ?? 'Not provided'}</dd><dt>Contract checks</dt><dd>{selectedPlugin.isConformant ? 'Passed' : 'Failed'}</dd></dl>
        <p className="hint">The registry does not provide evaluation-method compatibility. A completed run supplies its own mathematical evidence.</p>
      </section>}
    </div>
  </div>;
}
