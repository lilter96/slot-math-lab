import { useState, useEffect } from 'react';
import { useAppStore } from '../store';
import Inspector from './Inspector';
import SymbolEditor from './tables/SymbolEditor';
import PaytableEditor from './tables/PaytableEditor';
import ReelStripEditor from './tables/ReelStripEditor';
import BoardConfigEditor from './tables/BoardConfigEditor';
import MechanicManager from './mechanics/MechanicManager';
import PluginManager from './mechanics/PluginManager';

type MainTab = 'inspector' | 'tables' | 'mechanics';
type TableSection = 'symbols' | 'paytable' | 'reels' | 'board';
type MechanicsTab = 'mechanics' | 'plugins';

const SYMBOL_IDS = ['S1', 'S2', 'S3', 'W1', 'SC1'];

export default function RightPanel() {
  const selectedNodeId = useAppStore((s) => s.selectedNodeId);
  const [mainTab, setMainTab] = useState<MainTab>('inspector');
  const [tableSection, setTableSection] = useState<TableSection>('symbols');
  const [mechanicsTab, setMechanicsTab] = useState<MechanicsTab>('mechanics');

  useEffect(() => {
    if (selectedNodeId) setMainTab('inspector');
  }, [selectedNodeId]);

  return (
    <div className="panel" style={{ width: 360, flexShrink: 0, display: 'flex', flexDirection: 'column' }}>
      <div className="panel-h" style={{ padding: '6px 8px', gap: 2 }}>
        <button
          className={'tab' + (mainTab === 'inspector' ? ' active' : '')}
          onClick={() => setMainTab('inspector')}
          style={{ fontSize: 11 }}
        >
          Inspector
        </button>
        <button
          className={'tab' + (mainTab === 'tables' ? ' active' : '')}
          onClick={() => setMainTab('tables')}
          style={{ fontSize: 11 }}
        >
          Tables
        </button>
        <button
          className={'tab' + (mainTab === 'mechanics' ? ' active' : '')}
          onClick={() => setMainTab('mechanics')}
          style={{ fontSize: 11 }}
        >
          Mechanics
        </button>
      </div>

      {mainTab === 'inspector' && (
        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
          <Inspector />
        </div>
      )}

      {mainTab === 'tables' && (
        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
          <div className="panel-h" style={{ gap: 2, padding: '6px 8px', flexShrink: 0 }}>
            {(['symbols', 'paytable', 'reels', 'board'] as TableSection[]).map((s) => (
              <button
                key={s}
                className={'tab' + (tableSection === s ? ' active' : '')}
                onClick={() => setTableSection(s)}
                style={{ fontSize: 11 }}
              >
                {s === 'symbols' ? 'Symbols' : s === 'paytable' ? 'Paytable' : s === 'reels' ? 'Reels' : 'Board'}
              </button>
            ))}
          </div>
          <div style={{ flex: 1, overflowY: 'auto' }}>
            {tableSection === 'symbols' && <SymbolEditor />}
            {tableSection === 'paytable' && <PaytableEditor symbolIds={SYMBOL_IDS} />}
            {tableSection === 'reels' && <ReelStripEditor symbolIds={SYMBOL_IDS} />}
            {tableSection === 'board' && <BoardConfigEditor />}
          </div>
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
    </div>
  );
}
