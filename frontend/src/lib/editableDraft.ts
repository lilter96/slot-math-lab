import type { ProjectDraft } from '../store';
import { parseModelJson } from './modelJson';

const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);

/** Restore editor source, including incomplete DSL, without trusting imported evidence or identities. */
export function parseEditableDraft(text: string): ProjectDraft {
  const draft = parseModelJson(text);
  const fail = () => { throw new Error('Choose a valid slotmath-editable-draft-v1 backup. The current project has not been replaced.'); };
  if (draft.format !== 'slotmath-editable-draft-v1' || !Array.isArray(draft.nodes) || !Array.isArray(draft.edges) || !record(draft.tables) || !Array.isArray(draft.graphTrail) || (draft.configName !== null && typeof draft.configName !== 'string')) return fail();
  const ids = new Set<string>();
  for (const node of draft.nodes) {
    if (!record(node) || typeof node.id !== 'string' || !node.id || ids.has(node.id) || typeof node.type !== 'string' || !record(node.data) || typeof node.data.nodeType !== 'string' || !record(node.position) || typeof node.position.x !== 'number' || typeof node.position.y !== 'number') return fail();
    ids.add(node.id);
  }
  const edges = new Set<string>();
  for (const edge of draft.edges) {
    if (!record(edge) || typeof edge.id !== 'string' || !edge.id || edges.has(edge.id) || typeof edge.source !== 'string' || typeof edge.target !== 'string' || !ids.has(edge.source) || !ids.has(edge.target)) return fail();
    edges.add(edge.id);
  }
  for (const entry of draft.graphTrail) {
    if (!record(entry) || typeof entry.mechanic !== 'string' || !entry.mechanic || !record(entry.graph) || !Array.isArray(entry.graph.nodes) || !Array.isArray(entry.graph.edges) || !record(entry.graph.mechanics) || !record(entry.graph.mechanics[entry.mechanic])) return fail();
  }
  return {
    nodes: draft.nodes as ProjectDraft['nodes'], edges: draft.edges as ProjectDraft['edges'], tables: draft.tables,
    graphTrail: draft.graphTrail as ProjectDraft['graphTrail'], configName: draft.configName as string | null,
    verificationSource: null, authoringDraft: null, resultsDraft: null, updatedAt: new Date().toISOString(),
  };
}
