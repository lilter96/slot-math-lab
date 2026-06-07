/**
 * Deterministic config hash — same config produces same hash anywhere.
 * Used for export/import round-trip verification and caching.
 * Simple djb2 hash over canonical JSON — fast, deterministic, no crypto needed.
 */
export function configHash(obj: unknown): string {
  const json = JSON.stringify(obj, Object.keys(obj as object).sort());
  let hash = 5381;
  for (let i = 0; i < json.length; i++) {
    hash = ((hash << 5) + hash + json.charCodeAt(i)) | 0;
  }
  return (hash >>> 0).toString(16).padStart(8, '0');
}
