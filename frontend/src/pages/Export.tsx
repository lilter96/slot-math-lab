import { useState, useCallback, useRef, useMemo } from 'react';
import { useAppStore } from '../store';
import { configHash } from '../lib/hash';
import { Ic } from '../components/Icons';
import ProvBadge from '../components/ProvBadge';

// ── Build export payload from current store state ─────────────────
function buildExportPayload(nodes: ReturnType<typeof useAppStore.getState>['nodes'], edges: ReturnType<typeof useAppStore.getState>['edges'], symbols: ReturnType<typeof useAppStore.getState>['tableSymbols'], mechanics: ReturnType<typeof useAppStore.getState>['mechanics']) {
  return {
    schemaVersion: '1.0.0',
    name: 'Slot Math Lab Export',
    exportedAt: new Date().toISOString(),
    symbols: symbols.map((s) => ({ id: s.id, name: s.name, kind: s.kind, color: s.color })),
    nodes: nodes.map((n) => ({
      id: n.id,
      type: n.type,
      position: n.position,
      data: n.data,
    })),
    edges: edges.map((e) => ({
      id: e.id,
      source: e.source,
      target: e.target,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle,
    })),
    mechanics: mechanics.map((m) => ({
      id: m.id,
      name: m.name,
      description: m.description,
      nodes: m.nodes,
      edges: m.edges,
    })),
  };
}

// ── PAR sheet helpers ─────────────────────────────────────────────
function generateParCsv(symbols: { id: string; name: string; kind: string }[], metrics?: { rtp?: number; hitFreq?: number; volatility?: number }): string {
  const header = 'Section,Item,Value,Provenance';
  const rows: string[] = [header];

  rows.push(`Game Info,Name,Slot Math Lab Export,Exact`);
  rows.push(`Game Info,Date,${new Date().toISOString().split('T')[0]},Exact`);
  rows.push('');
  rows.push('Symbols,,,,');
  for (const s of symbols) {
    rows.push(`Symbol,${s.id},${s.name} (${s.kind}),—`);
  }
  rows.push('');
  rows.push('Paytable,,,,');
  rows.push(`Paytable,5-of-a-kind,,—`);
  rows.push('');
  rows.push('Metrics,,,,');
  if (metrics) {
    rows.push(`Metrics,RTP,${metrics.rtp != null ? (metrics.rtp * 100).toFixed(2) + '%' : '—'},${metrics.rtp != null ? 'Exact' : '—'}`);
    rows.push(`Metrics,Hit Frequency,${metrics.hitFreq != null ? (metrics.hitFreq * 100).toFixed(2) + '%' : '—'},${metrics.hitFreq != null ? 'Exact' : '—'}`);
    rows.push(`Metrics,Volatility Index,${metrics.volatility != null ? metrics.volatility.toFixed(2) : '—'},${metrics.volatility != null ? 'Exact' : '—'}`);
  }
  rows.push('');
  rows.push('Provenance Legend,,,,');
  rows.push('Legend,Exact,Rational closed-form,');
  rows.push('Legend,ExactWithinEpsilon,ε-pruned with bound,');
  rows.push('Legend,Sampled,Monte Carlo with n and CI,');

  return rows.join('\n');
}

