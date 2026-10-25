import { parseCsv } from './weightCsv';
import { validateReels } from './reelCommands';
import type { ModelGraph } from './modelProjection';
export function reelsCsv(strips: string[][]): string {
  return ['reel,index,symbolId', ...strips.flatMap((strip, reel) => strip.map((symbol, index) => `${reel + 1},${index},"${symbol.replaceAll('"', '""')}"`))].join('\r\n');
}
export function importReelsCsv(text: string, graph: ModelGraph, reelCount: number): string[][] {
  if (text.length > 1048576) throw new Error('Strip CSV is limited to 1 MiB.');
  const rows = parseCsv(text);
  if (rows[0]?.join(',') !== 'reel,index,symbolId') throw new Error('Use the reel,index,symbolId CSV header. Reels start at 1; indices start at 0.');
  const byReel = Array.from({ length: reelCount }, () => new Map<number, string>());
  for (const row of rows.slice(1)) {
    if (row.length !== 3 || !/^\d+$/.test(row[0]) || !/^\d+$/.test(row[1])) throw new Error('Each CSV row needs a reel number, index and symbol ID.');
    const reel = Number(row[0]) - 1, index = Number(row[1]);
    if (reel < 0 || reel >= reelCount || index > 4095 || byReel[reel].has(index)) throw new Error('CSV contains an unknown reel, duplicate index or index outside the editor limit.');
    byReel[reel].set(index, row[2]);
  }
  const strips = byReel.map(entries => {
    const ordered = [...entries.entries()].sort(([a], [b]) => a - b);
    if (ordered.some(([index], order) => index !== order)) throw new Error('Every reel must use consecutive indices starting at 0.');
    return ordered.map(([, symbol]) => symbol);
  });
  const invalid = validateReels(graph, strips); if (invalid) throw new Error(invalid);
  return strips;
}
