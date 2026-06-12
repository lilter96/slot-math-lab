import { useState } from 'react';
import { useAppStore } from '../../store';
import SymbolEditor from './SymbolEditor';
import PaytableEditor from './PaytableEditor';
import ReelStripEditor from './ReelStripEditor';
import BoardConfigEditor from './BoardConfigEditor';

const SECTIONS = ['symbols', 'paytable', 'reels', 'state'] as const;
type Section = (typeof SECTIONS)[number];

export default function TablesPanel() {
  const [active, setActive] = useState<Section>('symbols');
  const tableSymbols = useAppStore((s) => s.tableSymbols);
  const symbolIds = tableSymbols.map((s) => s.id);

  return (
    <div className="panel" style={{ flexShrink: 0, width: 480, display: 'flex', flexDirection: 'column' }}>
      <div className="panel-h" style={{ gap: 2, padding: '6px 8px' }}>
        {SECTIONS.map((s) => (
          <button
            key={s}
            className={'tab' + (active === s ? ' active' : '')}
            onClick={() => setActive(s)}
            style={{ fontSize: 11 }}
          >
            {s === 'symbols' ? 'Symbols' :
             s === 'paytable' ? 'Paytable' :
             s === 'reels' ? 'Reels' :
             'State Config'}
          </button>
        ))}
      </div>
      <div style={{ flex: 1, overflowY: 'auto' }}>
        {active === 'symbols' && <SymbolEditor />}
        {active === 'paytable' && <PaytableEditor symbolIds={symbolIds} />}
        {active === 'reels' && <ReelStripEditor symbolIds={symbolIds} />}
        {active === 'state' && <BoardConfigEditor />}
      </div>
    </div>
  );
}