function generateParHtml(symbols: { id: string; name: string; kind: string; color: string }[], hash: string, metrics?: { rtp?: number; hitFreq?: number; volatility?: number }): string {
  return `<!DOCTYPE html>
<html lang="en">
<head><meta charset="UTF-8"><title>PAR Sheet — Slot Math Lab</title>
<style>
  body { font-family: 'IBM Plex Sans', system-ui, sans-serif; background: #1a1b1e; color: #e9ecef; max-width: 800px; margin: 0 auto; padding: 32px; }
  h1 { font-size: 22px; font-weight: 600; letter-spacing: -0.01em; }
  h2 { font-size: 13px; text-transform: uppercase; letter-spacing: .06em; color: #909296; margin: 24px 0 12px; }
  .grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 1px; background: #373a40; border: 1px solid #373a40; border-radius: 12px; overflow: hidden; margin-bottom: 22px; }
  .cell { background: #25262b; padding: 14px 16px; }
  .cell .l { font-size: 10.5px; text-transform: uppercase; letter-spacing: .07em; color: #909296; margin-bottom: 6px; }
  .cell .v { font-family: 'IBM Plex Mono', monospace; font-size: 20px; font-weight: 500; }
  .prov { display: inline-flex; align-items: center; gap: 5px; padding: 2px 7px; border-radius: 20px; font-family: 'IBM Plex Mono', monospace; font-size: 10px; font-weight: 500; }
  .prov.exact { background: rgba(70,211,154,0.16); color: #46d39a; border: 1px solid rgba(70,211,154,0.3); }
  .prov.sampled { background: rgba(116,143,252,0.16); color: #748ffc; border: 1px solid rgba(116,143,252,0.3); }
  table { width: 100%; border-collapse: collapse; font-size: 12px; }
  th { text-align: left; color: #909296; font-weight: 500; padding: 8px 12px; border-bottom: 1px solid #373a40; font-size: 11px; text-transform: uppercase; letter-spacing: .04em; }
  td { padding: 9px 12px; border-bottom: 1px solid rgba(55,58,64,0.5); font-family: 'IBM Plex Mono', monospace; }
  td:first-child, th:first-child { font-family: 'IBM Plex Sans', system-ui, sans-serif; }
  .chip { display: inline-block; width: 12px; height: 12px; border-radius: 3px; margin-right: 6px; vertical-align: middle; }
  .hash { font-family: 'IBM Plex Mono', monospace; font-size: 10px; color: #909296; word-break: break-all; }
  footer { margin-top: 40px; padding-top: 16px; border-top: 1px solid #373a40; font-size: 11px; color: #909296; }
  @media print { body { background: white; color: black; } .grid { background: #ddd; border-color: #ddd; } .cell { background: #f8f9fa; } }
</style></head>
<body>
  <h1>PAR Sheet — Slot Math Lab</h1>
  <div class="hash">Config hash: ${hash}</div>

  <h2>Key Metrics</h2>
  <div class="grid">
    <div class="cell"><div class="l">RTP <span class="prov exact">Exact</span></div><div class="v">${metrics?.rtp != null ? (metrics.rtp * 100).toFixed(2) + '%' : '—'}</div></div>
    <div class="cell"><div class="l">Hit Frequency <span class="prov exact">Exact</span></div><div class="v">${metrics?.hitFreq != null ? (metrics.hitFreq * 100).toFixed(2) + '%' : '—'}</div></div>
    <div class="cell"><div class="l">Volatility <span class="prov exact">Exact</span></div><div class="v">${metrics?.volatility != null ? metrics.volatility.toFixed(2) + 'σ' : '—'}</div></div>
    <div class="cell"><div class="l">Max Win</div><div class="v">—</div></div>
  </div>

  <h2>Symbols</h2>
  <table><thead><tr><th>ID</th><th>Name</th><th>Kind</th></tr></thead><tbody>
    ${symbols.map((s) => `<tr><td><span class="chip" style="background:${s.color}"></span>${s.id}</td><td>${s.name}</td><td>${s.kind}</td></tr>`).join('')}
  </tbody></table>

  <footer>
    <p>Generated by Slot Math Lab · Provenance: all displayed values are <strong>Exact</strong> (rational closed-form) unless otherwise noted.</p>
  </footer>
</body></html>`;
}

