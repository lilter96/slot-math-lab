/** SHA-256 of recursively canonicalized authored JSON. */
export function canonicalJson(obj: unknown): string {
  function ordered(value: unknown): unknown {
    if (Array.isArray(value)) return value.map(ordered);
    if (value && typeof value === 'object') return Object.fromEntries(
      Object.entries(value).filter(([, v]) => v !== undefined).sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0).map(([k, v]) => [k, ordered(v)]));
    return value;
  }
  return JSON.stringify(ordered(obj));
}
export async function configHash(obj: unknown): Promise<string> {
  const bytes = new TextEncoder().encode(canonicalJson(obj));
  const hash = await crypto.subtle.digest('SHA-256', bytes);
  return Array.from(new Uint8Array(hash), byte => byte.toString(16).padStart(2, '0')).join('');
}
