import { Flow, Mechanics, type Mechanic } from '../../games/shared/graph/flow';
import { spinReels, reelState } from '../../games/shared/graph/mechanics/reels';
import { spinCells, cellState } from '../../games/shared/graph/mechanics/cells';
import type { ModelGraph, ModelNode, ModelDefinition } from '../modelProjection';
import { canonicalJson } from '../hash';
import { validateWeights } from '../modelCommands';
import { reelObject } from '../reelCommands';
import { indexGraphSources, resolveNodeSource } from './sourceIndex';
import { reelBindings } from './reelBindings';
import { cellBindings } from './cellBindings';
import { weightBindings } from './weightBindings';
import { exactInteger, normalizedCredits } from './exactAmounts';
import { prizeBindings } from './prizeBindings';
import { num, state, toNumber } from '../../games/shared/graph/expr';

export interface OutcomeInput { id: string; weight: string; payload: string }
export type SourceDefinitionInput = { kind: 'outcomes'; name: string; stateKey: string; outcomes: OutcomeInput[] }
  | { kind: 'cells'; name: string; columns: number; rows: number; outcomes: OutcomeInput[]; symbols?: { id: string; name: string; kind: string }[] }
  | { kind: 'reels'; name: string; rows: number; strips: string[][]; symbols?: { id: string; name: string; kind: string }[] };
