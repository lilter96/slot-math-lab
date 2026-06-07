import { useState, useCallback } from 'react';
import { useAppStore, type PluginContract } from '../../store';
import { Ic } from '../Icons';

export default function PluginManager() {
  const plugins = useAppStore((s) => s.plugins);
  const registerPlugin = useAppStore((s) => s.registerPlugin);
  const removePlugin = useAppStore((s) => s.removePlugin);
  const [selected, setSelected] = useState<string | null>(null);
  const [newPlugin, setNewPlugin] = useState({ pluginId: '', contract: 'IEvaluator' as PluginContract, version: '1.0.0' });
  const [error, setError] = useState<string | null>(null);

  const selectedPlugin = plugins.find((p) => p.pluginId === selected) ?? null;

  const handleRegister = useCallback(() => {
    if (!newPlugin.pluginId.trim()) {
      setError('Plugin ID is required');
      return;
    }
    if (plugins.some((p) => p.pluginId === newPlugin.pluginId)) {
      setError(`Plugin "${newPlugin.pluginId}" is already registered`);
      return;
    }
    registerPlugin({
      ...newPlugin,
      isConformant: true,
      forcesSampledRegime: true, // plugins default to sampled
    });
    setNewPlugin({ pluginId: '', contract: 'IEvaluator', version: '1.0.0' });
    setError(null);
  }, [newPlugin, plugins, registerPlugin]);

  return (
    <div>
      <div className="panel-h">
        <span className="t">Plugin Management</span>
        <span className="s">{plugins.length} registered</span>
      </div>
      <div className="panel-body">
        {/* ── Plugin list ── */}
        <div className="section-label">Registered Plugins</div>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          {plugins.map((p) => (
            <div
              key={p.pluginId}
              className="pal-item"
              style={{ cursor: 'pointer', margin: 0 }}
              onClick={() => setSelected(p.pluginId)}
            >
              <div className="pic" style={{
                color: p.isConformant ? 'var(--exact)' : 'var(--danger)',
                background: p.isConformant ? 'var(--exact-dim)' : 'var(--danger-dim)',
              }}>
                {p.isConformant ? <Ic.check /> : <Ic.x />}
              </div>
              <div style={{ flex: 1 }}>
                <div className="pt" style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                  {p.pluginId}
                  {p.forcesSampledRegime && (
                    <span className="prov sampled" style={{ fontSize: 9 }} title="Forces sampled regime">
                      <span className="pdot" />Sampled
                    </span>
                  )}
                </div>
                <div className="ps">{p.contract} · v{p.version || '1.0.0'}</div>
              </div>
              <button
                className="btn ghost sm icon"
                onClick={(e) => { e.stopPropagation(); removePlugin(p.pluginId); if (selected === p.pluginId) setSelected(null); }}
                title="Remove plugin"
              >
                <Ic.x style={{ width: 12, height: 12 }} />
              </button>
            </div>
          ))}
        </div>

        {/* ── Plugin detail ── */}
        {selectedPlugin && (
          <div style={{ marginTop: 14, padding: 12, background: 'var(--bg)', borderRadius: 8, border: '1px solid var(--line)' }}>
            <div className="section-label">Plugin Details</div>
            <div className="mini-row" style={{ padding: '4px 0' }}>
              <span className="k">ID</span>
              <span className="v">{selectedPlugin.pluginId}</span>
            </div>
            <div className="mini-row" style={{ padding: '4px 0' }}>
              <span className="k">Contract</span>
              <span className="v">{selectedPlugin.contract}</span>
            </div>
            <div className="mini-row" style={{ padding: '4px 0' }}>
              <span className="k">Version</span>
              <span className="v">{selectedPlugin.version || '1.0.0'}</span>
            </div>
            <div className="mini-row" style={{ padding: '4px 0' }}>
              <span className="k">Conformance</span>
              <span className="v" style={{ color: selectedPlugin.isConformant ? 'var(--exact)' : 'var(--danger)' }}>
                {selectedPlugin.isConformant ? 'Passed' : 'Failed'}
              </span>
            </div>
            {selectedPlugin.conformanceNote && (
              <div className="mini-row" style={{ padding: '4px 0' }}>
                <span className="k">Note</span>
                <span className="v" style={{ color: 'var(--danger)' }}>{selectedPlugin.conformanceNote}</span>
              </div>
            )}
            <div className="mini-row" style={{ padding: '4px 0' }}>
              <span className="k">Regime</span>
              <span className="v">
                {selectedPlugin.forcesSampledRegime ? (
                  <span className="prov sampled" style={{ fontSize: 10 }}>
                    <span className="pdot" />Sampled
                  </span>
                ) : (
                  <span className="prov exact" style={{ fontSize: 10 }}>
                    <span className="pdot" />Exact-compatible
                  </span>
                )}
              </span>
            </div>

            {!selectedPlugin.isConformant && (
              <div style={{
                marginTop: 10,
                padding: '8px 12px',
                background: 'var(--danger-dim)',
                border: '1px solid var(--danger)',
                borderRadius: 6,
                color: 'var(--danger)',
                fontSize: 11,
                fontFamily: 'var(--mono)',
              }}>
                ⚠️ Cannot use: {selectedPlugin.conformanceNote || 'Plugin failed conformance tests'}
              </div>
            )}

            {selectedPlugin.isConformant && (
              <div style={{
                marginTop: 10,
                padding: '8px 12px',
                background: selectedPlugin.forcesSampledRegime ? 'var(--sampled-dim)' : 'var(--exact-dim)',
                border: '1px solid ' + (selectedPlugin.forcesSampledRegime ? 'var(--sampled)' : 'var(--exact)'),
                borderRadius: 6,
                color: 'var(--text)',
                fontSize: 11,
              }}>
                ✅ Can use — {selectedPlugin.forcesSampledRegime
                  ? 'This plugin forces the sampled regime. Exact results unavailable.'
                  : 'This plugin is exact-compatible.'}
              </div>
            )}
          </div>
        )}

        <div className="divider" />

        {/* ── Register new plugin ── */}
        <div className="section-label">Register Plugin</div>
        <div className="field">
          <label>Plugin ID</label>
          <input
            className="inp"
            value={newPlugin.pluginId}
            onChange={(e) => setNewPlugin({ ...newPlugin, pluginId: e.target.value })}
            placeholder="e.g. megaways-evaluator"
          />
        </div>
        <div className="field">
          <label>Contract</label>
          <select
            className="inp"
            value={newPlugin.contract}
            onChange={(e) => setNewPlugin({ ...newPlugin, contract: e.target.value as PluginContract })}
          >
            <option value="IEvaluator">IEvaluator</option>
            <option value="ITransform">ITransform</option>
            <option value="WeightSource">WeightSource</option>
          </select>
        </div>
        {error && (
          <div style={{ color: 'var(--danger)', fontSize: 11, marginBottom: 8 }}>{error}</div>
        )}
        <button className="btn primary sm" onClick={handleRegister} disabled={!newPlugin.pluginId.trim()}>
          <Ic.plus style={{ width: 12, height: 12 }} /> Register
        </button>
      </div>
    </div>
  );
}
