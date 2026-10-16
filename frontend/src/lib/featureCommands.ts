import type { ExpressionAst } from './expressionParser';
import type { ModelGraph } from './modelProjection';
import { LOOP } from '../constants';
interface Constant { exprType: string; kind?: string; value?: string; [key: string]: unknown }
interface Conditional { exprType: string; thenExpr?: Constant; [key: string]: unknown }
export function freeSpinCapability(graph: ModelGraph): number | null {
  if (!String(graph.id ?? '').startsWith('nine-lives') || !graph.mechanics?.bonus || !graph.mechanics['base-spin']) return null;
  const bonus = graph.mechanics.bonus, base = graph.mechanics['base-spin'];
  const awarded = (bonus.expressions as Record<string, Constant> | undefined)?.lives;
  const initial = (base.expressions as Record<string, Conditional> | undefined)?.['initial-lives'];
  const loops = bonus.nodes.filter(node => node.id === 'free-spins' && node.nodeType === 'loop');
  const awardNode = bonus.nodes.find(node => node.id === 'lives' && node.outputKey === 'remaining' && node.expressionId === 'lives');
  const initialNode = base.nodes.find(node => node.id === 'initial-lives' && node.outputKey === 'remaining' && node.expressionId === 'initial-lives');
  const body = bonus.nodes.find(node => node.id === 'free-spins-body' && node.mechanicName === 'free-spin');
  if (!awardNode || !initialNode || !body || loops.length !== 1 || awarded?.exprType !== 'constant' || awarded.kind !== 'Integer' || initial?.exprType !== 'if' || initial.thenExpr?.exprType !== 'constant' || initial.thenExpr.kind !== 'Integer') return null;
  const count = Number(awarded.value);
  const state = graph.initialState as Record<string, unknown> | undefined;
  return Number.isInteger(count) && count >= 1 && count <= LOOP.capMax && Number(initial.thenExpr.value) === count && loops[0].maxIterations === count && state?.freeSpins === count ? count : null;
}
export function changeFreeSpinCount(graph: ModelGraph, raw: string): ModelGraph {
  if (freeSpinCapability(graph) == null) throw new Error('This model no longer matches the supported free-spin mapping. Edit its logic graph.');
  const count = Number(raw);
  if (!/^\d+$/.test(raw) || !Number.isInteger(count) || count < 1 || count > LOOP.capMax) throw new Error(`Free spins must be a whole number from 1 to ${LOOP.capMax.toLocaleString()}.`);
  const bonus = graph.mechanics!.bonus, base = graph.mechanics!['base-spin'];
  const bonusExpressions = bonus.expressions as Record<string, ExpressionAst>;
  const baseExpressions = base.expressions as Record<string, Conditional>;
  const initial = baseExpressions['initial-lives'];
  return { ...graph, initialState: { ...(graph.initialState as Record<string, unknown>), freeSpins: count }, mechanics: { ...graph.mechanics,
    bonus: { ...bonus, nodes: bonus.nodes.map(node => node.id === 'free-spins' ? { ...node, maxIterations: count } : node), expressions: { ...bonusExpressions, lives: { ...bonusExpressions.lives, value: String(count) } } },
    'base-spin': { ...base, expressions: { ...baseExpressions, 'initial-lives': { ...initial, thenExpr: { ...initial.thenExpr, value: String(count) } } } },
  } };
}
