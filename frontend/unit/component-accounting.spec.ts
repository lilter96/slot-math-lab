import { test, expect } from '@playwright/test';
import { accountingPlan, accountingCandidates, type AccountingInput } from '../src/lib/measurements/accounting';
import { definition } from '../src/lib/measurements/model';
import { parseExpression } from '../src/lib/expressionParser';
const input: AccountingInput = { name: 'Three', nodeId: 'end', unit: 'coins', stake: 2, total: parseExpression('state.total'), components: ['x', 'y', 'z'].map(name => ({ name, value: parseExpression(`state.${name}`) })), filter: parseExpression('state.fsType == "sticky"'), group: parseExpression('state.fsType'), supportLimit: 32, groupLimit: 4 };
test('A bounded six-component ledger collects all 15 cross terms on the same authored population', () => {
  const plan = accountingPlan({ ...input, components: ['a', 'b', 'c', 'd', 'e', 'f'].map(name => ({ name, value: parseExpression(`state.${name}`) })) }, 'six');
  expect(plan.metrics).toHaveLength(23); const definitions = plan.metrics.map(definition);
  expect(definitions.filter(d => d.options?.pair)).toHaveLength(15);
  for (const d of definitions) { expect(d.nodeId).toBe('end'); expect(d.filter).toEqual(input.filter); expect(d.options?.group).toEqual(input.group); expect(d.options?.stake).toBe(2); expect(d.options?.independentSubjects).toBe(false); }
  expect(definitions.at(-1)?.options?.assertion).toBe('zero');
  expect(accountingCandidates(definitions)).toEqual([plan.request]);
});
test('A component that is itself an addition stays one component after saved-plan discovery', () => {
  const plan = accountingPlan({ ...input, components: [{ name: 'Combined base', value: parseExpression('state.lines + state.scatter') }, ...input.components.slice(1)] }, 'compound');
  const definitions = plan.metrics.map(definition);
  expect(accountingCandidates(definitions)).toEqual([plan.request]);
  const residual = definitions.at(-1)!;
  const changed = definitions.map(d => d.id === residual.id ? { ...d, value: parseExpression('state.total - state.x') } : d);
  expect(accountingCandidates(changed)).toEqual([]);
  expect(accountingCandidates(definitions.map(d => d.id === plan.request.componentMeasurementIds[0] ? { ...d, nodeId: 'another-point' } : d))).toEqual([]);
});
test('Duplicate expression components retain distinct authored roles and ordered residual inputs', () => {
  const plan = accountingPlan({ ...input, components: [{ name: 'First award', value: parseExpression('state.x') }, { name: 'Second award', value: parseExpression('state.x') }] }, 'duplicate');
  expect(accountingCandidates(plan.metrics.map(definition))).toEqual([plan.request]);
  expect(() => accountingPlan({ ...input, components: input.components.slice(0, 1) }, 'bad')).toThrow();
});
