import { useState, useCallback, useRef, type KeyboardEvent, type ClipboardEvent } from 'react';
import { SymbolDef, type SymbolDef as SymbolDefType } from './schemas';

const DEFAULT_SYMBOL: SymbolDefType = { id: '', name: '', kind: 'Standard', color: '#4c6ef5' };

export default function SymbolEditor() {
  const [symbols, setSymbols] = useState<SymbolDefType[]>([
    { id: 'S1', name: 'Cherry', kind: 'Standard', color: '#e03131' },
    { id: 'S2', name: 'Lemon', kind: 'Standard', color: '#f08c00' },
    { id: 'S3', name: 'Bell', kind: 'Standard', color: '#f06595' },
    { id: 'W1', name: 'Wild', kind: 'Wild', color: '#2f9e44' },
    { id: 'SC1', name: 'Scatter', kind: 'Scatter', color: '#7950f2' },
  ]);
  const [errors, setErrors] = useState<Record<number, string>>({});
  const [focusedCell, setFocusedCell] = useState<{ row: number; col: string } | null>(null);
  const tableRef = useRef<HTMLTableElement>(null);

  const validate = useCallback((s: SymbolDefType) => {
    const r = SymbolDef.safeParse(s);
    if (!r.success) {
      return r.error.issues.map((i) => `${i.path.join('.') || 'field'}: ${i.message}`).join('; ');
    }
    // Check unique IDs
    return null;
  }, []);

  const updateSymbol = useCallback((index: number, field: keyof SymbolDefType, value: string) => {
    setSymbols((prev) => {
      const next = [...prev];
      next[index] = { ...next[index], [field]: value };
      const err = validate(next[index]);
      setErrors((e) => ({ ...e, [index]: err || '' }));
      return next;
    });
  }, [validate]);

  const addRow = useCallback(() => {
    setSymbols((prev) => [...prev, { ...DEFAULT_SYMBOL, id: `S${prev.length + 1}` }]);
  }, []);

  const removeRow = useCallback((index: number) => {
    setSymbols((prev) => prev.filter((_, i) => i !== index));
    setErrors((prev) => {
      const next = { ...prev };
      delete next[index];
      return next;
    });
  }, []);

  const handleKeyDown = useCallback((e: KeyboardEvent<HTMLInputElement>, row: number, col: string) => {
    const cols = ['id', 'name', 'kind', 'color'];
    const colIdx = cols.indexOf(col);
    if (e.key === 'Tab' && !e.shiftKey && colIdx === cols.length - 1 && row < symbols.length - 1) {
      e.preventDefault();
      setFocusedCell({ row: row + 1, col: cols[0] });
    }
    if (e.key === 'Tab' && e.shiftKey && colIdx === 0 && row > 0) {
      e.preventDefault();
      setFocusedCell({ row: row - 1, col: cols[cols.length - 1] });
    }
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setFocusedCell({ row: Math.min(row + 1, symbols.length - 1), col });
    }
    if (e.key === 'ArrowUp') {
      e.preventDefault();
      setFocusedCell({ row: Math.max(row - 1, 0), col });
    }
    if (e.key === 'Delete' && e.ctrlKey) {
      e.preventDefault();
      removeRow(row);
    }
  }, [symbols.length, removeRow]);

  const handlePaste = useCallback((e: ClipboardEvent) => {
    const text = e.clipboardData.getData('text');
    if (!text) return;

    const rows = text.trim().split('\n');
    const parsed: Partial<SymbolDefType>[] = [];
    for (const row of rows) {
      const cells = row.split('\t');
      if (cells.length >= 3) {
        parsed.push({
          id: cells[0]?.trim() || '',
          name: cells[1]?.trim() || '',
          kind: (cells[2]?.trim() || 'Standard') as SymbolDefType['kind'],
          color: cells[3]?.trim() || '#4c6ef5',
        });
      }
    }

    if (parsed.length > 0) {
      e.preventDefault();
      setSymbols((prev) => {
        const next = [...prev];
        for (const p of parsed) {
          next.push({ ...DEFAULT_SYMBOL, ...p, kind: p.kind || 'Standard' });
        }
        return next;
      });
    }
  }, []);

  // Validate all on submit
  const allValid = Object.values(errors).every((e) => !e) && symbols.length > 0 && symbols.every((s) => SymbolDef.safeParse(s).success);

  return (
    <div onPaste={handlePaste}>
      <div className="panel-h">
        <span className="t">Symbols</span>
        <span className="s">{symbols.length} symbols</span>
      </div>
      <div className="panel-body" style={{ padding: 0 }}>
        <table className="tbl" ref={tableRef}>
          <thead>
            <tr>
              <th style={{ width: 80 }}>ID</th>
              <th style={{ width: 120 }}>Name</th>
              <th style={{ width: 110 }}>Kind</th>
              <th style={{ width: 70 }}>Color</th>
              <th style={{ width: 36 }}></th>
            </tr>
          </thead>
          <tbody>
            {symbols.map((s, i) => {
              const hasError = errors[i];
              return (
                <tr key={i} style={hasError ? { background: 'var(--danger-dim)' } : undefined}>
                  <td>
                    <input
                      className="tbl-inp"
                      value={s.id}
                      onChange={(e) => updateSymbol(i, 'id', e.target.value)}
                      onKeyDown={(e) => handleKeyDown(e, i, 'id')}
                      autoFocus={focusedCell?.row === i && focusedCell?.col === 'id'}
                      onFocus={() => setFocusedCell({ row: i, col: 'id' })}
                      style={hasError ? { color: 'var(--danger)' } : undefined}
                    />
                  </td>
                  <td>
                    <input
                      className="tbl-inp"
                      value={s.name}
                      onChange={(e) => updateSymbol(i, 'name', e.target.value)}
                      onKeyDown={(e) => handleKeyDown(e, i, 'name')}
                      onFocus={() => setFocusedCell({ row: i, col: 'name' })}
                    />
                  </td>
                  <td>
                    <select
                      className="tbl-inp"
                      value={s.kind}
                      onChange={(e) => updateSymbol(i, 'kind', e.target.value)}
                      onKeyDown={(e) => handleKeyDown(e as unknown as React.KeyboardEvent<HTMLInputElement>, i, 'kind')}
                      onFocus={() => setFocusedCell({ row: i, col: 'kind' })}
                    >
                      {SymbolDef.shape.kind.options.map((k) => (
                        <option key={k} value={k}>{k}</option>
                      ))}
                    </select>
                  </td>
                  <td>
                    <div className="symcell">
                      <span className="symchip" style={{ background: s.color }} />
                      <input
                        className="tbl-inp"
                        value={s.color}
                        onChange={(e) => updateSymbol(i, 'color', e.target.value)}
                        onKeyDown={(e) => handleKeyDown(e, i, 'color')}
                        onFocus={() => setFocusedCell({ row: i, col: 'color' })}
                        style={{ width: 56, padding: '6px 4px' }}
                      />
                    </div>
                  </td>
                  <td>
                    <button
                      className="btn ghost sm icon"
                      onClick={() => removeRow(i)}
                      title="Remove symbol"
                      style={{ padding: '2px 4px', color: 'var(--danger)' }}
                    >
                      ×
                    </button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>

        <div style={{ padding: '8px 12px', borderTop: '1px solid var(--line)' }}>
          <div className="row" style={{ gap: 6 }}>
            <button className="btn sm" onClick={addRow}>+ Add symbol</button>
            <div className="hint" style={{ margin: 0 }}>
              Paste TSV data (ID→Name→Kind→Color) from clipboard
            </div>
          </div>
          {!allValid && symbols.length > 0 && (
            <div style={{ marginTop: 8, color: 'var(--danger)', fontSize: 11, fontFamily: 'var(--mono)' }}>
              Fix validation errors before submitting
            </div>
          )}
          {allValid && symbols.length > 0 && (
            <div style={{ marginTop: 8, color: 'var(--exact)', fontSize: 11, fontFamily: 'var(--mono)' }}>
              ✅ All {symbols.length} symbols valid
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
