import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAppStore } from '../../store';
import { buildConfigPayload } from '../../lib/configPayload';
import { loadProject } from '../../lib/projectFiles';
import { createDogHouseGraph, dogHouseReference } from './graph';
import { graphRequest, presentRound, downloadJson, SYMBOL_NAMES, type GameRound, type GraphRound } from './api';
import Sprite, { SymbolSprite } from './Sprite';
import GameDialog from './GameDialog';
import MathPanel from './MathPanel';
import './doghouse.css';
const money = (n: number) => n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const defaultBoard = [3, 11, 5, 9, 4, 8, 2, 4, 2, 7, 12, 6, 10, 5, 3];
const bets = [0.2, 0.4, 0.6, 0.8, 1, 1.2, 2, 4, 5, 10, 20, 50, 100];
type Dialog = 'rules' | 'math' | 'history' | 'settings' | 'autoplay' | 'bonus' | 'summary' | null;
interface DemoSession { balance: number; betIndex: number; seed: number; index: number; quick: boolean; sound: boolean; history: GameRound[] }
function readSession(): Partial<DemoSession> {
  try { const value = JSON.parse(localStorage.getItem('slotmath-dog-house-session-v1') ?? '{}');
    return { balance: Number.isFinite(value.balance) && value.balance >= 0 ? value.balance : 10000,
      betIndex: Number.isInteger(value.betIndex) && value.betIndex >= 0 && value.betIndex < bets.length ? value.betIndex : 4,
      seed: Number.isSafeInteger(value.seed) ? value.seed : 42, index: Number.isSafeInteger(value.index) && value.index >= 0 ? value.index : 0,
      quick: value.quick === true, sound: value.sound === true, history: Array.isArray(value.history) ? value.history.slice(0, 30) : [] };
  } catch { return {}; }
}
export default function DogHouse() {
  const saved = useMemo(() => readSession(), []);
  const nodes = useAppStore(s => s.nodes), edges = useAppStore(s => s.edges), tables = useAppStore(s => s.tables), name = useAppStore(s => s.configName), trail = useAppStore(s => s.graphTrail);
  const navigate = useNavigate();
  const config = useMemo(() => buildConfigPayload(nodes, edges, { name: name ?? 'Untitled', tables }), [nodes, edges, name, tables]);
  const ready = config && (config.mechanics as Record<string, unknown>)?.['dog-base-spin'] && !trail.length;
  const [dialog, setDialog] = useState<Dialog>(null), [balance, setBalance] = useState(saved.balance ?? 10000), [betIndex, setBetIndex] = useState(saved.betIndex ?? 4);
  const [seed, setSeed] = useState(saved.seed ?? 42), [index, setIndex] = useState(saved.index ?? 0), [quick, setQuick] = useState(saved.quick ?? false), [sound, setSound] = useState(saved.sound ?? false);
  const [busy, setBusy] = useState(false), [round, setRound] = useState<GameRound | null>(null), [frameIndex, setFrameIndex] = useState(0);
  const [history, setHistory] = useState<GameRound[]>(saved.history ?? []), [error, setError] = useState(''), [auto, setAuto] = useState(0), [autoCount, setAutoCount] = useState(10);
  const [hoverLine, setHoverLine] = useState<number | null>(null);
  const bet = bets[betIndex]; const frame = round?.frames[frameIndex]; const free = !!frame?.freeSpin;
  const spinning = useRef(false), abort = useRef<AbortController | null>(null), audio = useRef<AudioContext | null>(null);
  const mounted = useRef(true); const stage = useRef<HTMLDivElement>(null);
  const [settledWin, setSettledWin] = useState(0), [roundBet, setRoundBet] = useState(1);
  useEffect(() => { if (busy) return; try { localStorage.setItem('slotmath-dog-house-session-v1', JSON.stringify({ balance, betIndex, seed, index, quick, sound, history })); } catch { /* export remains available */ } }, [busy, balance, betIndex, seed, index, quick, sound, history]);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; abort.current?.abort(); void audio.current?.close(); }; }, []);
  const chime = useCallback(() => {
    if (!sound) return;
    try { const ctx = audio.current ?? new AudioContext(); audio.current = ctx; void ctx.resume();
      [392, 494, 587].forEach((frequency, i) => { const oscillator = ctx.createOscillator(), gain = ctx.createGain(); oscillator.connect(gain); gain.connect(ctx.destination);
        oscillator.frequency.value = frequency; gain.gain.setValueAtTime(0.025, ctx.currentTime + i * 0.08); gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + i * 0.08 + 0.2);
        oscillator.start(ctx.currentTime + i * 0.08); oscillator.stop(ctx.currentTime + i * 0.08 + 0.2); });
    } catch { setSound(false); }
  }, [sound]);
  const spin = useCallback(async (replay?: GameRound) => {
    if (!config || !ready || spinning.current || (dialog && !replay) || (!replay && (balance < bet || (round && frameIndex < round.frames.length - 1)))) return;
    if (!Number.isSafeInteger(seed) || !Number.isSafeInteger(index) || index < 0) { setError('Seed and round index must be safe integers.'); return; }
    spinning.current = true; setBusy(true); setError(''); setSettledWin(0); setRoundBet(replay ? 1 : bet);
    if (!replay) setBalance(v => v - bet);
    abort.current = new AbortController();
    try {
      const raw = await graphRequest<GraphRound>('play/round', { config, seed: replay?.seed ?? seed, roundIndex: replay?.roundIndex ?? index, trace: true }, abort.current.signal);
      if (!mounted.current) return;
      if (replay && raw.configHash !== replay.configHash) throw new Error('Replay graph changed. Restore the exported graph before replaying this round.');
      const result = presentRound(raw);
      if (!result.frames.length) throw new Error('Graph has no board trace. Enable trace snapshots in its subgraphs.');
      setRound(result); setFrameIndex(0);
      if (!replay) { setBalance(v => v + result.win * bet); setSettledWin(result.win * bet); setIndex(v => v + 1); setHistory(h => [result, ...h].slice(0, 30)); setAuto(v => Math.max(0, v - 1)); }
      else { setSettledWin(result.win); setAuto(0); }
      if (result.win > 0) chime();
      if (result.bonusGrid.length) { setAuto(0); setDialog('bonus'); }
    } catch (e) {
      if (mounted.current) { if (!replay) setBalance(v => v + bet); setError(e instanceof Error ? e.message : 'Graph execution failed'); setAuto(0); }
    } finally { spinning.current = false; if (mounted.current) setBusy(false); }
  }, [config, ready, dialog, balance, bet, seed, index, chime, round, frameIndex]);
  useEffect(() => {
    if (!round || dialog || busy || frameIndex >= round.frames.length - 1) return;
    const timer = setTimeout(() => { setFrameIndex(v => v + 1); chime(); if (frameIndex + 1 === round.frames.length - 1) setDialog('summary'); }, quick ? 100 : 1100);
    return () => clearTimeout(timer);
  }, [round, dialog, busy, frameIndex, quick, chime]);
  useEffect(() => {
    if (!auto || busy || dialog || (round && frameIndex < round.frames.length - 1)) return;
    if (balance < bet) return;
    const timer = setTimeout(() => void spin(), quick ? 100 : 1200); return () => clearTimeout(timer);
  }, [auto, busy, dialog, round, frameIndex, balance, bet, spin, quick]);
  useEffect(() => {
    const key = (e: KeyboardEvent) => { if (e.code === 'Space' && !dialog && !['INPUT', 'SELECT', 'TEXTAREA', 'BUTTON'].includes((e.target as HTMLElement).tagName)) { e.preventDefault(); void spin(); } };
    window.addEventListener('keydown', key); return () => window.removeEventListener('keydown', key);
  }, [spin, dialog]);
  const locked = busy || !!auto || (round && frameIndex < round.frames.length - 1) || dialog === 'bonus';
  const stopAuto = () => setAuto(0);
  if (!ready) return <div className="dh-empty"><Sprite name="s_logo" /><h1>The Dog House · constructor project</h1>
    <p>Build and save the game’s mathematical graph in the constructor, then play and verify that same graph here.</p>
    {trail.length ? <Link className="btn primary" to="/build">Save the open subgraph in Build</Link> : <button className="btn primary" onClick={() => { loadProject(createDogHouseGraph()); navigate('/build'); }}>Build Dog House in constructor</button>}
  </div>;
  return <div className="dh-page">
    <div className="dh-labbar"><div><b>The Dog House</b><span>Constructor graph · demo credits</span></div><div className="dh-actions">
      <Link to="/build" className="btn">Edit graph</Link><button className="btn" disabled={!!locked} onClick={() => { stopAuto(); setDialog('math'); }}>Verify RTP</button>
      <button className="btn" onClick={() => downloadJson('dog-house-constructor-graph.json', config)}>Export graph</button></div></div>
    <div className={'dh-stage' + (free ? ' dh-free' : '')} ref={stage}>
      <div className="dh-cabinet"><Sprite name={free ? 's_reels_fs' : 's_reels'} className="dh-cabinet-art" /><Sprite name="s_logo" className="dh-logo" />
        <div className={'dh-reels' + (busy ? ' dh-spinning' : '')} aria-label="Game reels" data-testid="game-reels">
          {(frame?.board ?? defaultBoard).map((symbol, position) => { const multiplier = frame?.multipliers[position] ?? 0; return <div key={position}
            className={'dh-cell' + (free && multiplier ? ' dh-sticky' : '')} aria-label={`Reel ${position % 5 + 1}, row ${Math.floor(position / 5) + 1}: ${SYMBOL_NAMES[symbol - 1]}${multiplier ? ` ×${multiplier}` : ''}`}>
            <SymbolSprite symbol={symbol} />{multiplier > 0 && <b className="dh-multiplier">{multiplier}×</b>}</div>; })}
          {hoverLine !== null && <svg className="dh-line" viewBox="0 0 500 300" aria-hidden="true"><polyline points={dogHouseReference.paylines[hoverLine].map((row, col) => `${50 + col * 100},${50 + row * 100}`).join(' ')} /></svg>}
        </div>
      </div>
      {free && <div className="dh-fs-banner" role="status">FREE SPIN {frameIndex} / {round!.frames.length - 1}</div>}
      <div className="dh-footer"><div className="dh-credit"><span>CREDIT <b data-testid="game-credit">{money(balance)}</b></span><span>BET <b>{money(bet)}</b></span></div>
        <div className="dh-message" aria-live="polite">{busy ? 'SPINNING…' : dialog === 'bonus' ? 'FREE SPINS WON!' : settledWin ? `WIN ${money(settledWin)}` : free ? 'STICKY WILDS!' : 'GOOD LUCK!'}</div>
        <div className="dh-spin-controls"><button aria-label="Decrease bet" disabled={!!locked || betIndex === 0} onClick={() => setBetIndex(v => v - 1)}>−</button>
          <button className="dh-spin" aria-label="Spin" disabled={!!locked || balance < bet || !!dialog} onClick={() => void spin()}><span>↻</span></button>
          <button aria-label="Increase bet" disabled={!!locked || betIndex === bets.length - 1} onClick={() => setBetIndex(v => v + 1)}>+</button></div>
      </div>
      <div className="dh-bottom-controls"><button aria-label="Game rules and paytable" onClick={() => { stopAuto(); setDialog('rules'); }}>ⓘ</button>
        <button aria-label="Game settings" onClick={() => { stopAuto(); setDialog('settings'); }}>⚙</button>
        <button aria-label="Toggle sound" aria-pressed={sound} onClick={() => setSound(v => !v)}>{sound ? '♫' : '♪'}</button>
        <button aria-label="Toggle quick spin" aria-pressed={quick} onClick={() => setQuick(v => !v)}>ϟ</button>
        <button aria-label="Round history" onClick={() => { stopAuto(); setDialog('history'); }}>History</button>
        <button aria-label={auto ? 'Stop autoplay' : 'Autoplay'} disabled={busy || (round !== null && frameIndex < round.frames.length - 1)} onClick={() => auto ? stopAuto() : setDialog('autoplay')}>{auto ? `■ Stop (${auto})` : '▶ Auto'}</button>
        <button aria-label="Toggle fullscreen" onClick={() => { if (document.fullscreenElement) void document.exitFullscreen(); else void stage.current?.requestFullscreen().catch(() => setError('Fullscreen is unavailable in this browser.')); }}>⛶</button></div>
    </div>
    {error && <p className="dh-error" role="alert">{error}</p>}
    <div className="dh-provenance"><span>Seed {seed} · next round {index}</span><span>{round ? `Graph ${round.configHash.slice(0, 16)} · payout ${round.winNumerator}/${round.winDenominator}×` : 'The saved graph calculates every payout and bonus.'}</span></div>
    {frame?.lines.length ? <details className="dh-line-wins"><summary>{frame.lines.length} winning lines · {money(frame.coins / 20)}× this spin</summary>{frame.lines.map(line => <button key={line.line} onMouseEnter={() => setHoverLine(line.line - 1)} onMouseLeave={() => setHoverLine(null)} onFocus={() => setHoverLine(line.line - 1)} onBlur={() => setHoverLine(null)}>Line {line.line}: {SYMBOL_NAMES[line.symbol - 1]} ×{line.count} · multiplier {line.multiplier} · {money(line.coins / 20)}×</button>)}</details> : null}
    {dialog === 'rules' && <GameDialog title="Rules & paytable" onClose={() => setDialog(null)} wide>
      <p>5 reels × 3 rows, 20 fixed lines, wins left to right. Longest matching combination pays. Wilds appear on reels 2–4 and substitute regular symbols. Participating Wild multipliers add; each reel’s new Wilds share ×2 or ×3. Free-spin Wild positions and their values stay locked.</p>
      <p>3 paws on reels 1, 3 and 5 award 5× the total bet and a nine-cell reveal. Each cell awards 1–3 free spins: 9–27 total. Free spins use separate strips and do not retrigger. These rules are expressed in the constructor graph.</p>
      <div className="dh-paytable">{SYMBOL_NAMES.slice(2).map((symbol, i) => <div key={symbol}><SymbolSprite symbol={i + 3} /><b>{symbol}</b><span>3: {Number((config.initialState as { linePaytable: string[] }).linePaytable[(i + 2) * 3]) / 20}×</span><span>4: {Number((config.initialState as { linePaytable: string[] }).linePaytable[(i + 2) * 3 + 1]) / 20}×</span><span>5: {Number((config.initialState as { linePaytable: string[] }).linePaytable[(i + 2) * 3 + 2]) / 20}×</span></div>)}</div>
      <div className="dh-paylines">{dogHouseReference.paylines.map((line, i) => <div key={i}><b>{i + 1}</b><svg viewBox="0 0 100 60"><polyline points={line.map((row, col) => `${10 + 20 * col},${10 + 20 * row}`).join(' ')} /></svg></div>)}</div>
      <p className="dh-model-note">Target RTP {Number((config.initialState as Record<string, unknown>).targetRtpPercent)}% per complete paid round. The constructor contains the calibrated free-spin strips and multiplier weights; Verify RTP checks the current graph. Visuals and rules follow the original public demo.</p>
      <a href={dogHouseReference.sourceUrl} target="_blank" rel="noreferrer">Provider page</a> · <a href={dogHouseReference.rulesUrl} target="_blank" rel="noreferrer">Provider rules</a>
    </GameDialog>}
    {dialog === 'math' && <GameDialog title="Constructor graph · RTP verification" onClose={() => setDialog(null)} wide><MathPanel config={config} /></GameDialog>}
    {dialog === 'settings' && <GameDialog title="Game settings" onClose={() => setDialog(null)}>
      <label>Seed<input aria-label="Game seed" type="number" value={seed} disabled={!!locked} onChange={e => setSeed(Number(e.target.value))} /></label>
      <label>Next round index<input aria-label="Game round index" type="number" min="0" value={index} disabled={!!locked} onChange={e => setIndex(Number(e.target.value))} /></label>
      <label>Bet<select aria-label="Game bet" value={betIndex} disabled={!!locked} onChange={e => setBetIndex(Number(e.target.value))}>{bets.map((b, i) => <option key={b} value={i}>{money(b)}</option>)}</select></label>
      <label><input type="checkbox" checked={quick} onChange={e => setQuick(e.target.checked)} />Quick spin</label><label><input type="checkbox" checked={sound} onChange={e => setSound(e.target.checked)} />Sound</label>
      <p>Seed + round index + graph hash reproduce a round. Replay does not change demo credits. Monte Carlo uses its own deterministic chunk streams.</p>
      <button onClick={() => setBalance(10000)} disabled={!!locked}>Reset demo credit</button>
    </GameDialog>}
    {dialog === 'history' && <GameDialog title="Round history" onClose={() => setDialog(null)} wide>
      {history.length ? <table className="dh-math-table"><thead><tr><th>Seed / round</th><th>Win ×</th><th>Free spins</th><th /></tr></thead><tbody>{history.map((h, i) => <tr key={i}><td>{h.seed} / {h.roundIndex}</td><td>{money(h.win)}</td><td>{h.frames.length - 1}</td><td><button disabled={busy} onClick={() => { setDialog(null); void spin(h); }}>Replay</button></td></tr>)}</tbody></table> : <p>No rounds yet.</p>}
      <button disabled={!history.length} onClick={() => downloadJson('dog-house-rounds.json', { config, rounds: history })}>Export rounds & graph</button>
    </GameDialog>}
    {dialog === 'autoplay' && <GameDialog title="Autoplay" onClose={() => setDialog(null)}><label>Paid rounds<select aria-label="Autoplay rounds" value={autoCount} onChange={e => setAutoCount(Number(e.target.value))}>{[5, 10, 25, 50].map(n => <option key={n}>{n}</option>)}</select></label><p>Stops at a bonus, an error or insufficient credit.</p><button className="dh-primary" onClick={() => { setAuto(autoCount); setDialog(null); }}>Start autoplay</button></GameDialog>}
    {dialog === 'bonus' && round && <GameDialog title="Free spins won!" onClose={() => { setDialog(null); setFrameIndex(1); }}><div className="dh-bonus-grid">{round.bonusGrid.map((n, i) => <div key={i}><span>🐾</span><b>{n}</b></div>)}</div><h3>{round.frames.length - 1} FREE SPINS</h3><p>Wilds and their multipliers stay locked for the bonus.</p><button className="dh-primary" onClick={() => { setDialog(null); setFrameIndex(1); }}>Start free spins</button></GameDialog>}
    {dialog === 'summary' && round && <GameDialog title="Bonus complete" onClose={() => setDialog(null)}><Sprite name="s_logo" /><h3>WIN {money(round.win * roundBet)}</h3><p>{round.frames.length - 1} free spins · {money(round.win)}× total paid-round win</p><button className="dh-primary" onClick={() => setDialog(null)}>Continue</button></GameDialog>}
  </div>;
}
