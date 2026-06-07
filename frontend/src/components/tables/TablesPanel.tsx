import { useState } from 'react';
import SymbolEditor from './SymbolEditor';
import PaytableEditor from './PaytableEditor';
import ReelStripEditor from './ReelStripEditor';
import BoardConfigEditor from './BoardConfigEditor';

const SECTIONS = ['symbols', 'paytable', 'reels', 'board'] as const;
type Section = (typeof SECTIONS)[number];

const SYMBOL_IDS = ['S1', 'S2', 'S3', 'W1', 'SC1'];

export default function TablesPanel() {
  const [active, setActive] = useState<Section>('symbols');

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
             'Board'}
          </button>
        ))}
      </div>
      <div style={{ flex: 1, overflowY: 'auto' }}>
        {active === 'symbols' && <SymbolEditor />}
        {active === 'paytable' && <PaytableEditor symbolIds={SYMBOL_IDS} />}
        {active === 'reels' && <ReelStripEditor symbolIds={SYMBOL_IDS} />}
        {active === 'board' && <BoardConfigEditor />}
      </div>
    </div>
  );
}