export type SourceConnection = { kind: 'edge'; scope: string | null; edgeId: string } | { kind: 'replace'; nodeKey: string } | { kind: 'definition' } | { kind: 'empty-round' };
const same = (a: unknown, b: unknown) => canonicalJson(a) === canonicalJson(b);
const statePorts = { state: { name: 'state', type: 'State' } };
const freshName = (name: string, taken: Set<string>) => { let candidate = name, index = 2; while (taken.has(candidate)) candidate = `${name}-${index++}`; taken.add(candidate); return candidate; };
function checkedName(name: string) { if (!/^[A-Za-z][A-Za-z0-9_-]{0,79}$/.test(name)) throw new Error('Use a source name starting with a letter, up to 80 letters, digits, hyphens or underscores.'); return name; }
function scopeOf(graph: ModelGraph, scope: string | null): ModelGraph { const value = scope === null ? graph : graph.mechanics?.[scope]; if (!value) throw new Error('The selected graph scope is unavailable.'); return value; }
function parsedOutcomes(input: OutcomeInput[]) {
  if (!input.length || input.length > 4096 || input.some(row => !row.id.length) || new Set(input.map(row => row.id)).size !== input.length) throw new Error('Provide 1–4096 unique, non-empty outcome IDs.');
  const error = validateWeights(input.map(row => row.weight)); if (error) throw new Error(error);
  return input.map(row => ({ outcomeId: row.id, weight: exactInteger(row.weight), value: exactInteger(row.payload) }));
}
/** New sources are ordinary primitives, wired into an explicitly selected State action/edge. */
export function createDrawingSource(graph: ModelGraph, input: SourceDefinitionInput, connection: SourceConnection): ModelGraph {
  const name = checkedName(input.name), existing = graph.mechanics ?? {};
  if (Object.hasOwn(existing, name)) throw new Error('This source definition name already exists. Choose a new name.');
  const next = structuredClone(graph), initial = { ...reelObject(next.initialState) }, definitions = new Mechanics(); let definition: Mechanic;
  if (input.kind === 'outcomes') {
    const key = checkedName(input.stateKey), keys = new Set(indexGraphSources(graph).stateWrites.map(write => write.key));
    if (Object.hasOwn(initial, key) || keys.has(key)) throw new Error('A new generic source needs an unused state key. Existing payout/stake/control fields require their dependency-aware editor.');
    const rows = parsedOutcomes(input.outcomes), flow = new Flow().draw('draw', input.name, key, rows.map(row => [row.outcomeId, row.weight] as const));
    definition = flow.mechanic(name, 'An authored independent weighted source.'); definition.nodes[0].drawWeights = rows; initial[key] = rows[0].outcomeId;
  } else {
    const geometry = reelObject(graph.boardConfig), columns = input.kind === 'cells' ? input.columns : input.strips.length, rows = input.rows;
    if (!Number.isSafeInteger(columns) || !Number.isSafeInteger(rows) || columns < 1 || columns > 64 || rows < 1 || columns * rows > 64) throw new Error('Choose a positive board geometry with at most 64 cells.');
    if (geometry && (geometry.columns !== columns || geometry.rows !== rows)) throw new Error('This source must match the current board geometry. Resizing existing game consumers needs a separate dependency transformation.');
    const symbols = Array.isArray(graph.symbols) && graph.symbols.length ? graph.symbols as { id: string; name: string; kind: string }[] : input.symbols;
    if (!symbols?.length || new Set(symbols.map(symbol => symbol.id)).size !== symbols.length || symbols.some(symbol => !symbol.id || !symbol.name)) throw new Error('Choose the current symbol dictionary, or define a unique dictionary for this new model.');
    if (!Array.isArray(graph.symbols) || !graph.symbols.length) next.symbols = structuredClone(symbols);
    next.boardConfig = geometry ?? { columns, rows };
    if (input.kind === 'cells') {
      const outcomes = parsedOutcomes(input.outcomes); if (outcomes.some(row => !symbols.some(symbol => symbol.id === row.outcomeId) || row.value !== 0)) throw new Error('Board draws require dictionary symbol IDs and zero numeric payloads.');
      const spec = { name, cells: columns * rows, outcomes: outcomes.map(row => [row.outcomeId, row.weight] as const) };
      definition = spinCells(spec); const owned = cellState(spec);
      for (const [key, value] of Object.entries(owned)) { if (key === 'board' && key in initial) continue; if (key in initial && !same(initial[key], value)) throw new Error(`The existing ${key} contract differs; preserve it and choose a compatible source.`); initial[key] ??= value; }
    } else {
      const prefix = `${name}Strip`, spec = { name, stripKey: prefix, strips: input.strips, rows };
      if ('emptyArray' in initial && !same(initial.emptyArray, [])) throw new Error('The existing emptyArray source is not an empty array; preserve its custom contract.');
      initial.emptyArray ??= [];
      if (input.strips.some(strip => strip.length < 1 || strip.length > 4096 || strip.some(id => !symbols.some(symbol => symbol.id === id)))) throw new Error('Each strip needs 1–4096 dictionary symbol IDs.');
      definition = spinReels(spec, definitions);
      for (const [key, value] of Object.entries(reelState(spec))) { if (key in initial && !same(initial[key], value)) throw new Error(`State key ${key} is already owned by another source.`); initial[key] = value; }
      initial.board ??= Array.from({ length: columns * rows }, () => symbols[0].id);
    }
  }
  const added = { ...definitions.all(), [name]: definition };
  if (Object.keys(added).some(key => Object.hasOwn(existing, key))) throw new Error('A generated helper name conflicts with an existing definition. Choose another source name.');
  const prepared: Record<string, ModelDefinition> = Object.fromEntries(Object.entries(added).map(([key, value]) => [key, { ...value }]));
  next.initialState = initial; next.mechanics = { ...next.mechanics, ...prepared };
  if (connection.kind === 'definition') return next;
  if (connection.kind === 'empty-round') {
    if (graph.nodes?.length !== 1 || graph.nodes[0].nodeType !== 'metricsSink' || (Array.isArray(graph.edges) && graph.edges.length) || graph.nodes[0].winStateKey !== undefined) throw new Error('Only a blank, unconnected payout sink can acquire a new round entry here. Choose an existing action/connection.');
    const sink = next.nodes![0], payout = `${name}Result`, nodes = new Set([sink.id]), start = freshName(`${name}-entry`, nodes), finish = freshName(`${name}-result`, nodes), ast = { ...reelObject(next.expressions) }, expression = freshName(`${name}-payout`, new Set(Object.keys(ast)));
    if (input.kind === 'outcomes') input.outcomes.forEach(row => exactInteger(row.id));
    ast[expression] = input.kind === 'outcomes' ? toNumber(state(input.stateKey)) : num(0);
    next.expressions = ast; next.nodes = [{ id: start, nodeType: 'library', label: input.name, mechanicName: name, parameters: {}, inputs: statePorts, outputs: statePorts }, { id: finish, nodeType: 'modifyState', label: input.kind === 'outcomes' ? 'Numeric outcome as the round payout' : 'No payouts have been authored yet', outputKey: payout, expressionId: expression, inputs: statePorts, outputs: statePorts }, { ...sink, winStateKey: payout, inputs: statePorts }];
    next.edges = [{ id: `${start}-to-result`, sourceNodeId: start, sourcePort: 'state', targetNodeId: finish, targetPort: 'state' }, { id: `${finish}-to-sink`, sourceNodeId: finish, sourcePort: 'state', targetNodeId: sink.id, targetPort: 'state' }];
    return next;
  }
  if (connection.kind === 'replace') {
    const target = resolveNodeSource(next, connection.nodeKey).node as ModelNode;
    if (target.nodeType !== 'library' || !same(target.inputs, statePorts) || !same(target.outputs, statePorts)) throw new Error('Select an existing State-to-State Library action to replace.');
    target.mechanicName = name;
  } else {
    const scope = scopeOf(next, connection.scope), edges = Array.isArray(scope.edges) ? scope.edges as Record<string, unknown>[] : [], found = edges.filter(edge => edge.id === connection.edgeId);
    if (found.length !== 1) throw new Error('The selected connection is missing or ambiguous.');
    const edge = found[0], from = scope.nodes?.find(node => node.id === edge.sourceNodeId), to = scope.nodes?.find(node => node.id === edge.targetNodeId);
    const output = reelObject(reelObject(from?.outputs)?.[String(edge.sourcePort)]), port = reelObject(reelObject(to?.inputs)?.[String(edge.targetPort)]);
    if (output?.type !== 'State' || port?.type !== 'State') throw new Error('Select a State connection; source insertion does not convert port types.');
    const nodeIds = new Set(scope.nodes!.map(node => node.id)), edgeIds = new Set(edges.map(edge => String(edge.id))), id = freshName(`${name}-use`, nodeIds);
    scope.nodes!.push({ nodeType: 'library', id, label: input.name, mechanicName: name, parameters: {}, inputs: statePorts, outputs: statePorts });
    scope.edges = edges.flatMap(item => item === edge ? [{ ...item, targetNodeId: id, targetPort: 'state' }, { id: freshName(`${id}-continue`, edgeIds), sourceNodeId: id, sourcePort: 'state', targetNodeId: edge.targetNodeId, targetPort: edge.targetPort }] : [item]);
  }
  return next;
}

