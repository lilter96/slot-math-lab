import { test, expect } from '@playwright/test';
import { weightsCsv, importWeightsCsv } from '../src/lib/weightCsv';
const rows = [{ outcomeId: 'a,"quoted"', weight: 1, value: 10 }, { outcomeId: 'line\nbreak', weight: 3, value: 0 }];
test('Weight CSV preserves quoted multiline outcome IDs and maps file rows to original order', () => {
  expect(importWeightsCsv(weightsCsv(rows), rows)).toEqual(['1', '3']);
  expect(importWeightsCsv(weightsCsv([...rows].reverse(), ['4', '2']), rows)).toEqual(['2', '4']);
});
test('CSV rejects unknown, missing, duplicate, invalid weights and malformed quoting before staging', () => {
  for (const csv of ['id,weight\na,1', 'outcomeId,weight\na,1', 'outcomeId,weight\na,1\na,2', 'outcomeId,weight\n"unclosed,2', weightsCsv(rows, ['0', '0']), weightsCsv(rows, ['1.5', '2'])]) expect(() => importWeightsCsv(csv, rows)).toThrow();
});
