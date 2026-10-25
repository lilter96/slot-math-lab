import { useState } from 'react';
import { useAppStore } from '../store';
import Inspector from './Inspector';
import MechanicManager from './mechanics/MechanicManager';
import PluginManager from './mechanics/PluginManager';
import AutoTunePanel from './ai/AutoTunePanel';
import { DEFAULT_FEATURES, useFeaturesQuery } from '../api/hooks';

type MainTab = 'inspector' | 'mechanics' | 'ai';
type MechanicsTab = 'mechanics' | 'plugins';

export default function RightPanel({ onClose, initialTab = 'inspector' }: { onClose: () => void; initialTab?: MainTab }) {
  const selectedNodeId = useAppStore((s) => s.selectedNodeId);
  const pinned = useAppStore((s) => s.inspectorPinned);
  const [preferredTab, setPreferredTab] = useState<MainTab>(initialTab);
  const [mechanicsTab, setMechanicsTab] = useState<MechanicsTab>('mechanics');
  const features = useFeaturesQuery().data ?? DEFAULT_FEATURES;

  const mainTab: MainTab = selectedNodeId ? 'inspector' : preferredTab;
  const openTab = (next: MainTab) => { useAppStore.getState().selectNode(null); useAppStore.getState().setInspectorOpen(true); setPreferredTab(next); };
  const togglePin = () => {
    useAppStore.getState().setInspectorPinned(!pinned);
    if (pinned && !selectedNodeId) requestAnimationFrame(() => document.querySelector<HTMLElement>('[aria-label="Show inspector"]')?.focus());
  };

  return (
    <aside aria-label="Context inspector" className="panel context-inspector" style={{ width: 344, flexShrink: 0, display: 'flex', flexDirection: 'column' }}>
      <div className="panel-h" style={{ padding: '6px 8px', gap: 2 }}>
        <button
          className={'tab' + (mainTab === 'inspector' ? ' active' : '')}
          onClick={() => openTab('inspector')}
          style={{ fontSize: 13 }}
        >
          Inspector
        </button>
        <button
          className={'tab' + (mainTab === 'mechanics' ? ' active' : '')}
          onClick={() => openTab('mechanics')}
          style={{ fontSize: 13 }}
        >
          Mechanics
        </button>
        {features.autoTune && (
          <button
            className={'tab' + (mainTab === 'ai' ? ' active' : '')}
            onClick={() => openTab('ai')}
            style={{ fontSize: 13 }}
          >
            AI
          </button>
        )}
      </div>

      <div style={{ display: 'flex', gap: 8, padding: 8, flexWrap: 'wrap' }}>
        <button className="btn" aria-pressed={pinned} onClick={togglePin}>{pinned ? 'Unpin inspector' : 'Pin inspector'}</button>
        <button className="btn inspector-close" aria-label="Close inspector" onClick={onClose}>Close inspector</button>
      </div>
      {mainTab === 'inspector' && (
        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
          <Inspector />
        </div>
      )}

      {mainTab === 'mechanics' && (
        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
          <div className="panel-h" style={{ gap: 2, padding: '6px 8px', flexShrink: 0 }}>
            <button
              className={'tab' + (mechanicsTab === 'mechanics' ? ' active' : '')}
              onClick={() => setMechanicsTab('mechanics')}
              style={{ fontSize: 13 }}
            >
              Custom Mechanics
            </button>
            {features.plugins && (
              <button
                className={'tab' + (mechanicsTab === 'plugins' ? ' active' : '')}
                onClick={() => setMechanicsTab('plugins')}
                style={{ fontSize: 13 }}
              >
                Plugins
              </button>
            )}
          </div>
          <div style={{ flex: 1, overflowY: 'auto' }}>
            {mechanicsTab === 'mechanics' && <MechanicManager />}
            {features.plugins && mechanicsTab === 'plugins' && <PluginManager />}
          </div>
        </div>
      )}

      {features.autoTune && mainTab === 'ai' && (
        <div style={{ flex: 1, overflowY: 'auto' }}>
          <AutoTunePanel />
        </div>
      )}
    </aside>
  );
}
