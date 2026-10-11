import featureFrames from './feature-frames.json' with { type: 'json' }
import type { AssetManifest } from '@tgslots/shared-contracts'
import frames1 from './symbol-frames-1.json' with { type: 'json' }

import frames2 from './symbol-frames-2.json' with { type: 'json' }

export const assets: AssetManifest = {
  images: {
    FEATURE_ATLAS: '/assets/images/ancient-dragon/feature-guardian.png',
    ACE: '/assets/images/ancient-dragon/rank-ace.png',
    KING: '/assets/images/ancient-dragon/rank-king.png',
    QUEEN: '/assets/images/ancient-dragon/rank-queen.png',
    JACK: '/assets/images/ancient-dragon/rank-jack.png',
    TEN: '/assets/images/ancient-dragon/rank-ten.png',
    NINE: '/assets/images/ancient-dragon/rank-nine.png',
    SYMBOLS_1: '/assets/images/ancient-dragon/symbols-1.png',
    SYMBOLS_2: '/assets/images/ancient-dragon/symbols-2.png',
    BACKGROUND_16_9: '/assets/images/ancient-dragon/shrine-background.webp',
    BACKGROUND_9_16: '/assets/images/ancient-dragon/shrine-background-portrait.webp',
    WIN_SMALL: '/assets/images/shared/win-panel.png',
    WIN_BIG: '/assets/images/shared/win-panel.png',
    WIN_MEGA: '/assets/images/shared/win-panel.png',
  },
  atlases: [
    { image: 'FEATURE_ATLAS', frames: featureFrames },
    { image: 'SYMBOLS_1', frames: frames1 },
    { image: 'SYMBOLS_2', frames: frames2 },
  ],
  audio: {
    'feature-rise': '/assets/sounds/original/feature-rise.mp3',
    'bgm-dragon': '/assets/sounds/original/dragon-sanctuary.mp3',
    'spin-start': '/assets/sounds/original/spin-soft.mp3',
    'reel-stop': '/assets/sounds/original/reel-settle.mp3',
    'win-small': '/assets/sounds/original/win-soft.mp3',
    'win-big': '/assets/sounds/original/win-major.mp3',
  },
}
