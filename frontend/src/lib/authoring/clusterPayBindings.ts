import { Mechanics, type Mechanic } from '../../games/shared/graph/flow';
import { clusterPays, clusterState, type ClusterBoard } from '../../games/shared/graph/mechanics/clusters';
import { canonicalJson } from '../hash';
import type { ModelGraph, ModelNode } from '../modelProjection';
import { reelObject } from '../reelCommands';
import { exactInteger, normalizedCredits } from './exactAmounts';
import { matchReelShape, type ReelShape } from './reelShape';
import { indexGraphSources } from './sourceIndex';
import type { PayBinding, PayoutCellEdit } from './payBindings';

interface Entry { symbolId: string; counts: number[]; payouts: string[] }
interface Table { id: string; entries: Entry[] }
interface Closure { root: string; board: ClusterBoard; observer?: { mechanic: string; label: string }; scopes: Map<string, string>; shapes: Map<string, ReelShape>; templates: Record<string, Mechanic> }
export interface ClusterContract { encoding: 'shared-column-major' | 'reference-offset'; minimum: number; weighted: boolean; roots: string[] }
const object = (value: unknown) => reelObject(value);
const same = (left: unknown, right: unknown) => canonicalJson(left) === canonicalJson(right);
const definitions = (graph: ModelGraph) => graph.mechanics ?? {};
function helpers(board: ClusterBoard, observer?: Closure['observer']) {
  const mechanics = new Mechanics(), root = mechanics.add(clusterPays(mechanics, board, observer));
  return { root, templates: mechanics.all() };
}
/** Match the complete helper closure, including alpha-renamed Library targets. Observers stay opaque and retained. */
function closure(graph: ModelGraph, root: string, board: ClusterBoard, observer?: Closure['observer']): Closure | null {
  const expected = helpers(board, observer), scopes = new Map<string, string>(), shapes = new Map<string, ReelShape>();
  const visit = (name: string, scope: string): boolean => {
    if (scopes.has(name)) return scopes.get(name) === scope;
    const actual = definitions(graph)[scope], template = expected.templates[name];
    if (!actual || !template) return false;
    const shape = matchReelShape(actual, template as unknown as ModelGraph); if (!shape) return false;
    scopes.set(name, scope); shapes.set(name, shape);
    for (const [callee, target] of shape.libraries) if (expected.templates[callee] && !visit(callee, target)) return false;
    return true;
  };
  return visit(expected.root, root) ? { root, board, observer, scopes, shapes, templates: expected.templates } : null;
}
function closures(graph: ModelGraph, board: ClusterBoard): Closure[] {
  const result: Closure[] = [];
  for (const [scope, definition] of Object.entries(definitions(graph))) {
    const observers = [undefined, ...definition.nodes.filter(node => node.nodeType === 'library' && typeof node.mechanicName === 'string').map(node => ({ mechanic: node.mechanicName!, label: String(node.label ?? '') }))];
    for (const observer of observers) { const match = closure(graph, scope, board, observer); if (match) { result.push(match); break; } }
  }
  return result;
}
function validTable(table: Table, graph: ModelGraph, cost: number, cells: number) {
  const symbols = Array.isArray(graph.symbols) ? graph.symbols as { id: string; kind: string }[] : [];
  return typeof table.id === 'string' && Array.isArray(table.entries) && table.entries.length > 0
    && new Set(table.entries.map(row => row.symbolId)).size === table.entries.length
    && table.entries.every(row => typeof row.symbolId === 'string' && symbols.some(symbol => symbol.id === row.symbolId)
      && Array.isArray(row.counts) && Array.isArray(row.payouts) && row.counts.length === row.payouts.length && row.counts.length > 0
      && new Set(row.counts).size === row.counts.length && row.counts.every(count => Number.isSafeInteger(count) && count > 0 && count <= cells)
      && row.payouts.every(value => { try { exactInteger(value, cost); return true; } catch { return false; } }));
}

