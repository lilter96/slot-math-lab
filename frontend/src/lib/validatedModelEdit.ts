import { useAppStore } from '../store';
import { graphRequest } from './graphApi';
import { canonicalJson } from './hash';
import { peekRootProject, rootProject, loadProject } from './projectFiles';
import type { ModelGraph } from './modelProjection';
export async function applyValidatedModel(base: ModelGraph, next: ModelGraph, signal?: AbortSignal): Promise<ModelGraph> {
  const scope = () => { const state = useAppStore.getState(); return canonicalJson([state.projectId, state.graphTrail.map(entry => entry.mechanic), peekRootProject()]); };
  if (canonicalJson(peekRootProject()) !== canonicalJson(base)) throw new Error('The model changed while these inputs were open. Reset before applying.');
  const origin = scope(), controller = new AbortController();
  const combined = signal ? AbortSignal.any([signal, controller.signal]) : controller.signal;
  const unsubscribe = useAppStore.subscribe((current, previous) => {
    if (current.projectId === previous.projectId && current.graphTrail === previous.graphTrail && current.nodes === previous.nodes && current.edges === previous.edges && current.tables === previous.tables && current.configName === previous.configName) return;
    if (scope() !== origin) controller.abort(new DOMException('Validation cancelled because the project, source or mechanic context changed.', 'AbortError'));
  });
  try {
    const checked = await graphRequest<{ isValid: boolean; errors?: { message: string }[] }>('validate', { config: next }, combined);
    combined.throwIfAborted();
    if (scope() !== origin) throw new DOMException('Validation context changed.', 'AbortError');
    if (!checked.isValid) throw new Error(checked.errors?.map(item => item.message).join('; ') || 'The graph is invalid. Open its logic to review validation errors.');
    // The accepted transaction itself changes the source; stop observing before applying it.
    unsubscribe();
    rootProject(); loadProject(next);
    return peekRootProject() as ModelGraph;
  } finally { unsubscribe(); }
}
