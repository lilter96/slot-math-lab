import { useState } from 'react';
import { useAppStore } from '../store';
import Inspector from './Inspector';
import MechanicManager from './mechanics/MechanicManager';
import PluginManager from './mechanics/PluginManager';
import AutoTunePanel from './ai/AutoTunePanel';

type MainTab = 'inspector' | 'mechanics' | 'ai';
type MechanicsTab = 'mechanics' | 'plugins';

export default function RightPanel() {
  const selectedNodeId = useAppStore((s) => s.selectedNodeId);
  const [preferredTab, setPreferredTab] = useState<MainTab>('inspector');
  const [mechanicsTab, setMechanicsTab] = useState<MechanicsTab>('mechanics');

  const mainTab: MainTab = selectedNodeId ? 'inspector' : preferredTab;
  const openTab = (next: MainTab) => { useAppStore.getState().selectNode(null); setPreferredTab(next); };

  return (
    <div className="panel" style={{ width: 360, flexShrink: 0, display: 'flex', flexDirection: 'column' }}>
      <div className="panel-h" style={{ padding: '6px 8px', gap: 2 }}>
        <button
          className={'tab' + (mainTab === 'inspector' ? ' active' : '')}
          onClick={() => openTab('inspector')}
          style={{ fontSize: 11 }}
        >
          Inspector
        </button>
        <button
          className={'tab' + (mainTab === 'mechanics' ? ' active' : '')}
          onClick={() => openTab('mechanics')}
          style={{ fontSize: 11 }}
        >
          Mechanics
        </button>
        <button
          className={'tab' + (mainTab === 'ai' ? ' active' : '')}
          onClick={() => openTab('ai')}
          style={{ fontSize: 11 }}
        >
          AI
        </button>
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
              style={{ fontSize: 11 }}
            >
              Custom Mechanics
            </button>
            <button
              className={'tab' + (mechanicsTab === 'plugins' ? ' active' : '')}
              onClick={() => setMechanicsTab('plugins')}
              style={{ fontSize: 11 }}
            >
              Plugins
            </button>
          </div>
          <div style={{ flex: 1, overflowY: 'auto' }}>
            {mechanicsTab === 'mechanics' && <MechanicManager />}
            {mechanicsTab === 'plugins' && <PluginManager />}
          </div>
        </div>
      )}

      {mainTab === 'ai' && (
        <div style={{ flex: 1, overflowY: 'auto' }}>
          <AutoTunePanel />
        </div>
      )}
    </div>
  );
}