export function clusterPayBindings(graph: ModelGraph): PayBinding[] {
  const state = object(graph.initialState), geometry = object(graph.boardConfig), symbols = Array.isArray(graph.symbols) ? graph.symbols as { id: string; kind: string }[] : [];
  const columns = Number(geometry?.columns), rows = Number(geometry?.rows), cells = columns * rows;
  if (!state || !Number.isSafeInteger(columns) || !Number.isSafeInteger(rows) || columns < 1 || rows < 1 || cells > 52 || (Array.isArray(graph.plugins) && graph.plugins.length)) return [];
  const index = indexGraphSources(graph), tables = Array.isArray(graph.paytables) ? graph.paytables as Table[] : [], result: PayBinding[] = [];
  if (index.stateWrites.some(write => ['clusterPays', 'clusterPaytable', 'clusterMinSize', 'roundCost', 'baseCost'].includes(write.key))) return [];
  for (const table of tables) {
    if (tables.filter(value => value.id === table.id).length !== 1) continue;
    const cost = Number(state.roundCost ?? state.baseCost); if (!Number.isSafeInteger(cost) || cost <= 0 || !validTable(table, graph, cost, cells)) continue;
    const pays = Object.fromEntries(table.entries.map(row => [Number(row.symbolId), Object.fromEntries(row.counts.map((count, i) => [count, exactInteger(row.payouts[i], cost)]))]));
    const matched: Closure[] = [];
    let encoding: ClusterContract['encoding'] = 'shared-column-major', minimum = 5, weighted = false, scopes: string[], roots: string[];
    if (Array.isArray(state.clusterPays)) {
      if (symbols.some((symbol, position) => symbol.id !== String(position)) || symbols.filter(symbol => symbol.kind === 'Wild').length !== 1) continue;
      const wild = symbols.findIndex(symbol => symbol.kind === 'Wild');
      const positive = Object.values(pays).flatMap(row => Object.entries(row).filter(([, pay]) => pay > 0).map(([size]) => Number(size)));
      if (!positive.length) continue;
      minimum = Math.min(...positive);
      const board: ClusterBoard = { columns, rows, wild, symbolCount: symbols.length, pays };
      const expectedState = clusterState(board);
      if (!same(state.clusterPays, expectedState.clusterPays) || state.clusterMinSize !== minimum) continue;
      // Weighted helper contracts use an actual fold source; equal initial arrays do not establish identity.
      const weightKeys = [...new Set(Object.values(definitions(graph)).flatMap(definition => definition.nodes.filter(node => node.outputKey === 'groupSize').flatMap(node => {
        const ast = object(object(definition.expressions)?.[String(node.expressionId)]);
        return ast?.exprType === 'fold' && typeof ast.stateKey === 'string' ? [ast.stateKey] : [];
      })))];
      for (const weights of [undefined, ...weightKeys]) matched.push(...closures(graph, { ...board, ...(weights ? { weights } : {}) }));
      if (!matched.length || matched.some(match => !!match.board.weights !== !!matched[0].board.weights)) continue;
      // A second custom reader must not be silently left with stale minimum/eligibility assumptions.
      const admitted = new Set(matched.flatMap(match => [...match.scopes.values()]));
      if (index.stateReads.some(read => ['clusterPays', 'clusterMinSize'].includes(String(read.statePath[0])) && !admitted.has(read.path[0] === 'mechanics' ? String(read.path[1]) : ''))) continue;
      weighted = !!matched[0].board.weights; scopes = [...admitted]; roots = matched.map(match => match.root);
    } else if (Array.isArray(state.clusterPaytable)) {
      encoding = 'reference-offset';
      // The separate reference contract has an authored offset-five, size-26 lookup, without name/ID dispatch.
      if (cells !== 30 || table.entries.length !== 5 || table.entries.some((row, i) => row.symbolId !== String(i + 1) || !same(row.counts, Array.from({ length: 26 }, (_, count) => count + 5)))) continue;
      const expected = table.entries.flatMap(row => row.payouts.map(value => String(exactInteger(value, cost))));
      if (!same(state.clusterPaytable, expected)) continue;
      const readers = index.stateReads.filter(read => read.statePath[0] === 'clusterPaytable');
      if (readers.length !== 2 || readers.some(read => {
        if (read.path[0] !== 'mechanics' || read.path[2] !== 'expressions') return true;
        const definition = definitions(graph)[String(read.path[1])], id = String(read.path[3]);
        return !definition.nodes.some(node => node.nodeType === 'modifyState' && node.outputKey === 'componentPay' && node.expressionId === id)
          || !same(object(definition.expressions)?.[id], referenceLookup());
      })) continue;
      scopes = [...new Set(readers.map(read => String(read.path[1])))]; roots = scopes;
      const sinks = (graph.nodes ?? []).filter(node => node.nodeType === 'metricsSink');
      const creditNodes = (graph.nodes ?? []).filter(node => node.nodeType === 'modifyState' && node.outputKey === sinks[0]?.winStateKey);
      if (sinks.length !== 1 || creditNodes.length !== 1 || !same(object(graph.expressions)?.[String(creditNodes[0].expressionId)], referenceCredits(cost))) continue;
    } else continue;
    const position = tables.indexOf(table), lookupKey = encoding === 'shared-column-major' ? 'clusterPays' : 'clusterPaytable';
    const uses = index.libraryUses.filter(use => roots.includes(use.definition)).map(use => use.source);
    const values = table.entries.map(row => row.payouts.map(value => String(exactInteger(value, cost))));
    result.push({ version: 1, key: JSON.stringify(['cluster', table.id]), revision: canonicalJson({ table, state, geometry, symbols, nodes: graph.nodes, edges: graph.edges, expressions: graph.expressions, mechanics: graph.mechanics }),
      kind: 'cluster', tableId: table.id, unit: 'cluster credits', roundCost: cost, scale: 1, columns: cells, wild: null, derivedWild: false,
      rows: table.entries.map((row, i) => ({ symbolId: row.symbolId, counts: [...row.counts], values: values[i] })), scopes, uses,
      reads: [['paytables', position], ['initialState'], ['boardConfig'], ['symbols'], ...scopes.map(scope => ['mechanics', scope])],
      writes: [['paytables', position, 'entries'], ['initialState', lookupKey], ...(encoding === 'shared-column-major' ? [['initialState', 'clusterMinSize'], ...scopes.map(scope => ['mechanics', scope])] : [])],
      maximum: Math.max(...values.flat().map(Number)), bound: null, cap: null, cluster: { encoding, minimum, weighted, roots } });
  }
  return result;
}
function referenceLookup() {
  const num = (value: number) => ({ exprType: 'constant', kind: 'Integer', value: String(value) }), field = (key: string) => ({ exprType: 'fieldAccess', target: 'state', path: [key] });
  const binary = (op: string, left: unknown, right: unknown) => ({ exprType: 'binary', op, left, right }), call = (fn: string, ...args: unknown[]) => ({ exprType: 'call', function: fn, args });
  return { exprType: 'if', condition: { exprType: 'compare', op: 'Gte', left: field('componentSize'), right: num(5) },
    thenExpr: call('toNumber', call('index', field('clusterPaytable'), binary('Add', binary('Mul', binary('Sub', call('toNumber', field('targetSymbol')), num(1)), num(26)), binary('Sub', field('componentSize'), num(5))))), elseExpr: num(0) };
}
function referenceCredits(cost: number) { return { exprType: 'binary', op: 'Div', left: { exprType: 'fieldAccess', target: 'state', path: ['roundCoins'] }, right: { exprType: 'constant', kind: 'Integer', value: String(cost) } }; }

