import type { GameManifest } from '@tgslots/shared-contracts'

export const manifest: GameManifest = {
  gameId: 'ancient-dragon',
  displayName: 'Ancient Dragon',
  grid: { reels: 5, rows: 3 },
  reelNaturalWidth: 780,
  reelNaturalHeight: 420,
  symbolSize: 140,
  symbols: [
    { id: 0, name: 'GOLDDRAGON', kind: 'wild' },
    { id: 1, name: 'GREENDRAGON', kind: 'regular' },
    { id: 2, name: 'FISH', kind: 'regular' },
    { id: 3, name: 'TURTLE', kind: 'regular' },
    { id: 4, name: 'FAN', kind: 'regular' },
    { id: 5, name: 'LOTUSFLOWER', kind: 'regular' },
    { id: 6, name: 'ACE', kind: 'regular' },
    { id: 7, name: 'KING', kind: 'regular' },
    { id: 8, name: 'QUEEN', kind: 'regular' },
    { id: 9, name: 'JACK', kind: 'regular' },
    { id: 10, name: 'TEN', kind: 'regular' },
    { id: 11, name: 'NINE', kind: 'regular' },
    { id: 12, name: 'YINYANG', kind: 'scatter' },
    { id: 13, name: 'INNER', kind: 'bonus' },
  ],
  theme: {
    primary: 0xcc1a1a,
    accent: 0xd4a017,
    background: 0x1a0000,
    text: 0xffe066,
  },
  winTiers: [
    { thresholdX: 25, copy: 'MEGA WIN!', textureName: 'WIN_MEGA' },
    { thresholdX: 10, copy: 'BIG WIN!', textureName: 'WIN_BIG' },
    { thresholdX: 2, copy: 'WIN!', textureName: 'WIN_SMALL' },
  ],
  features: ['free-spins'],
}
