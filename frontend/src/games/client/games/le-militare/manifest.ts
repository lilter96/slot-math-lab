import type { GameManifest } from '@tgslots/shared-contracts'

export const manifest: GameManifest = {
  gameId: 'le-militare',
  displayName: 'Le Militare',
  grid: { reels: 6, rows: 5 },
  reelNaturalWidth: 740,
  reelNaturalHeight: 600,
  symbolSize: 120,
  symbols: [
    { id: 0, name: 'WILD', kind: 'wild' },
    { id: 1, name: 'BULLET', kind: 'regular' },
    { id: 2, name: 'GRENADE', kind: 'regular' },
    { id: 3, name: 'HELMET', kind: 'regular' },
    { id: 4, name: 'MEDAL', kind: 'regular' },
    { id: 5, name: 'RIFLE', kind: 'regular' },
    { id: 6, name: 'TANK', kind: 'regular' },
    { id: 7, name: 'SOLDIER', kind: 'regular' },
    { id: 8, name: 'GENERAL', kind: 'regular' },
    { id: 9, name: 'PLANE', kind: 'regular' },
    { id: 10, name: 'S300', kind: 'bonus' },
    { id: 11, name: 'SCATTER', kind: 'scatter' },
  ],
  theme: {
    primary: 0xc41e1e,
    accent: 0xd4af37,
    background: 0x1a2e1f,
    text: 0xffffff,
  },
  winTiers: [
    { thresholdX: 100, copy: 'EPIC WIN!', textureName: 'WIN_MEGA' },
    { thresholdX: 25, copy: 'MEGA WIN!', textureName: 'WIN_BIG' },
    { thresholdX: 5, copy: 'BIG WIN!', textureName: 'WIN_SMALL' },
  ],
  features: ['cluster-pays', 'cascade', 'combat-operation', 'free-spins', 'buy-bonus'],
}
