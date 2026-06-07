import { useState } from 'react';
import MechanicManager from './MechanicManager';
import PluginManager from './PluginManager';

type Tab = 'mechanics' | 'plugins';

export default function MechanicsPanel() {
  const [active, setActive] = useState<Tab>('mechanics');

  return (
    <div className="panel" style={{ flexShrink: 0, width: 400, display: 'flex', flexDirection: 'column' }}>
      <div className="panel-h" style={{ gap: 2, padding: '6px 8px' }}>
        <button
          className={'tab' + (active === 'mechanics' ? ' active' : '')}
          onClick={() => setActive('mechanics')}
          style={{ fontSize: 11 }}
        >
          Custom Mechanics
        </button>
        <button
          className={'tab' + (active === 'plugins' ? ' active' : '')}
          onClick={() => setActive('plugins')}
          style={{ fontSize: 11 }}
        >
          Plugins
        </button>
      </div>
      <div style={{ flex: 1, overflowY: 'auto' }}>
        {active === 'mechanics' && <MechanicManager />}
        {active === 'plugins' && <PluginManager />}
      </div>
    </div>
  );
}
