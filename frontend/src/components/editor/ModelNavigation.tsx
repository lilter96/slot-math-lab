import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAppStore } from '../../store';
import { loadProject, openMechanic, rootProject } from '../../lib/projectFiles';
import { createExpectationGraph } from '../../games/doghouse/expectationGraph';
import ProjectDataEditor from './ProjectDataEditor';

export default function ModelNavigation() {
  const tables = useAppStore(s => s.tables), trail = useAppStore(s => s.graphTrail);
  const verificationSource = useAppStore(s => s.verificationSource);
  const activeNodeCount = useAppStore(s => s.nodes.length);
  const [showData, setShowData] = useState(false), [error, setError] = useState('');
  const navigate = useNavigate();
  const proof = tables.uiAnalysisKind === 'expectationProof';
  const source = proof ? verificationSource : trail[0]?.graph;
  const mechanics = (source?.mechanics ?? tables.mechanics) as Record<string, { nodes: unknown[] }> | undefined;
  if (!mechanics?.['dog-base-spin']) return null;
  const rootNodeCount = Array.isArray(source?.nodes) ? source.nodes.length : activeNodeCount;
  const totalNodeCount = rootNodeCount + Object.values(mechanics).reduce((sum, graph) => sum + graph.nodes.length, 0);
  const target = Number(((source?.initialState ?? tables.initialState) as Record<string, unknown>)?.targetRtpPercent ?? 98);
  const openGame = () => {
    if (proof && verificationSource) {
      loadProject(verificationSource); useAppStore.setState({ verificationSource: null, graphTrail: [] });
    } else rootProject();
  };
  const open = (name: string) => { openGame(); openMechanic(name); setError(''); };
  return <>
    <div className="model-navigation" data-testid="model-navigation">
      <div className="model-navigation-heading"><b>The Dog House · target {target}%</b><span>{proof ? 'Exact RTP & calibration graph' : trail.length ? trail.map(x => x.mechanic).join(' / ') : `Complete game model · ${totalNodeCount} nodes in ${Object.keys(mechanics).length + 1} editable graphs`}</span></div>
      <div className="model-navigation-actions" aria-label="Game model graphs">
        <button className="btn" aria-label="Open full game graph" onClick={openGame}>Full game</button>
        <button className="btn" aria-label="Open base game graph" onClick={() => open('dog-base-spin')}>Base reels · {mechanics['dog-base-spin'].nodes.length} nodes</button>
        <button className="btn" aria-label="Open payline graph" onClick={() => open('dog-line')}>Paylines & payouts · {mechanics['dog-line'].nodes.length} nodes</button>
        <button className="btn" aria-label="Open free spins graph" onClick={() => open('dog-free-spin')}>Free spins & Sticky Wilds · {mechanics['dog-free-spin'].nodes.length} nodes</button>
        <button className="btn" aria-label="Open reel and payout tables" onClick={() => { openGame(); setShowData(true); }}>Reels / paytable</button>
        <button className="btn" aria-label="Open exact RTP and calibration graph" disabled={proof} onClick={() => {
          try { openGame(); const game = rootProject(); if (!game) return; const reference = createExpectationGraph(game);
            useAppStore.setState({ verificationSource: game, graphTrail: [] }); loadProject(reference); useAppStore.getState().selectNode('rtp-total'); setError('');
          } catch (e) { setError(e instanceof Error ? e.message : 'Cannot construct the expectation proof'); }
        }}>Exact RTP / calibration</button>
        <button className="btn primary" onClick={() => { openGame(); navigate('/play'); }}>Play this model</button>
      </div>
      {error && <p role="alert">{error}</p>}
    </div>
    {showData && <ProjectDataEditor initialTable="linePaytable" onClose={() => setShowData(false)} />}
  </>;
}
