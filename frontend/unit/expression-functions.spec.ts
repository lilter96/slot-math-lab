import { test, expect } from '@playwright/test';
import { parseExpression } from '../src/lib/expressionParser';

test('Numeric calls produce structured ASTs with nested arithmetic and indexed state', () => {
  expect(parseExpression('abs(state.fractions[0])')).toEqual({ exprType: 'call', function: 'abs', args: [{ exprType: 'fieldAccess', target: 'state', path: ['fractions', '0'] }] });
  const parsed = parseExpression('max(min(3/2, 7/4), floor(-1/2)) + ceil(0.25)');
  expect(parsed).toMatchObject({ exprType: 'binary', op: 'Add', left: { exprType: 'call', function: 'max', args: [
    { exprType: 'call', function: 'min', args: [{ exprType: 'binary', op: 'Div' }, { exprType: 'binary', op: 'Div' }] },
    { exprType: 'call', function: 'floor' }], }, right: { exprType: 'call', function: 'ceil', args: [{ kind: 'Rational', value: '25/100' }] } });
  expect(parseExpression('ROUND(state.x > 1 ? state.x : -1/2)')).toMatchObject({ exprType: 'call', function: 'round', args: [{ exprType: 'if' }] });
});

test('Unknown code, wrong arities, malformed arguments and excessive nesting fail locally', () => {
  for (const text of ['eval(1)', 'constructor(1)', 'Math.abs(1)', 'abs()', 'abs(1,2)', 'min(1)', 'max(1,2,3)', 'floor(1,)', 'abs(,1)', 'abs(1) trailing', 'length()', 'index(state.a)', 'tonumber(1,2)', `${'abs('.repeat(65)}1${')'.repeat(65)}`])
    expect(() => parseExpression(text), text).toThrow();
});

test('Collection and exact text conversions remain typed calls in the native editor', () => {
  expect(parseExpression('toNumber(index(state.paytable, 2)) + length(state.awards)')).toMatchObject({ exprType: 'binary', op: 'Add',
    left: { function: 'tonumber', args: [{ function: 'index', args: [{ path: ['paytable'] }, { value: '2' }] }] },
    right: { function: 'length', args: [{ path: ['awards'] }] } });
  expect(parseExpression('contains(state.awards, 1) ? tonumber(tostring(1 / 3)) : 0')).toMatchObject({ exprType: 'if', condition: { function: 'contains' }, thenExpr: { function: 'tonumber', args: [{ function: 'tostring' }] } });
});
