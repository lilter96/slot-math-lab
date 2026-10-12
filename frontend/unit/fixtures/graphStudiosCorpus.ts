import { gameFixtures } from '../../src/games/fixtures';
import { createAncientDragonGraph } from '../../src/games/ancient-dragon/graph';
import { createWoodlandWhisperGraph } from '../../src/games/woodland-whisper/graph';
import { createX7ClubGraph } from '../../src/games/x7-club/graph';
import { createNineLivesGraph as createTgslotsGraph } from '../../src/games/nine-lives-tgslots/graph';
import { createNineLivesGraph as createReferenceGraph } from '../../src/games/nine-lives/graph';
import { createLeMilitareGraph, leMilitareModel, leMilitareFeatures } from '../../src/games/le-militare/graph';
import { createClusterStarter } from '../../src/lib/starterLibrary';
import type { ModelGraph } from '../../src/lib/modelProjection';
export interface CorpusGraph { caseId: string; graph: ModelGraph }

/** Verification corpus only: factories are never a replacement for an edited project. */
export function graphStudiosCorpus(): { graphs: CorpusGraph[]; nonGraphs: string[] } {
  const graphs: CorpusGraph[] = [], nonGraphs: string[] = [];
  const add = (caseId: string, value: Record<string, unknown>) => {
    if (!Array.isArray(value.nodes) || !Array.isArray(value.edges)) { nonGraphs.push(caseId); return; }
    graphs.push({ caseId, graph: value as ModelGraph });
  };
  add('ancient-dragon/ordinary', createAncientDragonGraph());
  for (const buy of [false, true]) {
    const entry = buy ? 'bought' : 'ordinary';
    add(`woodland-whisper/${entry}`, createWoodlandWhisperGraph(undefined, { buy }));
    add(`x7-club/${entry}`, createX7ClubGraph(undefined, { buy }));
    add(`nine-lives-tgslots/${entry}`, createTgslotsGraph(undefined, { buy }));
  }
  for (const mode of Object.keys(leMilitareModel.modes)) for (const feature of leMilitareFeatures) {
    add(`le-militare/${mode}/${feature}`, createLeMilitareGraph(leMilitareModel, { mode, feature }));
  }
  add('nine-lives/reference', createReferenceGraph());
  add('starter/cluster', createClusterStarter()); add('starter/cascade', createClusterStarter(true));
  for (const [name, create] of Object.entries(gameFixtures)) add(`fixture/${name}`, create());
  return { graphs, nonGraphs };
}
