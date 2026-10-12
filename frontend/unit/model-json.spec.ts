import { expect, test } from '@playwright/test';
import { assertModelNumbers, parseModelJson } from '../src/lib/modelJson';

test('Original unsafe JSON tokens are rejected in nested models and server envelopes', () => {
  for (const number of ['9007199254740993', '-9223372036854775808', '9.007199254740993e15', '1e309', '9007199254740991.1', '-9007199254740991.0001']) {
    expect(() => parseModelJson(`{"config":{"mechanics":{"nested":{"value":${number}}}}}`), number).toThrow(/exact browser range/);
  }
});

test('Boundary integers, long weights, fractions and unknown string metadata round trip', () => {
  const source = String.raw`{"nodes":[],"edges":[],"value":9007199254740991,"negative":-9007199254740991,"weight":10000000000,"fraction":0.05,"scientific":9.007199254740991e15,"metadata":"9007199254740993 \" 1e309","zero":0}`;
  const model = parseModelJson(source);
  expect(model).toMatchObject({ value: Number.MAX_SAFE_INTEGER, negative: -Number.MAX_SAFE_INTEGER, weight: 10000000000, fraction: 0.05, scientific: Number.MAX_SAFE_INTEGER });
  expect(parseModelJson(JSON.stringify(model))).toMatchObject(model);
  expect(() => parseModelJson('{"value":9007199254740991.0000}')).not.toThrow();
  expect(() => parseModelJson('{"value":90071992547409910e-1}')).not.toThrow();
});

test('Malformed JSON stays invalid and programmatic model numbers are guarded', () => {
  for (const source of ['null', '[]', '{"value":01}', '{"value":NaN}']) expect(() => parseModelJson(source)).toThrow();
  for (const value of [Infinity, NaN, 2 ** 53]) expect(() => assertModelNumbers({ nested: [value] })).toThrow(/exact browser range/);
  expect(() => assertModelNumbers({ nested: [0.05, 10000000000, '9223372036854775807'] })).not.toThrow();
});
