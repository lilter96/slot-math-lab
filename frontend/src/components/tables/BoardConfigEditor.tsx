import { useState, useCallback } from 'react';
import { BoardConfig, type BoardConfig as BoardConfigType } from './schemas';

export default function BoardConfigEditor() {
  const [config, setConfig] = useState<BoardConfigType>({
    rows: 3,
    columns: 5,
    allowMultiSymbol: false,
    allowEmpty: false,
    allowLocked: false,
    growable: false,
  });
  const [errors, setErrors] = useState<string | null>(null);

  const update = useCallback(<K extends keyof BoardConfigType>(key: K, value: BoardConfigType[K]) => {
    setConfig((prev) => {
      const next = { ...prev, [key]: value };
      const r = BoardConfig.safeParse(next);
      setErrors(r.success ? null : r.error.issues.map((i) => `${i.path.join('.')}: ${i.message}`).join('; '));
      return next;
    });
  }, []);

  const valid = BoardConfig.safeParse(config).success;

  return (
    <div>
      <div className="panel-h">
        <span className="t">Board Config</span>
        <span className="s">{config.columns}×{config.rows}</span>
      </div>
      <div className="panel-body">
        {/* Grid visualization */}
        <div style={{ marginBottom: 16, background: 'var(--bg-canvas)', borderRadius: 8, padding: 16, border: '1px solid var(--line)' }}>
          <div style={{
            display: 'grid',
            gridTemplateColumns: `repeat(${config.columns}, 1fr)`,
            gap: 4,
          }}>
            {Array.from({ length: config.rows * config.columns }, (_, i) => (
              <div key={i} style={{
                aspectRatio: '1',
                background: 'var(--bg-2)',
                border: '1px solid var(--line)',
                borderRadius: 4,
                display: 'grid',
                placeItems: 'center',
                fontSize: 9,
                color: 'var(--faint)',
                fontFamily: 'var(--mono)',
              }}>
                {i + 1}
              </div>
            ))}
          </div>
        </div>

        <div className="field">
          <label>Rows</label>
          <div className="stepper">
            <button onClick={() => update('rows', Math.max(1, config.rows - 1))}>−</button>
            <input
              className="inp"
              type="number"
              min={1} max={20}
              value={config.rows}
              onChange={(e) => update('rows', parseInt(e.target.value) || 3)}
            />
            <button onClick={() => update('rows', Math.min(20, config.rows + 1))}>+</button>
          </div>
        </div>

        <div className="field">
          <label>Columns</label>
          <div className="stepper">
            <button onClick={() => update('columns', Math.max(1, config.columns - 1))}>−</button>
            <input
              className="inp"
              type="number"
              min={1} max={20}
              value={config.columns}
              onChange={(e) => update('columns', parseInt(e.target.value) || 5)}
            />
            <button onClick={() => update('columns', Math.min(20, config.columns + 1))}>+</button>
          </div>
        </div>

        <div className="divider" />

        <div className="section-label">Cell Options</div>

        {(['allowMultiSymbol', 'allowEmpty', 'allowLocked', 'growable'] as const).map((key) => (
          <label key={key} className="row" style={{ marginBottom: 10, justifyContent: 'space-between' }}>
            <span style={{ flex: 'none', color: 'var(--muted)', fontSize: '12px' }}>
              {key === 'allowMultiSymbol' ? 'Multi-symbol cells' :
               key === 'allowEmpty' ? 'Empty cells' :
               key === 'allowLocked' ? 'Locked cells' :
               'Growable board'}
            </span>
            <button
              role="switch"
              aria-checked={config[key]}
              onClick={() => update(key, !config[key])}
              className={'toggle' + (config[key] ? ' on' : '')}
            />
          </label>
        ))}

        {errors && (
          <div style={{ color: 'var(--danger)', fontSize: 11, fontFamily: 'var(--mono)', marginTop: 8 }}>
            {errors}
          </div>
        )}
        {valid && !errors && (
          <div style={{ color: 'var(--exact)', fontSize: 11, fontFamily: 'var(--mono)', marginTop: 8 }}>
            ✅ Board config valid
          </div>
        )}
      </div>
    </div>
  );
}
