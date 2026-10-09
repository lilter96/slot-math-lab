import { useQueryClient } from '@tanstack/react-query';
import type { RunEvidence } from '../../lib/results/model';
import { downloadReport } from '../../lib/results/export';

export function DiagnosticEvidence({ evidence }: { evidence: RunEvidence }) {
  const client = useQueryClient(); const artifacts = evidence.diagnostics ?? [];
  return <section className="simulation-card" aria-label="Retained diagnostic evidence"><div className="simulation-card-head"><div><h2>Retained diagnostic evidence</h2><p>Server-calculated references, calibrated discrepancies and reconstructed witnesses stay with this run. Independently authored references establish their supplied model; their association with a run does not establish equivalence to its graph.</p></div><button type="button" className="btn" onClick={() => void client.invalidateQueries({ queryKey: ['run-evidence', evidence.run.id] })}>Refresh diagnostics</button></div>
    {!artifacts.length && <p>No retained calculations yet. Enumerate the pinned graph, calibrate a tracked PMF or reconstruct a witness. The independent workbench in Simulate can retain an authored reference for the selected run.</p>}
    {artifacts.map(a => <details key={a.id}><summary>{a.kind} · {new Date(a.createdAt).toLocaleString()} · {a.id.slice(0, 12)}</summary><dl><dt>Input SHA-256</dt><dd className="measurement-fingerprint"><code>{a.inputSha256}</code></dd><dt>Output SHA-256</dt><dd className="measurement-fingerprint"><code>{a.outputSha256}</code></dd><dt>Core SHA-256</dt><dd className="measurement-fingerprint"><code>{a.runtimeProvenance.coreBinarySha256 ?? 'Unavailable'}</code></dd></dl><details><summary>Authored input</summary><pre>{JSON.stringify(a.input, null, 2)}</pre></details><details><summary>Calculated result</summary><pre>{JSON.stringify(a.output, null, 2)}</pre></details><button type="button" className="btn" onClick={() => downloadReport(`run-${evidence.run.id}-${a.kind}-${a.id.slice(0, 12)}.json`, JSON.stringify(a, null, 2), 'application/json')}>Download retained calculation</button></details>)}
    <small>Up to 16 calculations / 2 MB per run. Identical results are deduplicated. A full budget preserves existing evidence and leaves the new result available for export. Diagnostic payloads are excluded from live WebSocket frames.</small>
  </section>;
}
