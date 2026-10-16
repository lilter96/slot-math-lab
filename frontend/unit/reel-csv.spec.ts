import { test, expect } from '@playwright/test';
import { importReelsCsv, reelsCsv } from '../src/lib/reelCsv';
const graph = { symbols: [{ id: 'a,b' }, { id: 'W' }, { id: 'say"hi' }] };
test('Strip CSV preserves symbol IDs and reconstructs ordered stops from unordered rows', () => {
  const strips = [['a,b', 'W'], ['say"hi']];
  expect(importReelsCsv(reelsCsv(strips), graph, 2)).toEqual(strips);
  expect(importReelsCsv('reel,index,symbolId\n2,0,W\n1,1,W\n1,0,"a,b"', graph, 2)).toEqual([['a,b', 'W'], ['W']]);
});
test('Strip CSV rejects unknown symbols, missing reels/indices, duplicates and malformed quoting', () => {
  for (const data of ['1,0,W\n1,0,W', '1,1,W', '3,0,W', '1,0,unknown', '1,0,"W', '1,0,W\n2,0,unknown', '1,0,W']) {
    expect(() => importReelsCsv('reel,index,symbolId\n' + data, graph, 2)).toThrow();
  }
});
