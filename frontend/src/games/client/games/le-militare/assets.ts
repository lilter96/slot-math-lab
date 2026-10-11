import type { AssetManifest } from '@tgslots/shared-contracts'
import impactFrames from './impact-frames.json' with { type: 'json' }
import radarFrames from './radar-turn-frames.json' with { type: 'json' }
import combatFrames from './combat-frames.json' with { type: 'json' }
import mascotFrames from './mascot-frames.json' with { type: 'json' }
import frames1 from './symbol-frames-1.json' with { type: 'json' }
import frames2 from './symbol-frames-2.json' with { type: 'json' }
import frames3 from './symbol-frames-3.json' with { type: 'json' }

export const assets: AssetManifest = {
  images: {
    IMPACT: '/assets/images/le-militare/impact-atlas.png',
    RADAR_TURN: '/assets/images/le-militare/radar-turn-atlas.png',
    COMBAT: '/assets/images/le-militare/combat-atlas.png',
    MASCOT: '/assets/images/le-militare/mascot-atlas.png',
    SYMBOLS_1: '/assets/images/le-militare/symbols-1.png',
    SYMBOLS_2: '/assets/images/le-militare/symbols-2.png',
    SYMBOLS_3: '/assets/images/le-militare/symbols-3.png',
    BACKGROUND_16_9: '/assets/images/le-militare/command-background.webp',
    BACKGROUND_9_16: '/assets/images/le-militare/command-background-portrait.webp',
    WIN_SMALL: '/assets/images/shared/win-panel.png',
    WIN_BIG: '/assets/images/shared/win-panel.png',
    WIN_MEGA: '/assets/images/shared/win-panel.png',
  },
  atlases: [
    { image: 'SYMBOLS_1', frames: frames1 },
    { image: 'SYMBOLS_2', frames: frames2 },
    { image: 'SYMBOLS_3', frames: frames3 },
    { image: 'MASCOT', frames: mascotFrames },
    { image: 'COMBAT', frames: combatFrames },
    { image: 'RADAR_TURN', frames: radarFrames },
    { image: 'IMPACT', frames: impactFrames },
  ],
  audio: {
    'feature-rise': '/assets/sounds/original/feature-rise.mp3',
    'bgm-combat': '/assets/sounds/original/steel-horizon.mp3',
    'spin-start': '/assets/sounds/original/spin-soft.mp3',
    'launch-air': '/assets/sounds/original/launch-air.mp3',
    'impact-soft': '/assets/sounds/original/impact-soft.mp3',
    'reel-stop': '/assets/sounds/original/reel-settle.mp3',
    'win-small': '/assets/sounds/original/win-soft.mp3',
    'win-big': '/assets/sounds/original/win-major.mp3',
  },
}
