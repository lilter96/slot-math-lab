import { test, expect } from '@playwright/test';
import { createDogHouseGraph } from '../src/games/doghouse/graph';
import { createExpectationGraph } from '../src/games/doghouse/expectationGraph';

test('The bonus-only completion point is authored and old standard graphs retain their proof scope', () => {
  const full = createDogHouseGraph(), legacy = createDogHouseGraph(false);
  const nodes = full.nodes as { id: string; nodeType: string; expressionId?: string; outputKey?: string }[];
  expect(nodes.find(n => n.id === 'bonus-completed')).toMatchObject({ nodeType: 'modifyState', expressionId: 'bonus-completed', outputKey: 'bonusCompleted' });
  expect((full.expressions as Record<string, unknown>)['bonus-completed']).toEqual({ exprType: 'constant', kind: 'Boolean', value: 'true' });
  expect(createExpectationGraph(full).name).toBeDefined(); expect(createExpectationGraph(legacy).name).toBeDefined();
  const altered = structuredClone(full); (altered.expressions as Record<string, unknown>)['bonus-completed'] = { exprType: 'constant', kind: 'Boolean', value: 'false' };
  expect(() => createExpectationGraph(altered)).toThrow('modified logic');
});
