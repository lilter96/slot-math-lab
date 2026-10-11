import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useModelProjection } from '../hooks/useModelProjection';
import { graphRequest, downloadJson, type GraphRound } from '../lib/graphApi';
import type { ModelGraph } from '../lib/modelProjection';
import { canonicalJson } from '../lib/hash';
import { cascadeTrace, boardCells } from '../lib/roundTrace';
import { assets } from '../games/nine-lives/manifest';
interface Receipt { config: ModelGraph; round: GraphRound }
let savedReceipt: Receipt | null = null;
export default function Debug() {
  const graph = useModelProjection();
  const [seed, setSeed] = useState('42'), [index, setIndex] = useState('0');
  const [receipt, setReceipt] = useState<Receipt | null>(savedReceipt);
  const [busy, setBusy] = useState(false), [error, setError] = useState(''), [replay, setReplay] = useState('');
  const [stepIndex, setStepIndex] = useState(0), [hitIndex, setHitIndex] = useState<number | null>(null), [after, setAfter] = useState(false);
  const config = receipt?.config;
  const geometry = config?.boardConfig as { rows?: number; columns?: number } | undefined;
  const rows = geometry?.rows ?? 0, columns = geometry?.columns ?? 0;
  const size = Number.isInteger(rows) && Number.isInteger(columns) && rows > 0 && columns > 0 && rows * columns <= 120 ? rows * columns : 0;
  const steps = receipt && size ? cascadeTrace(receipt.round, size) : null;
  const step = steps?.[stepIndex];
  const initial = receipt && size ? boardCells((receipt.round.state.boards as unknown[] | undefined)?.[0], size) : null;
  const cells = step ? after ? step.after : step.before : initial;
  const symbols = (config?.symbols ?? []) as { id: string; name: string }[];
  const nineLives = String(config?.id ?? '').startsWith('nine-lives');
  const changed = receipt && graph && canonicalJson(receipt.config) !== canonicalJson(graph);
  const execute = async (isReplay: boolean) => {
    setBusy(true); setError(''); setReplay('');
    try {
      const seedValue = isReplay ? receipt!.round.seed : Number(seed), roundIndex = isReplay ? receipt!.round.roundIndex : Number(index);
      if ((!isReplay && (!/^-?\d+$/.test(seed) || !/^\d+$/.test(index))) || !Number.isSafeInteger(seedValue) || !Number.isSafeInteger(roundIndex) || roundIndex < 0) throw new Error('Use a safe integer seed and a non-negative safe integer round index.');
      const pinned = structuredClone(isReplay ? receipt!.config : graph);
      if (!pinned) throw new Error('Open a model before running a round.');
      const round = await graphRequest<GraphRound>('play/round', { config: pinned, seed: seedValue, roundIndex, trace: true });
      if (isReplay) setReplay(canonicalJson(round) === canonicalJson(receipt!.round) ? 'Replay matches the stored payout, hash and full returned state.' : 'Replay differs from the stored response. Inspect both receipts; no determinism claim is made.');
      else { const next = { config: pinned, round }; savedReceipt = next; setReceipt(next); setStepIndex(0); setHitIndex(null); setAfter(false); }
    } catch (reason) { setError(reason instanceof Error ? reason.message : String(reason)); } finally { setBusy(false); }
  };
  return <section className="debug-page"><header className="page-heading"><div><h1>Spin & trace</h1><p>Execute the current model and inspect the state returned by the math engine.</p></div><Link className="btn" to="/build">Open current logic</Link></header>
    <form className="debug-inputs" onSubmit={event => { event.preventDefault(); void execute(false); }}><label>Seed<input className="inp" aria-label="Debug seed" value={seed} onChange={event => setSeed(event.target.value)} disabled={busy} /></label><label>Round index<input className="inp" aria-label="Debug round index" value={index} onChange={event => setIndex(event.target.value)} disabled={busy} /></label><button className="btn primary" disabled={busy || !graph}>{busy ? 'Executing…' : 'Run current model'}</button>{receipt && <button type="button" className="btn" disabled={busy} onClick={() => void execute(true)}>Replay pinned round</button>}</form>
    {error && <p role="alert" className="inline-validation">{error}</p>}{replay && <p role="status">{replay}</p>}
    {!receipt ? <p className="model-notice">No round executed in this session. A test spin is one observation; it does not measure RTP.</p> : <>
      <div className="round-receipt"><h2>Executed round</h2><dl><div><dt>Round payout</dt><dd>{receipt.round.winNumerator} / {receipt.round.winDenominator} model win units</dd></div><div><dt>Inputs</dt><dd>Seed {receipt.round.seed} · index {receipt.round.roundIndex}</dd></div><div><dt>Config hash</dt><dd className="mono">{receipt.round.configHash}</dd></div></dl><button className="btn" onClick={() => downloadJson('slotmath-round-receipt.json', receipt)}>Download pinned receipt</button>{changed && <p className="model-notice">The current draft differs from this receipt. Replay uses the pinned configuration.</p>}</div>
      <div className="trace-workspace"><div className="trace-stage"><h2>Returned board</h2>{cells ? <><p className="hint">{columns} reels × {rows} rows · {step ? `spin ${step.spin + 1}, cascade ${stepIndex + 1}` : 'first returned spin'} · {after ? 'after refill' : 'before cascade'}</p><div className="trace-board" aria-label="Returned trace board" style={{ gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))` }}>{cells.map((id, position) => { const symbol = symbols.find(item => item.id === id); const name = symbol?.name ?? id; return <div key={position} className={step && hitIndex != null && step.hits[hitIndex]?.positions.includes(position) ? 'trace-cell hit' : 'trace-cell'} title={`Cell ${position}: ${name}`} data-position={position}>{nineLives && assets.images?.[name] ? <img src={assets.images[name]} alt={name} /> : <span>{name}</span>}<small>{position}</small></div>; })}</div>{step && <button className="btn" onClick={() => { setAfter(value => !value); setHitIndex(null); }}>Show {after ? 'before cascade' : 'after refill'}</button>}</> : <p>No compatible board history in the returned state.</p>}</div>
        <aside className="trace-rail" aria-label="Cascade trace"><h2>Cascade history</h2>{steps?.length ? <><label>Recorded cascade<select className="inp" aria-label="Recorded cascade" value={stepIndex} onChange={event => { setStepIndex(Number(event.target.value)); setHitIndex(null); setAfter(false); }}>{steps.map((item, i) => <option key={i} value={i}>Spin {item.spin + 1} · cascade record {i + 1}</option>)}</select></label>{step && <><p>Multiplier {step.multiplier} · win {step.win} authored coin units</p><p className="hint">Hit totals below include the recorded multiplier. Board indices are row-major.</p>{step.hits.map((hit, i) => <button className="trace-hit" key={i} aria-pressed={hitIndex === i} onClick={() => { setHitIndex(hitIndex === i ? null : i); setAfter(false); }}><strong>{symbols.find(symbol => symbol.id === hit.symbol)?.name ?? hit.symbol} · {hit.size} cells</strong><span>{hit.payout} authored coin units</span><small>Highlight {hit.positions.length} recorded positions</small></button>)}</>}</> : <p>{steps ? 'No cascades were recorded in this round.' : 'Compatible cascade histories are not available. Inspect the raw state below.'}</p>}<p className="hint">Event-to-node mapping: Not available. This API returns authored histories without source-node IDs, timing or RNG stream records.</p></aside>
      </div><p className="hint">Replay compares an actual response using the pinned config, seed and index on the current server. Engine version is not returned; matching across versions is not certified.</p><details className="trace-raw"><summary>Full returned state</summary><pre>{JSON.stringify(receipt.round.state, null, 2)}</pre></details>
    </>}
  </section>;
}
