import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { switchProject } from '../../lib/projectFiles';
import { createNineLivesGraph } from './graph';
import type { launchNineLives } from './launch';

type Game = Awaited<ReturnType<typeof launchNineLives>>;
interface Replay { configHash: string; seed: number; index: number; buy: boolean }
export default function NineLives() {
  const host = useRef<HTMLDivElement>(null);
  const game = useRef<Game | null>(null);
  const [hash, setHash] = useState('');
  const [seed, setSeed] = useState(42);
  const [index, setIndex] = useState(0);
  const [buy, setBuy] = useState(false);
  const [error, setError] = useState('');
  const [ready, setReady] = useState(false);
  const navigate = useNavigate();
  useEffect(() => {
    let disposed = false;
    const controller = new AbortController();
    const element = host.current!;
    const record = (event: Event) => {
      const replay = (event as CustomEvent<Replay>).detail;
      if (!replay) return;
      setHash(replay.configHash); setSeed(replay.seed); setIndex(replay.index); setBuy(replay.buy);
    };
    element.addEventListener('nine-lives:round', record);
    void import('./launch').then(async ({ launchNineLives }) => {
      if (disposed) return;
      const mounted = await launchNineLives(element, controller.signal);
      if (disposed) mounted.dispose();
      else { game.current = mounted; setReady(true); }
    }).catch(reason => { if (!disposed) setError(reason instanceof Error ? reason.message : String(reason)); });
    return () => {
      disposed = true;
      controller.abort();
      element.removeEventListener('nine-lives:round', record);
      game.current?.dispose();
      game.current = null;
    };
  }, []);
  return <section className="nl-play">
    <p className="hint">Nine Lives reference demo · this demo uses its own authored model. Test your current project in Debug.</p>
    <div className="nl-graph-tools">
      <button className="btn" onClick={() => { switchProject(createNineLivesGraph(buy ? 'buy' : 'base')); navigate('/build'); }}>Open Nine Lives graph</button>
      <label>Config hash<input aria-label="Replay config hash" value={hash} onChange={event => setHash(event.target.value)} /></label>
      <label>Seed<input aria-label="Replay seed" type="number" value={seed} onChange={event => setSeed(Number(event.target.value))} /></label>
      <label>Index<input aria-label="Replay index" type="number" value={index} onChange={event => setIndex(Number(event.target.value))} /></label>
      <label><input type="checkbox" checked={buy} onChange={event => setBuy(event.target.checked)} />Bonus buy</label>
      <button className="btn" disabled={!hash || !ready} onClick={() => { setError(''); void game.current?.replay(hash, seed, index, buy).catch(reason => setError(reason instanceof Error ? reason.message : String(reason))); }}>Replay round</button>
    </div>
    {error && <p role="alert">{error}</p>}
    <div className="nl-game-host" ref={host} />
  </section>;
}
