/** Grid from server is row-major [row][reel]; ReelSet expects column-major [reel][row]. */
export function transposeGrid(grid: number[][]): number[][] {
  const rows = grid.length
  const cols = grid[0]?.length ?? 0
  return Array.from({ length: cols }, (_, c) =>
    Array.from({ length: rows }, (_, r) => grid[r]![c]!),
  )
}

/** ClusterHit positions are encoded as `reel * rowCount + row`. */
export function decodePosition(encoded: number, rowCount: number): { reel: number; row: number } {
  return { reel: Math.floor(encoded / rowCount), row: encoded % rowCount }
}
