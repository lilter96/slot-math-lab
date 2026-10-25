import type { ClusterHit } from '@tgslots/slots-core'

export function groupHitsBySymbol(hits: readonly ClusterHit[]): Map<number, readonly ClusterHit[]> {
  const map = new Map<number, ClusterHit[]>()
  for (const hit of hits) {
    const arr = map.get(hit.symbolId) ?? []
    arr.push(hit)
    map.set(hit.symbolId, arr)
  }
  return map
}
