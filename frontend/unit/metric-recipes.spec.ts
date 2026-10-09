import { test, expect } from '@playwright/test';
import { metricRecipes, recipeDraft } from '../src/lib/measurements/recipes';
import { definition, newMetric, samePlan } from '../src/lib/measurements/model';
import { defaultOptions } from '../src/lib/measurements/analysis';

test('Every researched requirement has a unique discoverable native workflow and explicit population', () => {
  expect(metricRecipes).toHaveLength(159); expect(new Set(metricRecipes.map(r => r.id)).size).toBe(159);
  expect(new Set(metricRecipes.map(r => r.family)).size).toBe(15);
  for (const recipe of metricRecipes) { expect(recipe.population.length).toBeGreaterThan(20); expect(recipe.prerequisite.length).toBeGreaterThan(20); expect(['measurement', 'reference', 'execution', 'results', 'planning']).toContain(recipe.action); expect(['ready', 'authored', 'partial']).toContain(recipe.availability); expect(recipe.implementationScope.length).toBeGreaterThan(30); }
});
test('Money-event recipes compare actual read-only settlement fields; mixed return uses a wager denominator', () => {
  const zero = definition(recipeDraft(metricRecipes.find(r => r.id === 'events.zero')!, 'zero'));
  expect(zero.options?.source).toBe('event'); expect(zero.value?.exprType).toBe('compare'); expect(zero.value?.left).toEqual({ exprType: 'fieldAccess', target: 'measurement', path: ['payout'] });
  const mixed = definition(recipeDraft(metricRecipes.find(r => r.id === 'return.rtp_mixed')!, 'mixed'));
  expect(mixed.options?.pairRole).toBe('wager'); expect(mixed.options?.referenceStatistic).toBe('ratio');
});
test('Game-specific recipes require authored fields or lifecycle boundaries instead of manufacturing zero measurements', () => {
  const cluster = recipeDraft(metricRecipes.find(r => r.id === 'mechanics.cluster')!, 'cluster');
  expect(cluster.nodeId).toBe('__choose_point__'); expect(cluster.expression).toBe(''); expect(() => definition(cluster)).toThrow();
  const bonus = recipeDraft(metricRecipes.find(r => r.id === 'features.episode_payout')!, 'bonus');
  expect(bonus.options?.subject).toBe('episode'); expect(bonus.options?.entryNodeId).toBeUndefined(); expect(() => definition(bonus)).toThrow();
});
test('Ledger assertions and retrigger recipes preserve the declared residual and episode populations', () => {
  const residual = recipeDraft(metricRecipes.find(r => r.id === 'counts.accounting_residual')!, 'residual');
  expect(residual.options?.assertion).toBe('zero'); expect(residual.options?.source).toBe('value');
  expect(residual.expression).toBe(''); expect(() => definition(residual)).toThrow();
  const retrigger = recipeDraft(metricRecipes.find(r => r.id === 'features.any_retrigger')!, 'retrigger');
  expect(retrigger.options?.subject).toBe('episode'); expect(retrigger.options?.source).toBe('event'); expect(retrigger.options?.reduction).toBe('any');
  const rounding = recipeDraft(metricRecipes.find(r => r.id === 'return.rounding')!, 'rounding');
  expect(rounding.options?.assertion).toBe('none'); // legitimate settlement rounding can be nonzero
});
test('An omitted legacy assertion and the API default pin the same plan; changed assertions remain detectable', () => {
  const legacy = { ...newMetric('old-draft'), name: 'Payout', options: defaultOptions() };
  delete legacy.options.assertion;
  const pinned = definition(legacy);
  expect(pinned.options?.assertion).toBe('none');
  const oldDefinition = structuredClone(pinned); delete oldDefinition.options!.assertion;
  expect(samePlan([oldDefinition], [pinned])).toBe(true);
  expect(samePlan([pinned], [{ ...pinned, options: { ...pinned.options!, assertion: 'zero' } }])).toBe(false);
  expect(samePlan([pinned], [{ ...pinned, options: { ...pinned.options!, stake: 2 } }])).toBe(false);
});