export interface DefinitionCopyPlan { nodeKey: string; definition: string; copied: string[]; privateStrips: string[]; uses: number }
export function definitionCopyPlan(graph: ModelGraph, nodeKey: string): DefinitionCopyPlan {
  const target = resolveNodeSource(graph, nodeKey); if (target.node.nodeType !== 'library' || typeof target.node.mechanicName !== 'string') throw new Error('Choose one Library use of a source.');
  const definition = target.node.mechanicName, copied = new Set<string>(), active = new Set<string>();
  const visit = (name: string) => { if (active.has(name)) throw new Error('Recursive source definitions cannot be copied by this workflow.'); if (copied.has(name)) return; const scope = graph.mechanics?.[name]; if (!scope) throw new Error('A referenced definition is missing.'); active.add(name); for (const node of scope.nodes) if (node.nodeType === 'library' && node.mechanicName) visit(node.mechanicName); active.delete(name); copied.add(name); };
  visit(definition);
  const reels = reelBindings(graph).filter(binding => binding.source.stops.some(stop => copied.has(stop.scope)));
  if (reels.some(binding => binding.source.conditions?.some(condition => copied.has(condition.scope)))) throw new Error('This conditioned source needs a private conditioning-state closure. Edit its existing strips; cloning that contract is not yet supported.');
  return { nodeKey, definition, copied: [...copied], privateStrips: [...new Set(reels.flatMap(binding => binding.source.stripKeys))], uses: indexGraphSources(graph).libraryUses.filter(use => use.definition === definition).length };
}
/** One selected use gets private definitions/strip data; other callers and game-state keys retain their contracts. */
export function copySourceUse(graph: ModelGraph, nodeKey: string, name: string): ModelGraph {
  checkedName(name); if (Object.hasOwn(graph.mechanics ?? {}, name)) throw new Error('Choose a fresh private definition name.');
  const plan = definitionCopyPlan(graph, nodeKey), next = structuredClone(graph), taken = new Set(Object.keys(next.mechanics!)), names = new Map([[plan.definition, name]]); taken.add(name);
  for (const original of plan.copied) if (!names.has(original)) names.set(original, freshName(`${name}-${original}`, taken));
  const initial = next.initialState as Record<string, unknown>, stateNames = new Map<string, string>();
  for (const key of plan.privateStrips) { const replacement = `${name}_${key}`; if (Object.hasOwn(initial, replacement)) throw new Error('A private strip state key already exists. Choose another name.'); stateNames.set(key, replacement); initial[replacement] = structuredClone(initial[key]); }
  const rename = (value: unknown): void => {
    if (!value || typeof value !== 'object') return; if (Array.isArray(value)) { value.forEach(rename); return; }
    const record = value as Record<string, unknown>;
    if (record.exprType === 'fieldAccess' && record.target === 'state' && Array.isArray(record.path) && stateNames.has(String(record.path[0]))) record.path[0] = stateNames.get(String(record.path[0]));
    if (typeof record.stateKey === 'string' && stateNames.has(record.stateKey)) record.stateKey = stateNames.get(record.stateKey);
    Object.values(record).forEach(rename);
  };
  for (const original of plan.copied) {
    const definition = structuredClone(graph.mechanics![original]); definition.name = names.get(original); definition.nodes = definition.nodes.map(node => node.mechanicName && names.has(node.mechanicName) ? { ...node, mechanicName: names.get(node.mechanicName) } : node); rename(definition);
    next.mechanics![names.get(original)!] = definition;
  }
  (resolveNodeSource(next, nodeKey).node as ModelNode).mechanicName = name; return next;
}

