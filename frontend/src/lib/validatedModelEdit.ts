import { useAppStore } from '../store';
import { graphRequest } from './graphApi';
import { canonicalJson } from './hash';
import { peekRootProject, rootProject, loadProject } from './projectFiles';
import type { ModelGraph } from './modelProjection';
export async function applyValidatedModel(base: ModelGraph, next: ModelGraph): Promise<ModelGraph> {
  const projectId = useAppStore.getState().projectId;
  const checked = await graphRequest<{ isValid: boolean; errors?: { message: string }[] }>('validate', { config: next });
  if (!checked.isValid) throw new Error(checked.errors?.map(item => item.message).join('; ') || 'The graph is invalid. Open its logic to review validation errors.');
  if (useAppStore.getState().projectId !== projectId) throw new Error('The active project changed during validation. Open the original project before applying.');
  if (canonicalJson(peekRootProject()) !== canonicalJson(base)) throw new Error('The model changed while these inputs were open. Reset before applying.');
  rootProject(); loadProject(next);
  return peekRootProject() as ModelGraph;
}
