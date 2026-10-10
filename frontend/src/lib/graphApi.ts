export interface GraphRound { seed: number; roundIndex: number; configHash: string; win: number; winNumerator: string; winDenominator: string; state: Record<string, unknown> }
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
export const MODES: MathMode[] = ['Exact', 'Pruned', 'Sampled', 'Hybrid'];
export const MODE_LABELS = { Exact: 'Exact', Pruned: 'ε-pruned', Sampled: 'Sampled', Hybrid: 'Auto (Hybrid)' };
export function downloadJson(name: string, value: unknown) {
  const url = URL.createObjectURL(new Blob([JSON.stringify(value, null, 2)], { type: 'application/json' }));
  const link = document.createElement('a'); link.href = url; link.download = name; link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
