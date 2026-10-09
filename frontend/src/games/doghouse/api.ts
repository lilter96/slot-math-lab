export interface LineWin { line: number; symbol: number; count: number; multiplier: number; coins: number }
export interface GameFrame { board: number[]; multipliers: number[]; lines: LineWin[]; coins: number; freeSpin: boolean }
export interface GraphRound { seed: number; roundIndex: number; configHash: string; win: number; winNumerator: string; winDenominator: string; state: Record<string, unknown> }
export interface GameRound extends GraphRound { bonusGrid: number[]; frames: GameFrame[] }
export type MathMode = 'Exact' | 'Pruned' | 'Sampled' | 'Hybrid';
export interface GraphAnalysis {
  requestedMode: MathMode; actualStrategy: string; provenance: string; status: string;
  rtp?: number; rationalRtp?: string; lower?: number; upper?: number; prunedMass?: number;
  samples?: number; requestedSamples?: number; seed?: number; degreeOfParallelism?: number;
  hitFrequency?: number; variance?: number; maxObserved?: number; elapsedMs: number; configHash: string; note?: string;
  streamScheme?: string; chunkSize?: number;
}
export async function graphRequest<T>(path: string, body: unknown, signal?: AbortSignal): Promise<T> {
  const response = await fetch((import.meta.env.VITE_API_URL ?? '') + '/api/' + path, { method: 'POST',
    headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body), signal });
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.error ?? error.title ?? (response.status === 401 ? 'Session expired. Sign in again.' : response.status === 429 ? 'Calculation limit reached. Retry shortly.' : `Request failed (${response.status})`));
  }
  return response.json();
}
export function presentRound(raw: GraphRound): GameRound {
  const s = raw.state;
  const boards = s.boards as string[][] ?? [];
  const numbers = (key: string, index: number): number[] => ((s[key] as string[][])?.[index] ?? []).map(Number);
  return { ...raw, bonusGrid: (s.bonusGrid as string[] ?? []).map(Number), frames: boards.map((board, index) => {
    const wins = numbers('lineWinHistory', index), symbols = numbers('lineSymbolHistory', index), counts = numbers('lineCountHistory', index), factors = numbers('lineFactorHistory', index);
    return { board: board.map(Number), multipliers: numbers('multiplierHistory', index), freeSpin: index > 0,
      coins: Number((s.winHistory as string[])?.[index] ?? 0),
      lines: wins.flatMap((coins, line) => coins > 0 ? [{ line: line + 1, coins, symbol: symbols[line], count: counts[line], multiplier: factors[line] }] : []) };
  }) };
}
export const SYMBOL_NAMES = ['Bonus', 'Wild', 'Rottweiler', 'Shih Tzu', 'Pug', 'Dachshund', 'Collar', 'Bone', 'A', 'K', 'Q', 'J', '10'];
export const MODES: MathMode[] = ['Exact', 'Pruned', 'Sampled', 'Hybrid'];
export const MODE_LABELS = { Exact: 'Exact', Pruned: 'ε-pruned', Sampled: 'Sampled', Hybrid: 'Auto (Hybrid)' };
export function downloadJson(name: string, value: unknown) {
  const url = URL.createObjectURL(new Blob([JSON.stringify(value, null, 2)], { type: 'application/json' }));
  const link = document.createElement('a'); link.href = url; link.download = name; link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
