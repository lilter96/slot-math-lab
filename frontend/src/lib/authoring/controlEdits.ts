import type { Expr } from '../../games/shared/graph/expr';
import type { ModelGraph, ModelNode } from '../modelProjection';
import { reelObject } from '../reelCommands';
import { controlRevision, controlSource } from './controlProjection';
import { resolveNodeSource } from './sourceIndex';
import { computedLineBound } from './payoutBounds';
export type ControlEdit = { kind: 'expression'; nodeKey: string; field: 'expressionId' | 'conditionId' | 'stopConditionId'; value: Expr }
  | { kind: 'loop-budget'; nodeKey: string; value: number }
  | { kind: 'label'; nodeKey: string; value: string };
/** Existing structured control stays in place. Guards/actions and shared expression users retain their IDs and order. */
export function changeControlSource(graph: ModelGraph, scope: string | null, revision: string, edits: ControlEdit[], capPolicy: 'keep' | 'recompute'): ModelGraph {
  if (controlRevision(graph, scope) !== revision) throw new Error('The projected source or its callers changed. Review the current graph before applying.');
  const next = structuredClone(graph), definition = controlSource(next, scope), changes = new Set<string>();
  for (const edit of edits) {
    const source = resolveNodeSource(next, edit.nodeKey); if (source.scope !== scope) throw new Error('This change belongs to another source scope.');
    const node = source.node as ModelNode, key = `${edit.nodeKey}:${edit.kind === 'expression' ? edit.field : edit.kind}`;
    if (changes.has(key)) throw new Error('A source input is edited twice.'); changes.add(key);
    if (edit.kind === 'label') { if (!edit.value.trim()) throw new Error('Keep a readable action/state label.'); node.label = edit.value; }
    else if (edit.kind === 'loop-budget') { if (node.nodeType !== 'loop' || !Number.isSafeInteger(edit.value) || edit.value < 1 || edit.value > 100000) throw new Error('The existing Loop limit needs 1–100000 whole passes. It remains an execution guardrail.'); node.maxIterations = edit.value; }
    else {
      const valid = edit.field === 'conditionId' ? node.nodeType === 'branch' : edit.field === 'stopConditionId' ? node.nodeType === 'loop' : node.nodeType === 'modifyState';
      const expressions = { ...reelObject(definition.expressions) }, id = node[edit.field]; if (!valid || typeof id !== 'string' || !Object.hasOwn(expressions, id)) throw new Error('This operation has no mapped expression input. Keep its custom source.');
      if (edit.value.exprType === undefined) throw new Error('Use a typed expression, not an expression ID.'); expressions[id] = structuredClone(edit.value); definition.expressions = expressions;
    }
  }
  if (capPolicy === 'recompute' && edits.some(edit => edit.kind !== 'label')) {
    const previous = computedLineBound(graph), updated = computedLineBound(next, false); if (!previous || !updated) throw new Error('This control edit has no proven computed settlement bound. Retain the current cap explicitly or resolve that contract in the graph.');
    next.nodes = next.nodes!.map(node => node.id === previous.sinkId ? { ...node, winCap: updated.cap } : node);
  }
  return next;
}