export function changeBoundClusterPayouts(graph: ModelGraph, binding: PayBinding, edits: PayoutCellEdit[], domain?: PayBinding['rows']): ModelGraph {
  const current = clusterPayBindings(graph).find(value => value.key === binding.key);
  if (!current || current.revision !== binding.revision) throw new Error('The cluster lookup, traversal or precision declaration changed. Reconcile this draft.');
  const next = structuredClone(graph), table = (next.paytables as Table[]).find(value => value.id === current.tableId)!, seen = new Set<string>();
  let changed = false;
  if (domain) {
    if (current.cluster!.encoding === 'reference-offset') throw new Error('The independent reference keeps its fixed eligible symbols and sizes. Zero a payout to remove its reward.');
    const symbols = graph.symbols as { id: string; kind: string }[];
    if (!domain.length || new Set(domain.map(row => row.symbolId)).size !== domain.length || domain.some(row => !symbols.some(symbol => symbol.id === row.symbolId) || !row.counts.length || row.counts.length !== row.values.length || new Set(row.counts).size !== row.counts.length || row.counts.some(count => !Number.isSafeInteger(count) || count < 1 || count > current.columns))) throw new Error('Choose distinct dictionary symbols and in-bounds paying sizes.');
    const entries = domain.map(row => ({ ...table.entries.find(entry => entry.symbolId === row.symbolId), symbolId: row.symbolId, counts: [...row.counts], payouts: row.values.map(value => normalizedCredits(exactInteger(value), current.roundCost)) }));
    changed = !same(entries, table.entries); table.entries = entries;
  }
  for (const edit of edits) {
    const identity = JSON.stringify([edit.symbolId, edit.count]); if (seen.has(identity)) throw new Error('A cluster payout cell occurs twice in this batch.'); seen.add(identity);
    const row = table.entries.find(row => row.symbolId === edit.symbolId), position = row?.counts.indexOf(edit.count) ?? -1;
    if (!row || !Number.isSafeInteger(edit.count) || edit.count < 1 || edit.count > current.columns) throw new Error('Choose an existing paying symbol and a cluster size within the board.');
    if (current.cluster!.encoding === 'reference-offset' && (position < 0 || edit.remove)) throw new Error('This reference lookup requires its authored size-five through size-thirty domain. Zero a payout to remove its reward.');
    if (edit.remove) {
      if (position < 0) throw new Error('This paying size is not authored.');
      if (row.counts.length === 1) throw new Error('A paying symbol needs at least one declared size. Remove its eligibility through the symbol transformation.');
      row.counts.splice(position, 1); row.payouts.splice(position, 1); changed = true; continue;
    }
    const value = exactInteger(edit.value);
    if (position < 0) { row.counts.push(edit.count); row.payouts.push(normalizedCredits(value, current.roundCost)); changed = true; continue; }
    if (value !== exactInteger(row.payouts[position], current.roundCost)) { row.payouts[position] = normalizedCredits(value, current.roundCost); changed = true; }
  }
  if (!changed) return next;
  reconcileClusterTable(graph, next, current);
  if (!clusterPayBindings(next).some(value => value.key === current.key)) throw new Error('The cluster dependency closure could not be reconciled. The original graph is unchanged.');
  return next;
}

