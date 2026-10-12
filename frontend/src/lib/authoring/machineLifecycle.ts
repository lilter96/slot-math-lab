import type { ModelGraph, ModelNode, ModelDefinition } from '../modelProjection';
import { canonicalJson } from '../hash';
import { reelObject } from '../reelCommands';
import type { MachineIR } from './machineIR';
import { installMachine, lowerMachine, machineSourceRevision, type MachineSourceBinding } from './machineLowering';
import { readMachineSource } from './machineSource';
import { indexGraphSources, resolveNodeSource } from './sourceIndex';
import type { SourceConnection } from './sourceLifecycle';
const same = (a: unknown, b: unknown) => canonicalJson(a) === canonicalJson(b);
const ports = { state: { name: 'state', type: 'State' } };
/** Apply a template delta to mapped source; retain extensions and unchanged authored IDs. */
function delta(actual: unknown, before: unknown, after: unknown): unknown {
  if (same(before, after)) return structuredClone(actual);
  if (Array.isArray(before) && Array.isArray(after) && Array.isArray(actual)) {
    if ([...before, ...after].every(value => reelObject(value)?.id !== undefined)) return after.map(value => { const id = reelObject(value)!.id, previous = before.find(item => reelObject(item)?.id === id), source = actual.find(item => reelObject(item)?.id === id); return previous ? delta(source, previous, value) : structuredClone(value); });
    return structuredClone(after);
  }
  const old = reelObject(before), updated = reelObject(after), source = reelObject(actual);
  if (!old || !updated || !source) return structuredClone(after);
  const result = structuredClone(source); for (const key of new Set([...Object.keys(old), ...Object.keys(updated)])) { if (!(key in updated)) delete result[key]; else if (!(key in old)) result[key] = structuredClone(updated[key]); else result[key] = delta(source[key], old[key], updated[key]); } return result;
}
export function updateMachine(graph: ModelGraph, binding: MachineSourceBinding, machine: MachineIR): ModelGraph {
  if (machineSourceRevision(graph, binding.scopes) !== binding.revision || machine.instance !== binding.definition) throw new Error('This machine, its bindings or its callers changed. Review the current source.');
  const old = lowerMachine(binding.machine, graph.mechanics), emitted = lowerMachine(machine, graph.mechanics), next = structuredClone(graph), owned = new Set(binding.scopes), index = indexGraphSources(graph);
  const removed = binding.scopes.filter(scope => !emitted.definitions[scope]);
  if (index.libraryUses.some(use => removed.includes(use.definition) && !owned.has(use.source.scope ?? ''))) throw new Error('A removed state/operation is used outside this machine. Redirect that caller first.');
  for (const scope of removed) delete next.mechanics![scope];
  for (const [scope, definition] of Object.entries(emitted.definitions)) {
    if (!owned.has(scope) && graph.mechanics?.[scope]) throw new Error('A generated definition collides with another feature.');
    next.mechanics![scope] = old.definitions[scope] ? delta(graph.mechanics![scope], old.definitions[scope], definition) as ModelDefinition : definition;
  }
  let initial = { ...reelObject(next.initialState) };
  for (const [key, value] of Object.entries(emitted.initialState)) { if (!Object.hasOwn(initial, key) || emitted.privateKeys.includes(key) && !same(old.initialState[key], value)) initial = { ...initial, [key]: value }; else if (typeof initial[key] !== typeof value) throw new Error(`Shared field ${key} has an incompatible type.`); }
  // Retain abandoned scratch values: an opaque operation may still reference them.
  next.initialState = initial; if (!readMachineSource(next, machine.instance)) throw new Error('This edit cannot preserve the verified machine source contract.'); return next;
}
export function connectMachine(graph: ModelGraph, definition: string, connection: SourceConnection, payoutKey?: string): ModelGraph {
  if (!graph.mechanics?.[definition]) throw new Error('Choose an existing definition.');
  const next = structuredClone(graph); if (connection.kind === 'definition') return next;
  if (connection.kind === 'replace') {
    const node = resolveNodeSource(next, connection.nodeKey).node as ModelNode;
    if (node.nodeType !== 'library' || !same(node.inputs, ports) || !same(node.outputs, ports)) throw new Error('Choose a State-to-State Library use.');
    node.mechanicName = definition; node.parameters = {}; return next;
  }
  if (connection.kind === 'empty-round') {
    if (next.nodes?.length !== 1 || next.nodes[0].nodeType !== 'metricsSink' || (Array.isArray(next.edges) && next.edges.length) || next.nodes[0].winStateKey !== undefined || !payoutKey || typeof reelObject(next.initialState)?.[payoutKey] !== 'number') throw new Error('A blank round needs one unconnected payout sink and an explicit numeric payout field.');
    const id = `${definition}-entry`, sink = next.nodes[0]; next.nodes = [{ id, nodeType: 'library', label: definition, mechanicName: definition, parameters: {}, inputs: ports, outputs: ports }, { ...sink, winStateKey: payoutKey, inputs: ports }]; next.edges = [{ id: `${id}-settle`, sourceNodeId: id, sourcePort: 'state', targetNodeId: sink.id, targetPort: 'state' }]; return next;
  }
  const owner = connection.scope === null ? next : next.mechanics?.[connection.scope]; if (!owner) throw new Error('The connection scope is unavailable.');
  const edges = Array.isArray(owner.edges) ? owner.edges as Record<string, unknown>[] : [], matches = edges.filter(edge => edge.id === connection.edgeId); if (matches.length !== 1) throw new Error('The connection changed.');
  const edge = matches[0], from = owner.nodes!.find(node => node.id === edge.sourceNodeId), to = owner.nodes!.find(node => node.id === edge.targetNodeId);
  if (reelObject(reelObject(from?.outputs)?.[String(edge.sourcePort)])?.type !== 'State' || reelObject(reelObject(to?.inputs)?.[String(edge.targetPort)])?.type !== 'State') throw new Error('Choose a State connection.');
  let id = `${definition}-use`, suffix = 2; while (owner.nodes!.some(node => node.id === id) || edges.some(edge => edge.id === `${id}-continue`)) id = `${definition}-use-${suffix++}`;
  owner.nodes!.push({ id, nodeType: 'library', label: definition, mechanicName: definition, parameters: {}, inputs: ports, outputs: ports }); owner.edges = edges.flatMap(item => item === edge ? [{ ...item, targetNodeId: id, targetPort: 'state' }, { id: `${id}-continue`, sourceNodeId: id, sourcePort: 'state', targetNodeId: edge.targetNodeId, targetPort: edge.targetPort }] : [item]); return next;
}
export function createMachine(graph: ModelGraph, machine: MachineIR, connection: SourceConnection, payoutKey?: string): ModelGraph { return connectMachine(installMachine(graph, machine), machine.instance, connection, payoutKey); }
/** Rebuild verified machines with new private namespaces. Shared game fields remain intentional. */
export function copyMachineUse(graph: ModelGraph, nodeKey: string, instance: string): ModelGraph {
  const use = resolveNodeSource(graph, nodeKey), definition = String(use.node.mechanicName), binding = readMachineSource(graph, definition); if (use.node.nodeType !== 'library' || !binding) throw new Error('Select a verified reusable machine use.');
  let next = structuredClone(graph); const names = new Map<string, string>(), active = new Set<string>(), taken = new Set(Object.keys(graph.mechanics ?? {})); taken.add(instance);
  const nestedName = () => { const stem = `nested-${instance.slice(0, 25)}-${instance.slice(-25)}-${names.size}`; let name = stem, index = 2; while ([...taken].some(value => value === name || value.startsWith(`${name}-`))) name = `${stem}-${index++}`; taken.add(name); return name; };
  const dependency = (original: string): string => {
    if (active.has(original)) throw new Error('Recursive operation dependencies need explicit graph repair.'); if (names.has(original)) return names.get(original)!;
    const name = nestedName(); if (readMachineSource(graph, original)) { copy(original, name); return name; }
    const definition = graph.mechanics?.[original]; if (!definition) throw new Error('A nested operation definition is missing.');
    if (indexGraphSources(graph).stateWrites.some(write => write.source.scope === original && /^__machine_[\da-f]+_(?:pc|diagnostic|private_)/.test(write.key))) throw new Error('This operation contains unverified private machine controls. Repair its source before making a separate instance.');
    active.add(original); names.set(original, name); const clone = structuredClone(definition); clone.name = name;
    for (const node of clone.nodes) if (node.nodeType === 'library' && node.mechanicName) node.mechanicName = dependency(node.mechanicName);
    next.mechanics![name] = clone; active.delete(original); return name;
  };
  const copy = (original: string, name: string): void => {
    if (active.has(original)) throw new Error('Recursive machine definitions need explicit graph repair.'); if (names.has(original)) return;
    const source = readMachineSource(graph, original); if (!source) return; active.add(original); names.set(original, name); const machine = structuredClone(source.machine); machine.instance = name;
    const actions = machine.states.flatMap(state => [...state.actions, ...state.transitions.flatMap(transition => transition.effects)]);
    for (const action of actions) if (action.kind === 'library') action.definition = dependency(action.definition);
    next = installMachine(next, machine); const fresh = lowerMachine(machine, next.mechanics), old = lowerMachine(source.machine, graph.mechanics);
    const oldEntries = Object.values(old.definitions), freshEntries = Object.values(fresh.definitions);
    freshEntries.forEach((value, index) => { const previous = oldEntries[index]; if (previous) next.mechanics![String(value.name)] = delta(graph.mechanics![String(previous.name)], previous, value) as ModelDefinition; });
    active.delete(original);
  }; copy(definition, instance); return connectMachine(next, instance, { kind: 'replace', nodeKey });
}
