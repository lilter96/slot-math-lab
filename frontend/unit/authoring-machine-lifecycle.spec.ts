import { test, expect } from '@playwright/test';
import { machineProofGraph, sequenceMachine, selfMachine } from './fixtures/machineProofs';
import { readMachineSource } from '../src/lib/authoring/machineSource';
import { updateMachine, copyMachineUse, createMachine } from '../src/lib/authoring/machineLifecycle';
import { lowerMachine } from '../src/lib/authoring/machineLowering';
import { num } from '../src/games/shared/graph/expr';
import { Flow } from '../src/games/shared/graph/flow';
import type { ModelDefinition } from '../src/lib/modelProjection';
test('Machine updates preserve opaque graph/node/edge data, no-op identity and source fences', () => {
  const machine = sequenceMachine(), graph = machineProofGraph(machine); graph.mechanics![machine.instance].opaque = { preserved: true }; graph.mechanics![machine.instance].nodes[0].extension = 'keep';
  const binding = readMachineSource(graph, machine.instance)!; expect(binding).not.toBeNull(); expect(updateMachine(graph, binding, binding.machine)).toEqual(graph);
  const edited = structuredClone(binding.machine); edited.states[0].actions[0] = { kind: 'assign', id: 'A', label: 'New award', key: 'coins', expression: num(5) }; const next = updateMachine(graph, binding, edited);
  expect(next.mechanics![machine.instance].opaque).toEqual({ preserved: true }); expect(next.mechanics![machine.instance].nodes[0].extension).toBe('keep'); expect(next.nodes).toEqual(graph.nodes); expect(readMachineSource(next, machine.instance)).not.toBeNull(); expect(() => updateMachine(next, binding, edited)).toThrow('changed');
});
test('One independent use gets private counters and definitions while other fields and callers stay intact', () => {
  const machine = selfMachine(), graph = machineProofGraph(machine), next = copyMachineUse(graph, JSON.stringify([null, 'machine']), 'independent');
  expect(next.nodes![0].mechanicName).toBe('independent'); expect(next.mechanics![machine.instance]).toEqual(graph.mechanics![machine.instance]); expect(readMachineSource(next, 'independent')).not.toBeNull(); expect(lowerMachine(readMachineSource(next, 'independent')!.machine).privateKeys.every(key => !lowerMachine(machine).privateKeys.includes(key))).toBe(true); expect((next.initialState as Record<string, unknown>).coins).toBe(0);
});
test('Creation inserts one connected Library on a real State edge and never adds disconnected entries or extra sinks', () => {
  const graph = machineProofGraph(sequenceMachine()), machine = { ...selfMachine(), instance: 'new-feature' }, edge = (graph.edges as { id: string }[])[0], next = createMachine(graph, machine, { kind: 'edge', scope: null, edgeId: edge.id });
  expect(next.nodes).toHaveLength(graph.nodes!.length + 1); expect(next.nodes!.filter(node => node.nodeType === 'metricsSink')).toHaveLength(1); expect((next.edges as unknown[]).length).toBe((graph.edges as unknown[]).length + 1); expect(next.mechanics!['sequence-machine']).toEqual(graph.mechanics!['sequence-machine']);
});
test('Nested independent copies keep all private namespaces separate, including long instance IDs and repeated copies', () => {
  const inner = { ...selfMachine(), instance: 'inner' }, outer = { ...selfMachine(), instance: 'outer' }; outer.states[0].actions.unshift({ kind: 'library', id: 'nested', label: 'Run nested feature', definition: 'inner', parameters: {} }); const graph = machineProofGraph(outer, lowerMachine(inner).definitions);
  const name = 'a'.repeat(60), first = copyMachineUse(graph, JSON.stringify([null, 'machine']), `${name}-one`), second = copyMachineUse(first, JSON.stringify([null, 'machine']), `${name}-two`); expect(readMachineSource(second, `${name}-two`)).not.toBeNull(); const nested = readMachineSource(second, `${name}-two`)!.machine.states[0].actions[0]; expect(nested.kind).toBe('library'); if (nested.kind === 'library') expect(readMachineSource(second, nested.definition)).not.toBeNull(); expect(second.mechanics!.inner).toEqual(graph.mechanics!.inner); expect(second.mechanics!.outer).toEqual(graph.mechanics!.outer);
});
test('An opaque operation wrapping a nested machine stays intact while its private dependency closure is copied', () => {
  const inner = { ...selfMachine(), instance: 'inner' }, outer = { ...selfMachine(), instance: 'outer' }, wrapper = new Flow().use('inner', 'Nested invocation', 'inner').mechanic('wrapper', 'Opaque wrapping operation') as unknown as ModelDefinition; wrapper.extension = { preserved: true }; outer.states[0].actions.unshift({ kind: 'library', id: 'wrapped', label: 'Wrapped invocation', definition: 'wrapper', parameters: {} }); const graph = machineProofGraph(outer, { ...lowerMachine(inner).definitions, wrapper }), next = copyMachineUse(graph, JSON.stringify([null, 'machine']), 'independent'), binding = readMachineSource(next, 'independent')!;
  expect(binding).not.toBeNull(); const action = binding.machine.states[0].actions[0]; if (action.kind !== 'library') throw new Error('Expected the retained operation'); const operation = next.mechanics![action.definition]; expect(operation.extension).toEqual(wrapper.extension); expect(operation.nodes[0].mechanicName).not.toBe('inner'); expect(readMachineSource(next, String(operation.nodes[0].mechanicName))).not.toBeNull(); expect(next.mechanics!.wrapper).toEqual(wrapper);
});
test('Fractional existing shared state stays editable without resetting it; authored field names remain literal data keys', () => {
  const machine = sequenceMachine(), graph = machineProofGraph(machine); (graph.initialState as Record<string, unknown>).coins = 0.5; const binding = readMachineSource(graph, machine.instance)!; expect(binding).not.toBeNull(); expect(updateMachine(graph, binding, binding.machine)).toEqual(graph);
  const extra = { ...selfMachine(), instance: 'constructor' }; extra.variables.push({ key: '__proto__', scope: 'shared', initial: 7 }, { key: 'constructor', scope: 'shared', initial: 3 }); const next = createMachine(graph, extra, { kind: 'definition' }); expect(Object.hasOwn(next.initialState!, '__proto__')).toBe(true); expect((next.initialState as Record<string, unknown>).constructor).toBe(3); expect((next.initialState as Record<string, unknown>).coins).toBe(0.5);
});
