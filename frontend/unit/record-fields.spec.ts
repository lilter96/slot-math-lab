import { test, expect } from '@playwright/test';
import { stateFieldExpression } from '../src/lib/measurements/model';
import { parseExpression } from '../src/lib/expressionParser';

test('Graph field insertion preserves path segments, literal dots and quoted keys', () => {
  for (const path of [['selected', 'money', 'award'], ['literal.dot'], ['quoted"key', '0', 'nested'], ['back\\slash']]) {
    const expression = stateFieldExpression({ name: path.join('.'), type: 'Number', path });
    expect(parseExpression(expression)).toEqual({ exprType: 'fieldAccess', target: 'state', path });
  }
  expect(parseExpression(stateFieldExpression({ name: 'legacy.dot', type: 'Number' }))).toEqual({ exprType: 'fieldAccess', target: 'state', path: ['legacy.dot'] });
});
