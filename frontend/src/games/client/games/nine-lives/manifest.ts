import type { GameManifest, AssetManifest } from '@tgslots/shared-contracts'
import { SYMBOLS } from '@tgslots/nine-lives'
export const manifest: GameManifest = {
  gameId: 'nine-lives',
  displayName: 'Nine Lives',
  grid: { reels: 6, rows: 5 },
  reelNaturalWidth: 746,
  reelNaturalHeight: 632,
  symbolSize: 116,
  symbols: SYMBOLS.map((name, id) => ({
    id,
    name,
    kind: id === 0 ? 'wild' : id === 7 ? 'scatter' : id === 6 ? 'bonus' : 'regular',
  })),
  theme: { primary: 0xbe3428, accent: 0xf2d6a3, background: 0x120e0d, text: 0xf7ecd9 },
  features: ['cluster-pays', 'cascade', 'free-spins', 'buy-bonus'],
  winTiers: [
    { thresholdX: 2, copy: 'NICE CATCH' },
    { thresholdX: 10, copy: 'DEAD LUCKY' },
    { thresholdX: 50, copy: 'SOUL COLLECTOR' },
    { thresholdX: 100, copy: 'DEATH JACKPOT' },
  ],
}
export const assets: AssetManifest = {
  images: {
    BACKGROUND: '/assets/images/nine-lives/background.webp',
    ...Object.fromEntries(
      SYMBOLS.map((name) => [name, `/assets/images/nine-lives/${name.toLowerCase()}.webp`]),
    ),
  },
}
