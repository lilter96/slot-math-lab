import { useState, useCallback, type ClipboardEvent } from 'react';

interface PaytableEntry {
  symbolId: string;
  counts: number[];
  payouts: number[];
}

interface PaytableEditorProps {
  symbolIds: string[];
}

/** Build a 5-of-a-kind paytable — classic slot matrix. */
export default function PaytableEditor({ symbolIds }: PaytableEditorProps) {
  const maxOfAKind = 5;
  const [entries, setEntries] = useState<PaytableEntry[]>(() =>
    symbolIds.map((id) => ({
      symbolId: id,
      counts: Array.from({ length: maxOfAKind }, (_, i) => i + 1),
      payouts: Array.from({ length: maxOfAKind }, () => 0),
    })),
  );

  const updatePayout = useCallback((row: number, col: number, value: number) => {
    setEntries((prev) => {
      const next = [...prev];
      const payouts = [...next[row].payouts];
      payouts[col] = value;
      next[row] = { ...next[row], payouts };
      return next;
    });
  }, []);

  const handlePaste = useCallback((e: ClipboardEvent) => {
    const text = e.clipboardData.getData('text');
    if (!text) return;
    const rows = text.trim().split('\n');
    if (rows.length === 0) return;
    e.preventDefault();
    setEntries((prev) => {
      const next = [...prev];
      for (let r = 0; r < Math.min(rows.length, next.length); r++) {
        const cells = rows[r].split('\t');
        const payouts = [...next[r].payouts];
        for (let c = 0; c < Math.min(cells.length, maxOfAKind); c++) {
          payouts[c] = parseFloat(cells[c]) || 0;
        }
        next[r] = { ...next[r], payouts };
      }
      return next;
    });
  }, []);

  return (
    <div onPaste={handlePaste}>
      <div className="panel-h">
        <span className="t">Paytable</span>
        <span className="s">{entries.length} symbols × {maxOfAKind}-of-a-kind</span>
      </div>
      <div className="panel-body" style={{ padding: 0, overflowX: 'auto' }}>
        <table className="tbl">
          <thead>
            <tr>
              <th style={{ width: 70 }}>Symbol</th>
              {Array.from({ length: maxOfAKind }, (_, i) => (
                <th key={i} style={{ width: 56 }}>{i + 1}×</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {entries.map((entry, r) => (
              <tr key={entry.symbolId}>
                <td>
                  <div className="symcell">
                    <span className="symchip" style={{ background: 'var(--n-draw)' }} />
                    <span style={{ fontSize: 11 }}>{entry.symbolId}</span>
                  </div>
                </td>
                {entry.payouts.map((p, c) => (
                  <td key={c}>
                    <input
                      className="tbl-inp"
                      type="number"
                      min={0}
                      step={0.1}
                      value={p}
                      onChange={(e) => updatePayout(r, c, parseFloat(e.target.value) || 0)}
                      style={p > 0 ? { color: 'var(--exact)', fontWeight: 500 } : undefined}
                    />
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
        <div className="hint" style={{ padding: '8px 12px' }}>
          Payouts in ×bet. Paste TSV from spreadsheet to fill.
        </div>
      </div>
    </div>
  );
}
