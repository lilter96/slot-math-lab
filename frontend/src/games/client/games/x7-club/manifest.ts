import type { GameManifest, AssetManifest } from '@tgslots/shared-contracts'
import { SYMBOLS } from '@tgslots/x7-club'
export const manifest: GameManifest = {
  gameId: 'x7-club',
  displayName: 'X7 Club',
  grid: { reels: 5, rows: 3 },
  reelNaturalWidth: 700,
  reelNaturalHeight: 520,
  symbolSize: 132,
  symbols: SYMBOLS.map((name, id) => ({
    id,
    name,
    kind: id === 5 ? 'wild' : id === 6 ? 'bonus' : 'regular',
  })),
  theme: { primary: 0xa572ff, accent: 0xd9ff43, background: 0x130b22, text: 0xffffff },
  features: ['hold-spin', 'column-boost', 'buy-bonus'],
  winTiers: [
    { thresholdX: 2, copy: 'GOOD VIBES' },
    { thresholdX: 10, copy: 'BIG MOOD' },
    { thresholdX: 50, copy: 'ABSOLUTE CINEMA' },
    { thresholdX: 100, copy: 'LEGEND ENERGY' },
  ],
}
export const assets: AssetManifest = {
  images: {
    BACKGROUND: '/assets/images/x7-club/nightclub.webp',
    ...Object.fromEntries(
      SYMBOLS.map((id) => [id, `/assets/images/x7-club/${id.toLowerCase()}.webp`]),
    ),
  },
}