/** Shared authoring reconciliation for eligibility and stable dictionary extension; never executed by the runtime. */
export function reconcileClusterTable(graph: ModelGraph, next: ModelGraph, current: PayBinding): void {
  const state = next.initialState as Record<string, unknown>, table = (next.paytables as Table[]).find(table => table.id === current.tableId)!;
  if (current.cluster!.encoding === 'reference-offset') state.clusterPaytable = table.entries.flatMap(row => row.payouts.map(value => String(exactInteger(value, current.roundCost))));
  else {
    const pays = Object.fromEntries(table.entries.map(row => [Number(row.symbolId), Object.fromEntries(row.counts.map((count, i) => [count, exactInteger(row.payouts[i], current.roundCost)]))]));
    const geometry = object(graph.boardConfig)!, symbols = graph.symbols as { kind: string }[], originalPays = Object.fromEntries(current.rows.map(row => [Number(row.symbolId), Object.fromEntries(row.counts.map((count, i) => [count, Number(row.values[i])]))]));
    const originalBoard: ClusterBoard = { columns: Number(geometry.columns), rows: Number(geometry.rows), wild: symbols.findIndex(symbol => symbol.kind === 'Wild'), symbolCount: symbols.length, pays: originalPays };
    const original = closures(graph, originalBoard);
    if (current.cluster!.weighted) {
      const index = indexGraphSources(graph), weights = [...new Set(index.nodes.filter(source => source.node.outputKey === 'groupSize').flatMap(source => {
        const ast = object(object(source.scope ? definitions(graph)[source.scope].expressions : graph.expressions)?.[String(source.node.expressionId)]);
        return ast?.exprType === 'fold' && typeof ast.stateKey === 'string' ? [ast.stateKey] : [];
      }))];
      for (const weightsKey of weights) original.push(...closures(graph, { ...originalBoard, weights: weightsKey }));
    }
    const updatedBoard = { ...originalBoard, pays, symbolCount: (next.symbols as unknown[]).length }, updatedState = clusterState(updatedBoard);
    if (!Number.isFinite(updatedState.clusterMinSize)) throw new Error('At least one positive cluster payout is required by this traversal contract.');
    state.clusterPays = updatedState.clusterPays; state.clusterMinSize = updatedState.clusterMinSize;
    for (const match of original) reconcileClosure(next, match, { ...match.board, pays, symbolCount: updatedBoard.symbolCount });
  }
}