// ── Component ──────────────────────────────────────────────────────
export default function Export() {
  const nodes = useAppStore((s) => s.nodes);
  const edges = useAppStore((s) => s.edges);
  const symbols = useAppStore((s) => s.tableSymbols);
  const mechanics = useAppStore((s) => s.mechanics);
  const [imported, setImported] = useState<Record<string, unknown> | null>(null);
  const [importError, setImportError] = useState<string | null>(null);
  const [importHash, setImportHash] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [shareUrl, setShareUrl] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  const exportPayload = useMemo(() => buildExportPayload(nodes, edges, symbols, mechanics), [nodes, edges, symbols, mechanics]);
  const exportHash = useMemo(() => configHash(exportPayload), [exportPayload]);
  const exportJson = useMemo(() => JSON.stringify(exportPayload, null, 2), [exportPayload]);

  // ── Download JSON ──────────────────────────────────────────────
  const handleDownload = useCallback(() => {
    const blob = new Blob([exportJson], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `slot-math-lab-${exportHash}.json`;
    a.click();
    URL.revokeObjectURL(url);
  }, [exportJson, exportHash]);

  // ── Copy to clipboard ──────────────────────────────────────────
  const handleCopy = useCallback(async () => {
    await navigator.clipboard.writeText(exportJson);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }, [exportJson]);

  // ── Shareable link ─────────────────────────────────────────────
  const handleShareLink = useCallback(() => {
    const encoded = btoa(encodeURIComponent(exportJson));
    const url = `${window.location.origin}/build?load=${encoded}`;
    setShareUrl(url);
    navigator.clipboard.writeText(url).catch(() => {});
  }, [exportJson]);

  // ── Import JSON ────────────────────────────────────────────────
  const handleImport = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      try {
        const data = JSON.parse(reader.result as string);
        if (!data.schemaVersion) {
          setImportError('Invalid config: missing schemaVersion');
          setImported(null);
          return;
        }
        setImported(data);
        setImportError(null);
        setImportHash(configHash(data));
      } catch {
        setImportError('Invalid JSON file');
        setImported(null);
        setImportHash(null);
      }
    };
    reader.readAsText(file);
    e.target.value = '';
  }, []);

  // ── Verify round-trip ──────────────────────────────────────────
  const handleVerifyRoundTrip = useCallback(() => {
    if (!imported) return;
    const importedHash = configHash(imported);
    // Re-export and compare
    const reExported = JSON.stringify(imported, Object.keys(imported as object).sort());
    const reImported = JSON.parse(reExported);
    const roundTripHash = configHash(reImported);
    setImportHash(roundTripHash);
    if (roundTripHash === importedHash) {
      setImportError(null);
    } else {
      setImportError(`Hash mismatch: original=${importedHash}, round-trip=${roundTripHash}`);
    }
  }, [imported]);

  // ── PAR sheet downloads ────────────────────────────────────────
  const handleParCsv = useCallback(() => {
    const csv = generateParCsv(symbols);
    const blob = new Blob([csv], { type: 'text/csv' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `par-sheet-${exportHash}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }, [symbols, exportHash]);

  const handleParHtml = useCallback(() => {
    const html = generateParHtml(symbols, exportHash);
    const blob = new Blob([html], { type: 'text/html' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `par-sheet-${exportHash}.html`;
    a.click();
    URL.revokeObjectURL(url);
  }, [symbols, exportHash]);

  const hashesMatch = importHash && exportHash === importHash;

  return (
    <div className="workspace" style={{ overflow: 'auto' }}>
      <div className="doc">
        <div className="doc-inner">
          {/* ── Header ── */}
          <div className="par-h">
            <h1>Export / Import</h1>
            <ProvBadge p={{ kind: 'Exact' }} />
          </div>
          <div className="par-meta">
            Config hash: <span className="mono" style={{ color: 'var(--exact)' }}>{exportHash}</span>
            {' · '}{nodes.length} nodes · {edges.length} edges · {symbols.length} symbols · {mechanics.length} mechanics
          </div>

          {/* ── Export section ── */}
          <div className="par-section">
            <h2>Export</h2>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
              <button className="btn primary" onClick={handleDownload}>
                <Ic.export style={{ width: 14, height: 14 }} /> Download JSON
              </button>
              <button className="btn" onClick={handleCopy}>
                <Ic.copy style={{ width: 14, height: 14 }} /> {copied ? 'Copied!' : 'Copy to clipboard'}
              </button>
              <button className="btn" onClick={handleShareLink}>
                <Ic.target style={{ width: 14, height: 14 }} /> Shareable link
              </button>
            </div>
            {shareUrl && (
              <div style={{ marginTop: 10, padding: '8px 12px', background: 'var(--bg-2)', border: '1px solid var(--line)', borderRadius: 7 }}>
                <div className="mono" style={{ fontSize: 10, color: 'var(--faint)', wordBreak: 'break-all' }}>{shareUrl}</div>
                <div className="hint">Link copied to clipboard. Share it to reopen this exact graph.</div>
              </div>
            )}
          </div>

          {/* ── Import section ── */}
          <div className="par-section">
            <h2>Import</h2>
            <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
              <input
                ref={fileRef}
                type="file"
                accept=".json"
                onChange={handleImport}
                style={{ display: 'none' }}
              />
              <button className="btn" onClick={() => fileRef.current?.click()}>
                <Ic.plus style={{ width: 14, height: 14 }} /> Load JSON file
              </button>
              {imported && (
                <button className="btn" onClick={handleVerifyRoundTrip}>
                  <Ic.check style={{ width: 14, height: 14 }} /> Verify round-trip
                </button>
              )}
            </div>
            {importError && (
              <div style={{ marginTop: 8, padding: '8px 12px', background: 'var(--danger-dim)', border: '1px solid var(--danger)', borderRadius: 6, color: 'var(--danger)', fontSize: 12, fontFamily: 'var(--mono)' }}>
                {importError}
              </div>
            )}
            {importHash && !importError && (
              <div style={{ marginTop: 8, display: 'flex', alignItems: 'center', gap: 12 }}>
                <div className="mono" style={{ fontSize: 11, color: 'var(--muted)' }}>
                  Import hash: <span style={{ color: hashesMatch ? 'var(--exact)' : 'var(--epsilon)' }}>{importHash}</span>
                </div>
                {hashesMatch && (
                  <span style={{ color: 'var(--exact)', fontSize: 12, fontFamily: 'var(--mono)' }}>
                    ✅ Round-trip verified — hashes match
                  </span>
                )}
                {importHash && !hashesMatch && (
                  <span style={{ color: 'var(--epsilon)', fontSize: 12, fontFamily: 'var(--mono)' }}>
                    Hashes differ — different configs
                  </span>
                )}
              </div>
            )}
          </div>

          {/* ── PAR sheet export ── */}
          <div className="par-section">
            <h2>PAR Sheet Export</h2>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
              <button className="btn" onClick={handleParCsv}>
                <Ic.export style={{ width: 14, height: 14 }} /> Download CSV
              </button>
              <button className="btn" onClick={handleParHtml}>
                <Ic.results style={{ width: 14, height: 14 }} /> Download printable summary (HTML)
              </button>
            </div>
          </div>

          {/* ── JSON preview ── */}
          <div className="par-section">
            <h2>JSON Preview</h2>
            <div className="code-block">
              {exportJson.substring(0, 3000)}
              {exportJson.length > 3000 && '\n… (truncated)'}
            </div>
          </div>

          {/* ── Metric breakdown ── */}
          <div className="par-section">
            <h2>Metric Breakdown</h2>
            <div className="par-grid">
              <div className="par-cell">
                <div className="pl">Symbols <ProvBadge p={{ kind: 'Exact' }} /></div>
                <div className="pv">{symbols.length}</div>
              </div>
              <div className="par-cell">
                <div className="pl">Nodes <ProvBadge p={{ kind: 'Exact' }} /></div>
                <div className="pv">{nodes.length}</div>
              </div>
              <div className="par-cell">
                <div className="pl">Edges <ProvBadge p={{ kind: 'Exact' }} /></div>
                <div className="pv">{edges.length}</div>
              </div>
              <div className="par-cell">
                <div className="pl">Mechanics <ProvBadge p={{ kind: 'Exact' }} /></div>
                <div className="pv">{mechanics.length}</div>
              </div>
            </div>
          </div>

          {/* ── Symbols table ── */}
          <div className="par-section">
            <h2>Symbols</h2>
            <table className="par-table">
              <thead>
                <tr>
                  <th>Symbol</th>
                  <th>ID</th>
                  <th>Name</th>
                  <th>Kind</th>
                </tr>
              </thead>
              <tbody>
                {symbols.map((s) => (
                  <tr key={s.id}>
                    <td><span className="sym-chip"><span className="sw" style={{ background: s.color }} /></span></td>
                    <td>{s.id}</td>
                    <td>{s.name}</td>
                    <td>{s.kind}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  );
}
