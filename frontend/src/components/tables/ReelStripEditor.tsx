import { useState, useCallback, type ClipboardEvent } from 'react';

interface ReelStripEditorProps {
  symbolIds: string[];
}

export default function ReelStripEditor({ symbolIds }: ReelStripEditorProps) {
  const defaultStrip = symbolIds.join(',').repeat(3).split(',').slice(0, 30);
  const [strips, setStrips] = useState<string[][]>([[...defaultStrip], [...defaultStrip], [...defaultStrip], [...defaultStrip], [...defaultStrip]]);

  const addSymbol = useCallback((stripIdx: number, symbolId: string) => {
    setStrips((prev) => {
      const next = [...prev];
      next[stripIdx] = [...next[stripIdx], symbolId];
      return next;
    });
  }, []);

  const removeSymbol = useCallback((stripIdx: number, pos: number) => {
    setStrips((prev) => {
      const next = [...prev];
      next[stripIdx] = next[stripIdx].filter((_, i) => i !== pos);
      return next;
    });
  }, []);

  const handlePaste = useCallback((e: ClipboardEvent) => {
    const text = e.clipboardData.getData('text');
    if (!text) return;
    e.preventDefault();
    const rows = text.trim().split('\n');
    setStrips((prev) => {
      const next = [...prev];
      for (let r = 0; r < Math.min(rows.length, next.length); r++) {
        const symbols = rows[r].split(/[\t,]/).map((s) => s.trim()).filter(Boolean);
        if (symbols.length > 0) {
          next[r] = symbols;
        }
      }
      return next;
    });
  }, []);

  return (
    <div onPaste={handlePaste}>
      <div className="panel-h">
        <span className="t">Reel Strips</span>
        <span className="s">{strips.length} reels · {strips.reduce((s, r) => s + r.length, 0)} stops</span>
      </div>
      <div className="panel-body" style={{ padding: 0 }}>
        {strips.map((strip, stripIdx) => (
          <div key={stripIdx} style={{ marginBottom: 10, padding: '0 12px' }}>
            <div className="section-label">Reel {stripIdx + 1} ({strip.length} stops)</div>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4, alignItems: 'center' }}>
              {strip.map((sym, pos) => (
                <div key={pos} style={{ position: 'relative' }}>
                  <span
                    className="sym-chip"
                    style={{ cursor: 'pointer' }}
                    onClick={() => removeSymbol(stripIdx, pos)}
                    title={`${sym} — click to remove`}
                  >
                    <span className="sw" style={{ background: symbolIds.includes(sym) ? 'var(--n-eval)' : 'var(--faint)' }} />
                    {sym}
                  </span>
                </div>
              ))}
              <select
                className="inp"
                style={{ width: 'auto', padding: '2px 6px', fontSize: 11 }}
                value=""
                onChange={(e) => {
                  if (e.target.value) addSymbol(stripIdx, e.target.value);
                  e.target.value = '';
                }}
              >
                <option value="">+ Add</option>
                {symbolIds.map((id) => (
                  <option key={id} value={id}>{id}</option>
                ))}
              </select>
            </div>
          </div>
        ))}
        <div className="hint" style={{ padding: '8px 12px' }}>
          Click a symbol to remove. Paste TSV from spreadsheet (one strip per row, comma or tab separated).
        </div>
      </div>
    </div>
  );
}