/** Patch only verified template differences; existing IDs, labels, edge data and custom envelope fields survive. */
function reconcileClosure(graph: ModelGraph, original: Closure, board: ClusterBoard) {
  const updated = helpers(board, original.observer), scopeNames = new Map(original.scopes), all = definitions(graph);
  const unique = (base: string, taken: Set<string>) => { let name = base, suffix = 2; while (taken.has(name)) name = `${base}-${suffix++}`; taken.add(name); return name; };
  const scopeIds = new Set(Object.keys(all));
  for (const name of Object.keys(updated.templates)) if (!scopeNames.has(name)) scopeNames.set(name, unique(`${original.root}-${name}`, scopeIds));
  for (const [name, template] of Object.entries(updated.templates)) {
    const scope = scopeNames.get(name)!, prior = original.templates[name], actual = all[scope];
    if (!prior) { const added = structuredClone(template) as unknown as ModelGraph; added.nodes = added.nodes!.map(node => node.mechanicName ? { ...node, mechanicName: scopeNames.get(node.mechanicName) ?? node.mechanicName } : node); all[scope] = added as typeof all[string]; continue; }
    const shape = original.shapes.get(name)!, nodeIds = new Map([...shape.nodes].map(([id, node]) => [id, node.id])), taken = new Set(actual.nodes.map(node => node.id));
    const expressionIds = new Map<string, string>(), ast = object(actual.expressions) ?? {}, expressionTaken = new Set(Object.keys(ast));
    for (const node of prior.nodes) for (const field of ['expressionId', 'conditionId', 'stopConditionId']) if (typeof node[field] === 'string') expressionIds.set(String(node[field]), String(shape.nodes.get(node.id)![field]));
    for (const node of template.nodes) if (!nodeIds.has(node.id)) nodeIds.set(node.id, unique(node.id, taken));
    for (const id of Object.keys(template.expressions)) if (!expressionIds.has(id)) expressionIds.set(id, unique(id, expressionTaken));
    const semantic = ['nodeType', 'inputs', 'outputs', 'outputKey', 'expressionId', 'conditionId', 'stopConditionId', 'maxIterations', 'mechanicName', 'parameters'];
    const translate = (node: ModelNode): ModelNode => { const value: ModelNode = { ...node, id: nodeIds.get(node.id)! }; for (const field of ['expressionId', 'conditionId', 'stopConditionId']) if (typeof value[field] === 'string') value[field] = expressionIds.get(String(value[field])); if (value.mechanicName) value.mechanicName = scopeNames.get(value.mechanicName) ?? value.mechanicName; return value; };
    actual.nodes = template.nodes.map(node => {
      const previous = prior.nodes.find(value => value.id === node.id), source = shape.nodes.get(node.id), translated = translate(node);
      if (!source || !previous) return translated;
      const patched = { ...source }; for (const field of semantic) if (!same(previous[field], node[field])) { if (node[field] === undefined) delete patched[field]; else patched[field] = translated[field]; } return patched;
    });
    const priorExpressions = prior.expressions, resultAst = { ...ast };
    for (const id of Object.keys(priorExpressions)) if (!(id in template.expressions)) delete resultAst[expressionIds.get(id)!];
    for (const [id, value] of Object.entries(template.expressions)) if (!same(priorExpressions[id], value)) resultAst[expressionIds.get(id)!] = structuredClone(value);
    actual.expressions = resultAst;
    const existingEdges = Array.isArray(actual.edges) ? actual.edges as Record<string, unknown>[] : [], edgeIds = new Set(existingEdges.map(edge => String(edge.id)));
    actual.edges = template.edges.map(edge => {
      const endpoints = { sourceNodeId: nodeIds.get(edge.sourceNodeId)!, targetNodeId: nodeIds.get(edge.targetNodeId)!, sourcePort: edge.sourcePort, targetPort: edge.targetPort };
      return existingEdges.find(value => Object.entries(endpoints).every(([key, endpoint]) => value[key] === endpoint))
        ?? { ...edge, ...endpoints, id: unique(edge.id, edgeIds) };
    });
  }
  graph.mechanics = all;
}
