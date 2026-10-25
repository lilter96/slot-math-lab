import type { GameManifest } from '@tgslots/shared-contracts'

export const manifest: GameManifest = {
  gameId: 'woodland-whisper',
  displayName: 'Woodland Whisper',
  grid: { reels: 5, rows: 3 },
  reelNaturalWidth: 1344,
  reelNaturalHeight: 768,
  symbolSize: 140,
  symbols: [
    { id: 0, name: 'WOMAN', kind: 'wild' },
    { id: 1, name: 'CHEST', kind: 'regular' },
    { id: 2, name: 'TIARA', kind: 'regular' },
    { id: 3, name: 'MUSHROOMS', kind: 'regular' },
    { id: 4, name: 'PINECONE', kind: 'regular' },
    { id: 5, name: 'A', kind: 'regular' },
    { id: 6, name: 'K', kind: 'regular' },
    { id: 7, name: 'Q', kind: 'regular' },
    { id: 8, name: 'J', kind: 'regular' },
    { id: 9, name: '10', kind: 'regular' },
    { id: 10, name: '9', kind: 'regular' },
    { id: 11, name: 'COIN', kind: 'scatter' },
  ],
  theme: {
    primary: 0xd4a017,
    accent: 0x2d7a2d,
    background: 0x060e04,
    text: 0xffe066,
  },
  winTiers: [
    { thresholdX: 25, copy: 'MEGA WIN!', textureName: 'WIN_MEGA' },
    { thresholdX: 10, copy: 'BIG WIN!', textureName: 'WIN_BIG' },
    { thresholdX: 2, copy: 'WIN!', textureName: 'WIN_SMALL' },
  ],
  features: ['free-spins', 'pick-bonus', 'buy-bonus'],
}
