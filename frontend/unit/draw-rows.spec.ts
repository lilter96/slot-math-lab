import { expect, test } from '@playwright/test';
import { checkDrawRows, drawRowSummary } from '../src/lib/drawRows';

test('Static integer row summaries retain exact totals and signed fractions beyond safe products', () => {
  expect(drawRowSummary([{ weight: 10000000000, value: 10000000001 }, { weight: 10000000000, value: -10000000000 }])).toEqual({ total: '20000000000', mean: '1/2' });
  expect(drawRowSummary([{ weight: Number.MAX_SAFE_INTEGER, value: 1 }, { weight: Number.MAX_SAFE_INTEGER, value: 2 }])).toEqual({ total: '18014398509481982', mean: '3/2' });
  expect(drawRowSummary([{ weight: 1, value: -2 }])).toEqual({ total: '1', mean: '-2' });
  expect(drawRowSummary([{ weight: 0, value: 1 }])?.mean).toContain('Undefined');
  expect(drawRowSummary([{ weight: 1.5, value: 1 }])).toBeNull();
});

test('Draw row edits accept zero and long weights but reject empty, zero-total and unsafe total', () => {
  expect(() => checkDrawRows([{ weight: 0 }, { weight: 10000000000 }])).not.toThrow();
  for (const rows of [[], [{ weight: 0 }], [{ weight: -1 }], [{ weight: 1.5 }], [{ weight: Number.MAX_SAFE_INTEGER }, { weight: 1 }]]) expect(() => checkDrawRows(rows)).toThrow();
});
