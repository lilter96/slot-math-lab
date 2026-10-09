import { test, expect } from '@playwright/test';
import { metricRecipes, recipeDraft } from '../src/lib/measurements/recipes';
import { definition } from '../src/lib/measurements/model';

test('Every researched requirement has a unique discoverable native workflow and explicit population', () => {
  expect(metricRecipes).toHaveLength(159); expect(new Set(metricRecipes.map(r => r.id)).size).toBe(159);
  expect(new Set(metricRecipes.map(r => r.family)).size).toBe(15);
  for (const recipe of metricRecipes) { expect(recipe.population.length).toBeGreaterThan(20); expect(recipe.prerequisite.length).toBeGreaterThan(20); expect(['measurement', 'reference', 'execution', 'results', 'planning']).toContain(recipe.action); }
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