export function changeSourceOutcomes(graph: ModelGraph, key: string, input: OutcomeInput[]): ModelGraph {
  const binding = weightBindings(graph).find(binding => binding.key === key); if (!binding || binding.capability !== 'editable') throw new Error(binding?.reason ?? 'This source cannot edit static outcomes.');
  if (reelBindings(graph).some(binding => binding.source.stops.some(stop => resolveNodeSource(graph, key).scope === stop.scope && resolveNodeSource(graph, key).id === stop.nodeId))) throw new Error('Change physical stops in the strip editor so wrap/domain dependencies stay synchronized.');
  const rows = parsedOutcomes(input), next = structuredClone(graph), cell = cellBindings(graph).find(binding => binding.cells.some(cell => cell.source.key === key));
  if (cell) {
    const symbols = graph.symbols as { id: string }[]; if (rows.some(row => row.value !== 0 || !symbols.some(symbol => symbol.id === row.outcomeId))) throw new Error('Board outcomes require existing dictionary symbols and zero payloads.');
  } else {
    const source = binding.source.node, owned = source.nodeType === 'draw' && typeof source.stateWriteKey === 'string' && Object.keys(graph.mechanics ?? {}).some(scope => {
      const definition = graph.mechanics![scope]; return scope === binding.source.scope && definition.nodes.length === 1 && definition.nodes[0].id === source.id && !Object.keys(reelObject(definition.expressions) ?? {}).length;
    });
    if (!owned) throw new Error('This outcome domain has a custom/prize/control dependency. Use its payout/source contract or create an explicit independent source.');
  }
  const node = resolveNodeSource(next, key).node as ModelNode, previous = node.drawWeights as Record<string, unknown>[];
  node.drawWeights = rows.map(row => ({ ...previous.find(previous => previous.outcomeId === row.outcomeId), ...row }));
  return next;
}
export function changePrizeDomain(graph: ModelGraph, key: string, values: { value: string; addedWeight?: string }[], removed: boolean): ModelGraph {
  const binding = prizeBindings(graph).find(binding => binding.key === key); if (!binding) throw new Error('The numeric prize contract is unavailable.');
  const amounts = values.map(row => exactInteger(row.value)); if (!amounts.length || amounts.some(value => value <= 0 || !Number.isSafeInteger(value * binding.scale)) || new Set(amounts).size !== amounts.length) throw new Error('Prizes need distinct, positive exact stake amounts.');
  if (same(amounts.map(String), binding.prize!.outcomeIds) && values.every(row => row.addedWeight === undefined)) return structuredClone(graph);
  const old = binding.prize!.outcomeIds; if (old.some(id => !amounts.map(String).includes(id)) && !removed) throw new Error('Confirm discarding the weights of removed prize outcomes.');
  const next = structuredClone(graph);
  for (const source of binding.prize!.sources) {
    const node = resolveNodeSource(next, source.key).node as ModelNode, previous = node.drawWeights as Record<string, unknown>[];
    const rows = values.map((row, index) => { const original = previous.find(value => value.outcomeId === String(amounts[index])); if (!original && row.addedWeight === undefined) throw new Error('Choose the exact weight for every added prize.'); return { ...original, outcomeId: String(amounts[index]), weight: original?.weight ?? exactInteger(row.addedWeight!), value: 0 }; });
    const error = validateWeights(rows.map(row => String(row.weight))); if (error) throw new Error(error); node.drawWeights = rows;
  }
  const tables = next.paytables as { id: string; entries: { counts: number[]; payouts: string[] }[] }[], table = tables.find(table => table.id === binding.tableId)!;
  table.entries[0] = { ...table.entries[0], counts: amounts.map((_, index) => index + 1), payouts: amounts.map(value => normalizedCredits(value * binding.scale, binding.roundCost)) };
  if (!prizeBindings(next).some(binding => binding.key === key)) throw new Error('The edited prize domain failed its consumer/precision contract.'); return next;
}
